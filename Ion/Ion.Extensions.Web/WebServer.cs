using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Http;

namespace Ion.Extensions.Web;

/// <summary>
/// The web server: HTTP/1.1 and WebSocket on the shared server core, with the game's <see cref="HttpAttribute"/> and
/// <see cref="WebSocketAttribute"/> methods called on the game thread.
/// </summary>
/// <remarks>
/// <para>
/// Threading. One accept thread and one thread per connection (plus a writer thread per WebSocket) with blocking sockets.
/// A connection thread parses the request, applies the security checks, matches the route and reads the body; static files
/// and the mounted endpoints (<c>/rpc</c>) are answered there. A route's request, and every WebSocket connection, message
/// and disconnection, is put on one bounded lock-free queue; the game thread drains it in <see cref="ProcessFrame"/> (a
/// Last step at <see cref="StageOrder.Web"/>), in arrival order, calling the handlers; a request's response goes back to
/// its waiting connection thread. Nothing in the frame waits on the network. Each connection reuses its buffers, so a
/// small request allocates nothing in steady state, on either thread.
/// </para>
/// <para>
/// Security (the remote protocol's policy). Off unless <see cref="WebOptions.Enabled"/>; loopback unless a non-loopback
/// bind is explicitly allowed (logged); the <c>Host</c> check against DNS rebinding on loopback; requests with an
/// <c>Origin</c> other than the server's own or an allowed one are refused; mutating endpoints need the bearer token when
/// one is configured (always on a LAN bind, unless anonymous mutations are allowed); per-address rate limits; bounded
/// heads, bodies, messages, connections and queues.
/// </para>
/// </remarks>
public sealed class WebServer : IWebServer, IDisposable
{
	private readonly IServiceProvider _services;
	private readonly WebOptions _options;
	private readonly ILogger _logger;
	private readonly BoundedQueue<WorkItem> _queue;
	private readonly HttpRateLimiter _rateLimiter;
	private WebRoute[] _routes = [];
	private WebSocketRoute[] _sockets = [];
	private WebSocketChannel[] _channels = [];
	private object?[] _routeTargets = [];
	private object?[] _socketTargets = [];
	private bool[] _resolved = [];
	private IHttpEndpoint[] _endpoints = [];
	private StaticFiles? _static;
	private HttpSocketListener? _listener;
	private byte[]? _token;
	private int _maxRouteValues = 1;
	private long _requestCount;
	private long _clientIds;
	private volatile bool _disposed;

	/// <summary>Creates the server; it starts on <see cref="Start"/> (the web system's Init step).</summary>
	public WebServer(IServiceProvider services, IOptions<WebOptions> options, ILogger<WebServer>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);
		_services = services;
		_options = options.Value;
		_logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
		_queue = new BoundedQueue<WorkItem>(Math.Max(16, _options.QueueCapacity));
		_rateLimiter = new HttpRateLimiter(_options.RateLimit, _options.RateLimitBurst);
	}

	/// <summary>The options.</summary>
	public WebOptions Options => _options;

	/// <inheritdoc/>
	public bool IsRunning => _listener is not null && !_disposed;

	/// <inheritdoc/>
	public int Port => _listener?.Port ?? 0;

	/// <summary>The bound address, or null when not running.</summary>
	public IPAddress? Address => _listener?.Address;

	/// <inheritdoc/>
	public string? BaseUrl { get; private set; }

	/// <summary>The bearer token mutating endpoints need, or null when none is required.</summary>
	public string? Token { get; private set; }

	/// <inheritdoc/>
	public IReadOnlyList<WebRoute> Routes => _routes;

	/// <inheritdoc/>
	public IReadOnlyList<WebSocketRoute> WebSockets => _sockets;

	/// <inheritdoc/>
	public long RequestCount => Interlocked.Read(ref _requestCount);

	/// <summary>The number of queued requests and messages handed to the game thread so far.</summary>
	public long HandledCount { get; private set; }

	/// <summary>The number of WebSocket messages dropped by the rate limit or a full queue.</summary>
	public long DroppedMessages => Interlocked.Read(ref _dropped);

	private long _dropped;

	/// <summary>The allocated bytes of the connection thread that served the last request, read after it (for the allocation tests).</summary>
	internal long LastServeThreadAllocatedBytes;

	/// <inheritdoc/>
	public WebSocketChannel Channel(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		for (var i = 0; i < _sockets.Length; i++)
		{
			if (string.Equals(_sockets[i].Path, path.Length > 1 ? path.TrimEnd('/') : path, StringComparison.OrdinalIgnoreCase)) return _channels[i];
		}

		throw new KeyNotFoundException($"No [WebSocket] endpoint has the path '{path}'. Endpoints: {string.Join(", ", _sockets.Select(static s => s.Path))}.");
	}

	/// <summary>
	/// Builds the route table, applies the security policy, starts listening and prints the URL. Called by the web system's
	/// Init step; idempotent.
	/// </summary>
	/// <exception cref="WebSecurityException">The bind address is not loopback and <see cref="WebOptions.AllowNonLoopback"/> is off.</exception>
	/// <exception cref="InvalidOperationException">Two routes match the same method and paths, or two WebSocket endpoints share a path.</exception>
	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_listener is not null) return;

		if (!HttpSecurity.TryResolveBind(_options.Bind, out var address)) throw new InvalidOperationException($"Ion:Web:Bind '{_options.Bind}' is not an IP address, 'localhost' or '*'.");
		var loopback = IPAddress.IsLoopback(address);
		if (!loopback && !_options.AllowNonLoopback)
		{
			throw new WebSecurityException($"Refusing to bind the web server to the non-loopback address '{_options.Bind}'. Set Ion:Web:AllowNonLoopback=true to allow it explicitly (anyone on the network can then reach the game's endpoints).");
		}

		BuildRoutes();

		Token = _options.Token is { Length: > 0 } token ? token
			: (_options.GenerateToken || (!loopback && !_options.AllowAnonymousMutations)) ? HttpSecurity.NewToken()
			: null;
		_token = Token is null ? null : Encoding.ASCII.GetBytes(Token);

		if (_options.StaticFiles is { Length: > 0 } folder)
		{
			var root = Path.IsPathRooted(folder) ? folder : Path.Combine(AppContext.BaseDirectory, folder);
			if (Directory.Exists(root)) _static = new StaticFiles(root, _options.DefaultFile, _options.MaxStaticFileBytes);
			else _logger.LogWarning("Ion web: the static file folder {Folder} does not exist; no static files are served.", root);
		}

		if (_options.HostEndpoints) _endpoints = [.. _services.GetServices<IHttpEndpoint>()];

		var settings = new HttpListenerSettings
		{
			Name = "Ion web",
			MaxConnections = _options.MaxConnections,
			BusyBody = "Too many connections."u8.ToArray(),
		};
		_listener = new HttpSocketListener(address, _options.Port, settings, Serve, _logger);
		BaseUrl = $"http://{HttpSecurity.FormatHost(loopback || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? (address.AddressFamily == AddressFamily.InterNetworkV6 && !address.Equals(IPAddress.IPv6Any) ? IPAddress.IPv6Loopback : IPAddress.Loopback) : address)}:{_listener.Port}/";

		if (!loopback)
		{
			_logger.LogWarning("Ion web: listening on the non-loopback address {Address} because Ion:Web:AllowNonLoopback is set. Anyone on the network can reach the game's endpoints{Token}.", address,
				Token is null ? ", including the mutating ones (Ion:Web:AllowAnonymousMutations)" : "; mutating ones need the token");
		}

		_logger.LogInformation("Ion web started on port {Port}: {Routes} routes, {Sockets} WebSocket endpoints, static files {Static}, mounted {Endpoints}.",
			Port, _routes.Length, _sockets.Length, _static?.Root ?? "off", string.Join(", ", _endpoints.Select(static e => e.Path)));

		if (_options.PrintUrl)
		{
			var error = Console.Error;
			error.WriteLine($"Ion web: listening on {BaseUrl}");
			if (!loopback)
			{
				foreach (var lan in LanAddresses(address)) error.WriteLine($"Ion web: on the network at http://{HttpSecurity.FormatHost(lan)}:{Port}/{(Token is null ? "" : "#token=" + Token)}");
			}

			if (Token is not null) error.WriteLine($"Ion web: token {Token}");
			error.Flush();
		}
	}

	private void BuildRoutes()
	{
		var routes = new List<WebRoute>();
		var sockets = new List<WebSocketRoute>();
		IEnumerable<WebRouteTable> tables = _services.GetServices<WebRouteTable>();
		if (_options.UseGeneratedRoutes) tables = WebRoutes.Tables.Concat(tables);
		foreach (var table in tables.Distinct())
		{
			routes.AddRange(table.Routes);
			sockets.AddRange(table.WebSockets);
		}

		var shapes = new Dictionary<string, WebRoute>(StringComparer.Ordinal);
		foreach (var route in routes)
		{
			if (shapes.TryGetValue(route.ShapeKey, out var other))
			{
				throw new InvalidOperationException($"The routes {other.Name} and {route.Name} both match {route.Method} {route.Template}.");
			}

			shapes.Add(route.ShapeKey, route);
		}

		var paths = new Dictionary<string, WebSocketRoute>(StringComparer.OrdinalIgnoreCase);
		foreach (var socket in sockets)
		{
			if (!paths.TryAdd(socket.Path, socket)) throw new InvalidOperationException($"The WebSocket endpoints {paths[socket.Path].Name} and {socket.Name} share the path {socket.Path}.");
		}

		// Stable sort: the most specific first, table order among equals.
		_routes = [.. routes.Select(static (r, i) => (r, i)).OrderBy(static x => x.r, Comparer<WebRoute>.Create(WebRoute.CompareSpecificity)).ThenBy(static x => x.i).Select(static x => x.r)];
		_sockets = [.. sockets];
		_channels = [.. _sockets.Select(static s => new WebSocketChannel(s.Path))];
		_routeTargets = new object?[_routes.Length];
		_socketTargets = new object?[_sockets.Length];
		_resolved = new bool[_routes.Length + _sockets.Length];
		_maxRouteValues = Math.Max(1, _routes.Length == 0 ? 1 : _routes.Max(static r => r.RouteValueCount));
	}

	private static IEnumerable<IPAddress> LanAddresses(IPAddress bound)
	{
		if (!bound.Equals(IPAddress.Any) && !bound.Equals(IPAddress.IPv6Any)) return [bound];
		try
		{
			return [.. NetworkInterface.GetAllNetworkInterfaces()
				.Where(static n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
				.SelectMany(static n => n.GetIPProperties().UnicastAddresses)
				.Select(static a => a.Address)
				.Where(a => a.AddressFamily == (bound.Equals(IPAddress.Any) ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6) && !IPAddress.IsLoopback(a))];
		}
		catch (NetworkInformationException)
		{
			return [];
		}
	}

	/// <summary>
	/// Hands queued requests and WebSocket messages to their handlers, in arrival order, at most
	/// <see cref="WebOptions.MaxRequestsPerFrame"/> per call. Called on the game thread by the web system's Last step.
	/// </summary>
	public void ProcessFrame()
	{
		if (_listener is null || _disposed) return;
		var budget = _options.MaxRequestsPerFrame;
		while (budget-- > 0 && _queue.TryDequeue(out var item))
		{
			HandledCount++;
			item.Execute(this);
		}
	}

	private object? Target(int index, bool socket)
	{
		var slot = socket ? _routes.Length + index : index;
		if (!_resolved[slot])
		{
			_resolved[slot] = true;
			var type = socket ? _sockets[index].SystemType : _routes[index].SystemType;
			try
			{
				var target = socket ? _sockets[index].Resolve(_services) : _routes[index].Resolve(_services);
				if (socket) _socketTargets[index] = target;
				else _routeTargets[index] = target;
				if (target is null) _logger.LogWarning("Ion web: {System} is not registered, so its endpoints answer 503. Register it (AddSingleton) to serve them.", type.Name);
			}
			catch (InvalidOperationException ex)
			{
				_logger.LogWarning(ex, "Ion web: {System} could not be resolved from the root services (a scoped system?); its endpoints answer 503.", type.Name);
			}
		}

		return socket ? _socketTargets[index] : _routeTargets[index];
	}

	// ---- Connection threads ----

	private void Serve(HttpConnection connection)
	{
		var state = new ConnectionState(connection, _rateLimiter.GetBucket(connection.RemoteAddress), _maxRouteValues);
		connection.State = state;
		var loopback = _listener!.IsLoopback;
		while (connection.TryReadRequest(out var status))
		{
			Interlocked.Increment(ref _requestCount);
			if (status != HttpParseStatus.Ok)
			{
				Answer(connection, HttpStatus.FromParseStatus(status), "Malformed HTTP request."u8, keepAlive: false);
				return;
			}

			var request = connection.Request;
			// An early answer cannot keep the connection when a body is still unread.
			var canKeep = request.KeepAlive && request.ContentLength <= 0 && !request.IsChunked;
			if (!HttpSecurity.CheckHost(request, loopback))
			{
				Answer(connection, 403, "The Host header must name a loopback host (localhost, 127.0.0.1 or [::1])."u8, keepAlive: false);
				return;
			}

			if (!HttpSecurity.CheckOrigin(request, _options.AllowedOrigins, allowSameOrigin: true))
			{
				Answer(connection, 403, "Requests from this browser origin are refused (Ion:Web:AllowedOrigins)."u8, keepAlive: false);
				return;
			}

			if (!state.Bucket.TryTake())
			{
				connection.WriteResponse(429, "text/plain"u8, "Too many requests."u8, canKeep, "Retry-After: 1\r\n"u8);
				if (!canKeep) return;
				continue;
			}

			var endpoint = FindEndpoint(request.RawPath);
			if (endpoint is not null)
			{
				if (!endpoint.Serve(connection)) return;
				continue;
			}

			if (request.IsWebSocketUpgrade)
			{
				ServeWebSocket(connection, state);
				return;
			}

			if (request.Method == HttpVerb.Options)
			{
				ServePreflight(connection, state, canKeep);
				if (!canKeep) return;
				continue;
			}

			if (!ServeRoute(connection, state, canKeep)) return;
			LastServeThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
		}
	}

	private IHttpEndpoint? FindEndpoint(ReadOnlySpan<byte> path)
	{
		foreach (var endpoint in _endpoints)
		{
			if (Ascii.Equals(path, endpoint.Path)) return endpoint;
		}

		return null;
	}

	/// <summary>Serves a request that is not an upgrade, a preflight or a mounted endpoint; returns whether the connection stays open.</summary>
	private bool ServeRoute(HttpConnection connection, ConnectionState state, bool canKeep)
	{
		var request = connection.Request;
		var path = request.RawPath;
		var values = state.Values;
		var index = -1;
		var pathMatched = false;
		for (var i = 0; i < _routes.Length; i++)
		{
			if (_routes[i].Verb != request.Method || !_routes[i].TryMatch(path, values)) continue;
			index = i;
			break;
		}

		if (index < 0 && request.Method == HttpVerb.Head)
		{
			for (var i = 0; i < _routes.Length; i++)
			{
				if (_routes[i].Verb != HttpVerb.Get || !_routes[i].TryMatch(path, values)) continue;
				index = i;
				break;
			}
		}

		if (index < 0)
		{
			for (var i = 0; i < _routes.Length && !pathMatched; i++) pathMatched = _routes[i].TryMatch(path, values);
			if (pathMatched)
			{
				var headers = state.Headers.Clear().Append("Allow: "u8);
				var first = true;
				for (var i = 0; i < _routes.Length; i++)
				{
					if (!_routes[i].TryMatch(path, values)) continue;
					if (!first) headers.Append(", "u8);
					headers.AppendAscii(_routes[i].Method);
					first = false;
				}

				headers.Append("\r\n"u8);
				AppendCors(state.Headers, request);
				connection.WriteResponse(405, "text/plain"u8, "Method not allowed."u8, canKeep, state.Headers.Span, request.Method == HttpVerb.Head);
				return canKeep;
			}

			if (_static is not null && request.Method is HttpVerb.Get or HttpVerb.Head)
			{
				AppendCors(state.Headers.Clear(), request);
				if (_static.TryServe(connection, path, request.Method == HttpVerb.Head, canKeep, state.Headers.Span)) return canKeep;
			}

			Answer(connection, 404, "Not found."u8, canKeep, request.Method == HttpVerb.Head);
			return canKeep;
		}

		var route = _routes[index];
		if (request.ContentLength > _options.MaxRequestBytes)
		{
			Answer(connection, 413, "The request body is too large (Ion:Web:MaxRequestBytes)."u8, keepAlive: false);
			return false;
		}

		var authenticated = IsAuthenticated(request);
		if (route.Access == WebAccess.Mutate && !authenticated)
		{
			connection.WriteResponse(401, "text/plain"u8, "This endpoint changes the game: send 'Authorization: Bearer <token>' (Ion:Web:Token)."u8, canKeep,
				"Cache-Control: no-store\r\nWWW-Authenticate: Bearer realm=\"ion-web\"\r\n"u8);
			return canKeep;
		}

		switch (connection.ReadBody(_options.MaxRequestBytes))
		{
			case HttpBodyStatus.TooLarge:
				Answer(connection, 413, "The request body is too large (Ion:Web:MaxRequestBytes)."u8, keepAlive: false);
				return false;
			case HttpBodyStatus.Malformed:
				Answer(connection, 400, "Malformed chunked body."u8, keepAlive: false);
				return false;
		}

		var exchange = state.Exchange;
		exchange.Prepare(index, route.RouteValueCount, authenticated);
		if (!_queue.TryEnqueue(exchange))
		{
			exchange.State = ExchangeState.Idle;
			Answer(connection, 503, "The game is busy (Ion:Web:QueueCapacity)."u8, request.KeepAlive);
			return request.KeepAlive;
		}

		if (!exchange.Done.Wait(_options.RequestTimeoutMs))
		{
			if (Interlocked.CompareExchange(ref exchange.StateValue, (int)ExchangeState.Abandoned, (int)ExchangeState.Queued) == (int)ExchangeState.Queued)
			{
				Answer(connection, 504, "The game thread did not answer in time (is the game loop running, or paused by the remote protocol?)."u8, keepAlive: false);
				return false;
			}

			// The handler is running: its answer is moments away.
			exchange.Done.Wait();
		}

		if (exchange.State != ExchangeState.Completed)
		{
			Answer(connection, 503, "The web server is stopping."u8, keepAlive: false);
			return false;
		}

		var response = exchange.Response;
		var keepAlive = request.KeepAlive;
		var headOnly = request.Method == HttpVerb.Head;
		var extra = state.Headers.Clear().Append("Cache-Control: no-store\r\n"u8).Append(response.HeaderBytes);
		AppendCors(extra, request);
		if (!response.IsWritten)
		{
			connection.WriteResponse(204, default, default, keepAlive, extra.Span, headOnly);
		}
		else
		{
			var contentType = response.ContentType is null ? default : state.ContentType.Clear().AppendAscii(response.ContentType).Span;
			var noBody = response.Status is 204 or 304;
			connection.WriteResponse(response.Status, noBody ? default : contentType, noBody ? default : response.WrittenSpan, keepAlive, extra.Span, headOnly);
		}

		exchange.State = ExchangeState.Idle;
		return keepAlive;
	}

	private bool IsAuthenticated(HttpRequest request) =>
		_token is null || (HttpSecurity.TryGetBearer(request, out var presented) && HttpSecurity.TokenEquals(presented, _token));

	private void ServePreflight(HttpConnection connection, ConnectionState state, bool keepAlive)
	{
		var request = connection.Request;
		var headers = state.Headers.Clear();
		if (request.TryGetHeader("Origin"u8, out _) && AppendCors(headers, request))
		{
			headers.Append("Access-Control-Allow-Methods: GET, HEAD, POST, PUT, PATCH, DELETE\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Max-Age: 600\r\n"u8);
		}

		headers.Append("Allow: GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS\r\n"u8);
		connection.WriteResponse(204, default, default, keepAlive, headers.Span);
	}

	/// <summary>Adds CORS headers when the request comes from an allowed cross origin; returns whether it did.</summary>
	private bool AppendCors(ByteBuilder headers, HttpRequest request)
	{
		if (_options.AllowedOrigins.Count == 0 || !request.TryGetHeader("Origin"u8, out var origin)) return false;
		foreach (var allowed in _options.AllowedOrigins)
		{
			if (!Ascii.Equals(origin, allowed)) continue;
			headers.Append("Access-Control-Allow-Origin: "u8).Append(origin).Append("\r\nVary: Origin\r\n"u8);
			return true;
		}

		return false;
	}

	private static void Answer(HttpConnection connection, int status, ReadOnlySpan<byte> text, bool keepAlive, bool headOnly = false) =>
		connection.WriteResponse(status, "text/plain; charset=utf-8"u8, text, keepAlive, "Cache-Control: no-store\r\n"u8, headOnly);

	private void ServeWebSocket(HttpConnection connection, ConnectionState state)
	{
		var request = connection.Request;
		var index = -1;
		for (var i = 0; i < _sockets.Length; i++)
		{
			if (!_sockets[i].Matches(request.RawPath)) continue;
			index = i;
			break;
		}

		if (index < 0)
		{
			Answer(connection, 404, "No WebSocket endpoint at this path."u8, keepAlive: false);
			return;
		}

		var route = _sockets[index];
		var offersIon = false;
		var authenticated = IsAuthenticated(request);
		if (request.TryGetHeader("Sec-WebSocket-Protocol"u8, out var protocols))
		{
			// Browsers cannot set headers on a WebSocket: the token may come as an offered subprotocol "bearer.<token>".
			while (protocols.Length > 0)
			{
				var comma = protocols.IndexOf((byte)',');
				var item = (comma < 0 ? protocols : protocols[..comma]).Trim(" \t"u8);
				if (item.SequenceEqual("ion"u8)) offersIon = true;
				else if (_token is not null && item.StartsWith("bearer."u8) && HttpSecurity.TokenEquals(item[7..], _token)) authenticated = true;
				if (comma < 0) break;
				protocols = protocols[(comma + 1)..];
			}
		}

		if (route.Access == WebAccess.Mutate && !authenticated)
		{
			connection.WriteResponse(401, "text/plain"u8, "This endpoint needs the token: 'Authorization: Bearer <token>', or offer the subprotocols 'ion' and 'bearer.<token>'."u8, keepAlive: false,
				"WWW-Authenticate: Bearer realm=\"ion-web\"\r\n"u8);
			return;
		}

		var settings = new WebSocketSettings { MaxMessageBytes = _options.MaxWebSocketMessageBytes, SendQueueCapacity = 1024, WriterThreadName = "Ion web WebSocket writer" };
		using var socket = connection.AcceptWebSocket(settings, offersIon ? "ion"u8 : default);
		if (socket is null) return;

		var client = new WebSocketClient(Interlocked.Increment(ref _clientIds), socket, _channels[index], connection.RemoteAddress, authenticated);
		var session = new WebSocketSession(index, client, Math.Max(2, _options.MaxPendingMessagesPerClient));
		if (!EnqueueReliably(session.Connected)) return;

		while (socket.ReadMessage(out var opcode, out var payload))
		{
			if (!state.Bucket.TryTake())
			{
				Interlocked.Increment(ref _dropped);
				continue;
			}

			if (!session.Free.TryDequeue(out var message))
			{
				// The game is not keeping up with this client: drop it rather than grow.
				Interlocked.Increment(ref _dropped);
				socket.Close(1008);
				break;
			}

			message.Set(opcode == WebSocketOpcode.Binary ? WebSocketMessageKind.Binary : WebSocketMessageKind.Text, payload);
			if (!_queue.TryEnqueue(message))
			{
				Interlocked.Increment(ref _dropped);
				session.Free.TryEnqueue(message);
			}
		}

		EnqueueReliably(session.Disconnected);
	}

	/// <summary>Enqueues a connection change, waiting for room (the game thread drains the queue every frame).</summary>
	private bool EnqueueReliably(WorkItem item)
	{
		for (var i = 0; i < 2000 && !_disposed; i++)
		{
			if (_queue.TryEnqueue(item)) return true;
			Thread.Sleep(5);
		}

		return false;
	}

	/// <summary>Stops listening, closes every connection and releases waiting requests.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		foreach (var channel in _channels) channel.CloseAll();
		_listener?.Dispose();
		while (_queue.TryDequeue(out var item))
		{
			if (item is HttpExchange exchange && Interlocked.CompareExchange(ref exchange.StateValue, (int)ExchangeState.Abandoned, (int)ExchangeState.Queued) == (int)ExchangeState.Queued) exchange.Done.Set();
		}
	}

	// ---- Work items (executed on the game thread) ----

	private abstract class WorkItem
	{
		public abstract void Execute(WebServer server);
	}

	private enum ExchangeState
	{
		Idle,
		Queued,
		Running,
		Completed,
		Abandoned,
	}

	/// <summary>A connection's request on its way to the game thread and back (one per connection, reused).</summary>
	private sealed class HttpExchange(HttpConnection connection, int maxRouteValues) : WorkItem
	{
		public readonly HttpConnection Connection = connection;
		public readonly WebResponseWriter Response = new();
		public readonly ManualResetEventSlim Done = new(false, spinCount: 0);
		public readonly Range[] Values = new Range[maxRouteValues];
		public int StateValue;
		public int RouteIndex;
		public int ValueCount;
		public bool Authenticated;

		public ExchangeState State
		{
			get => (ExchangeState)Volatile.Read(ref StateValue);
			set => Volatile.Write(ref StateValue, (int)value);
		}

		public void Prepare(int routeIndex, int valueCount, bool authenticated)
		{
			RouteIndex = routeIndex;
			ValueCount = valueCount;
			Authenticated = authenticated;
			Response.Reset();
			Done.Reset();
			State = ExchangeState.Queued;
		}

		public override void Execute(WebServer server)
		{
			if (Interlocked.CompareExchange(ref StateValue, (int)ExchangeState.Running, (int)ExchangeState.Queued) != (int)ExchangeState.Queued) return;
			var route = server._routes[RouteIndex];
			try
			{
				var target = server.Target(RouteIndex, socket: false);
				if (target is null)
				{
					Response.WriteText($"The system {route.SystemType.Name} that serves this endpoint is not registered.", "text/plain; charset=utf-8", 503);
				}
				else
				{
					var request = new WebRequest(Connection.Request, Values, ValueCount, Authenticated, Connection.RemoteAddress);
					route.Handler(target, in request, new WebResponse(Response));
				}
			}
			catch (Exception ex)
			{
				server._logger.LogError(ex, "Ion web: {Route} failed.", route.Name);
				Response.Reset();
				var detail = server._listener?.IsLoopback == true ? $"{route.Name} failed: {ex.GetType().Name}: {ex.Message}" : "The endpoint failed.";
				Response.WriteText(detail, "text/plain; charset=utf-8", 500);
			}
			finally
			{
				State = ExchangeState.Completed;
				Done.Set();
			}
		}
	}

	/// <summary>One WebSocket client's connection changes and messages (pooled per client).</summary>
	private sealed class WebSocketSession
	{
		public WebSocketSession(int routeIndex, WebSocketClient client, int capacity)
		{
			RouteIndex = routeIndex;
			Client = client;
			Free = new BoundedQueue<WebSocketEvent>(capacity);
			for (var i = 0; i < Free.Capacity; i++) Free.TryEnqueue(new WebSocketEvent(this, WebSocketMessageKind.Text));
			Connected = new WebSocketEvent(this, WebSocketMessageKind.Connected);
			Disconnected = new WebSocketEvent(this, WebSocketMessageKind.Disconnected);
		}

		public int RouteIndex { get; }

		public WebSocketClient Client { get; }

		public BoundedQueue<WebSocketEvent> Free { get; }

		public WebSocketEvent Connected { get; }

		public WebSocketEvent Disconnected { get; }

		public bool Refused { get; set; }
	}

	private sealed class WebSocketEvent(WebSocketSession session, WebSocketMessageKind kind) : WorkItem
	{
		private byte[] _data = kind is WebSocketMessageKind.Text or WebSocketMessageKind.Binary ? new byte[256] : [];
		private int _length;
		private WebSocketMessageKind _kind = kind;

		public void Set(WebSocketMessageKind kind, ReadOnlySpan<byte> payload)
		{
			_kind = kind;
			if (_data.Length < payload.Length) _data = new byte[Math.Max(payload.Length, _data.Length * 2)];
			payload.CopyTo(_data);
			_length = payload.Length;
		}

		public override void Execute(WebServer server)
		{
			var client = session.Client;
			var route = server._sockets[session.RouteIndex];
			try
			{
				var target = server.Target(session.RouteIndex, socket: true);
				if (target is null)
				{
					if (_kind == WebSocketMessageKind.Connected) client.Close(1011);
					session.Refused = true;
					return;
				}

				if (session.Refused) return;
				if (_kind == WebSocketMessageKind.Connected) client.Channel.Add(client);
				var message = new WebSocketMessage(_kind, client, _data.AsSpan(0, _length));
				route.Handler(target, in message);
			}
			catch (Exception ex)
			{
				server._logger.LogError(ex, "Ion web: {Route} failed on a {Kind} message.", route.Name, _kind);
			}
			finally
			{
				if (_kind == WebSocketMessageKind.Disconnected) client.Channel.Remove(client);
				else if (_kind is WebSocketMessageKind.Text or WebSocketMessageKind.Binary) session.Free.TryEnqueue(this);
			}
		}
	}

	/// <summary>A connection's reusable state.</summary>
	private sealed class ConnectionState(HttpConnection connection, HttpRateLimiter.Bucket bucket, int maxRouteValues)
	{
		public HttpRateLimiter.Bucket Bucket { get; } = bucket;

		public HttpExchange Exchange { get; } = new(connection, maxRouteValues);

		public Range[] Values => Exchange.Values;

		public ByteBuilder Headers { get; } = new(512);

		public ByteBuilder ContentType { get; } = new(64);
	}
}

/// <summary>A growable byte buffer for composing header lines without allocating in steady state.</summary>
internal sealed class ByteBuilder(int capacity)
{
	private byte[] _buffer = new byte[capacity];
	private int _length;

	public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

	public ByteBuilder Clear()
	{
		_length = 0;
		return this;
	}

	public ByteBuilder Append(ReadOnlySpan<byte> bytes)
	{
		Ensure(bytes.Length);
		bytes.CopyTo(_buffer.AsSpan(_length));
		_length += bytes.Length;
		return this;
	}

	public ByteBuilder AppendAscii(string text)
	{
		Ensure(text.Length);
		_length += Encoding.ASCII.GetBytes(text, _buffer.AsSpan(_length));
		return this;
	}

	private void Ensure(int more)
	{
		if (_length + more > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_length + more, _buffer.Length * 2));
	}
}
