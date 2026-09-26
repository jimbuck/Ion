using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Http;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Remote;

/// <summary>
/// The remote inspection server: JSON-RPC 2.0 over HTTP, WebSocket and stdio, modelled on the Bevy Remote Protocol.
/// </summary>
/// <remarks>
/// <para>
/// Threading. Transports run on their own threads with blocking sockets and streams; they parse and authenticate requests
/// and queue them. The game thread applies the queue in one step at the end of every frame (<see cref="StageOrder.Remote"/>
/// in Last, see <see cref="ProcessFrame"/>): every handler, read or mutate, runs there, at a stage boundary, so it sees a
/// consistent frame and never races the game. Nothing in the frame waits on the network.
/// </para>
/// <para>
/// Security. Off unless <see cref="RemoteOptions.Enabled"/>; binds to loopback unless a non-loopback bind is explicitly
/// allowed; HTTP and WebSocket clients present a per-run bearer token (a read token, and a mutate token only when
/// mutations are allowed), written with owner-only permissions to the token file and printed once; requests from browser
/// origins are refused unless listed, and the Host header must name a loopback host when bound to loopback (DNS rebinding);
/// stdio needs no token but gets the mutate scope only when mutations are allowed. Mutations are idempotent per request id
/// (a repeated id with the same method and params replays the stored response) and are refused while a scene is loading.
/// </para>
/// </remarks>
public sealed class RemoteServer : IDisposable
{
	private readonly IServiceProvider _services;
	private readonly RemoteOptions _options;
	private readonly ILogger _logger;
	private readonly ConcurrentQueue<PendingRequest> _queue = new();
	private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
	private readonly List<Watch> _watches = [];
	private readonly IdempotencyCache _idempotency;
	private readonly List<IDisposable> _transports = [];
	private readonly GameLoopContext? _loop;
	private readonly List<RemoteEventSource> _eventSources;
	private readonly RemoteEventLog _eventLog = new();
	private SceneSystem? _scenes;
	private bool _scenesResolved;
	private volatile bool _disposed;
	private bool _started;
	private bool _paused;
	private int _stepRemaining;
	private PendingRequest? _stepRequest;
	private byte[]? _readTokenBytes;
	private byte[]? _mutateTokenBytes;
	private RemoteHttpEndpoint? _httpEndpoint;

	/// <summary>Creates the server. It starts on <see cref="Start"/> (the remote system's Init step).</summary>
	public RemoteServer(IServiceProvider services, IOptions<RemoteOptions> options, ILogger<RemoteServer> logger)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);
		_services = services;
		_options = options.Value;
		_logger = logger;
		_idempotency = new IdempotencyCache(Math.Max(1, _options.IdempotencyCacheSize));
		_loop = services.GetService<GameLoopContext>();
		_eventSources = [.. services.GetServices<RemoteEventSource>()];
		Methods = new RemoteMethodRegistry();
		_paused = _options.StartPaused;
	}

	/// <summary>The options.</summary>
	public RemoteOptions Options => _options;

	/// <summary>The registered methods (complete after <see cref="Start"/>).</summary>
	public RemoteMethodRegistry Methods { get; }

	/// <summary>Whether the server has started.</summary>
	public bool IsStarted => _started;

	/// <summary>The HTTP port actually bound (after <see cref="Start"/>), or 0.</summary>
	public int Port { get; private set; }

	/// <summary>The read token (after <see cref="Start"/>).</summary>
	public string? ReadToken { get; private set; }

	/// <summary>The mutate token (after <see cref="Start"/>), or null when mutations are not allowed.</summary>
	public string? MutateToken { get; private set; }

	/// <summary>The token file written at start, or null when no HTTP transport runs.</summary>
	public string? TokenFilePath { get; private set; }

	/// <summary>Whether the game is paused by the remote protocol (the game thread serves requests inside the remote step).</summary>
	public bool IsPaused => _paused;

	/// <summary>The number of requests applied so far.</summary>
	public long AppliedCount { get; private set; }

	/// <summary>The recent events, for <c>events.tail</c>.</summary>
	internal RemoteEventLog EventLog => _eventLog;

	/// <summary>
	/// Registers the methods, creates the tokens, starts the configured transports and writes the token file. Called by the
	/// remote system's Init step; idempotent.
	/// </summary>
	/// <exception cref="RemoteSecurityException">The bind address is not a loopback address and <see cref="RemoteOptions.AllowNonLoopback"/> is off.</exception>
	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_started) return;

		new CoreRemoteMethods(this).Register(Methods);
		foreach (var provider in _services.GetServices<IRemoteMethodProvider>()) provider.Register(Methods);

		var events = _services.GetService<IEvents>();
		if (events is not null)
		{
			foreach (var source in _eventSources) source.Attach(events);
		}

		ReadToken = HttpSecurity.NewToken();
		MutateToken = _options.AllowMutations ? HttpSecurity.NewToken() : null;
		_readTokenBytes = Encoding.ASCII.GetBytes(ReadToken);
		_mutateTokenBytes = MutateToken is null ? null : Encoding.ASCII.GetBytes(MutateToken);

		var transport = _options.Transport;
		if (transport is RemoteTransport.Http or RemoteTransport.Both)
		{
			var address = ResolveBind(_options.Bind);
			if (!IPAddress.IsLoopback(address))
			{
				if (!_options.AllowNonLoopback)
				{
					throw new RemoteSecurityException($"Refusing to bind the remote server to the non-loopback address '{_options.Bind}'. Set Ion:Remote:AllowNonLoopback=true to allow it explicitly (anyone who can reach the port and has a token can then inspect the game).");
				}

				_logger.LogWarning("Ion remote: binding to the non-loopback address {Address} because Ion:Remote:AllowNonLoopback is set. Anyone who can reach this port and holds a token can inspect{Mutate} the game.", address, _options.AllowMutations ? " and change" : "");
			}

			var http = new HttpTransport(this, address, _options.Port, _logger);
			_transports.Add(http);
			Port = http.Port;
			WriteTokenFile(address);
		}

		if (transport is RemoteTransport.Stdio or RemoteTransport.Both)
		{
			var input = Console.OpenStandardInput();
			var output = Console.OpenStandardOutput();
			// Everything else the process writes to standard output would corrupt the protocol stream.
			Console.SetOut(Console.Error);
			AttachStream(input, output, "stdio");
		}

		_started = true;
		_logger.LogInformation("Ion remote started: {Methods} methods, transport {Transport}, mutations {Mutations}.", Methods.Methods.Count, transport, _options.AllowMutations ? "allowed" : "not allowed");

		if (_options.PrintToken && Port != 0)
		{
			var error = Console.Error;
			error.WriteLine($"Ion remote: listening on http://{FormatHost(ResolveBind(_options.Bind))}:{Port}/ (WebSocket /ws); token file {TokenFilePath}");
			error.WriteLine($"Ion remote: read token {ReadToken}");
			if (MutateToken is not null) error.WriteLine($"Ion remote: mutate token {MutateToken}");
			error.Flush();
		}
	}

	/// <summary>
	/// Serves the protocol as newline-delimited JSON over <paramref name="input"/> and <paramref name="output"/> (the stdio
	/// transport over any pair of streams; used by tests and by hosts that embed a game). No token: the session gets the
	/// mutate scope when mutations are allowed.
	/// </summary>
	public void AttachStream(Stream input, Stream output, string name = "stream")
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(output);
		var access = _options.AllowMutations ? RemoteAccess.Mutate : RemoteAccess.Read;
		var transport = new StreamTransport(this, input, output, access, name);
		lock (_transports) _transports.Add(transport);
	}

	/// <summary>Pauses the game at the end of the current frame (the game thread then serves requests until resumed).</summary>
	public void Pause() => _paused = true;

	/// <summary>Resumes a paused game.</summary>
	public void Resume()
	{
		_paused = false;
		_signal.Release();
	}

	/// <summary>
	/// Applies queued requests and re-evaluates watches. Called on the game thread by the remote system's Last step; while
	/// paused it blocks there, serving requests, until resumed, stepped, or the loop is stopped.
	/// </summary>
	public void ProcessFrame()
	{
		if (!_started || _disposed) return;

		PollEvents();
		Drain(Math.Max(1, _options.MaxRequestsPerFrame));
		EvaluateWatches();

		var frame = _loop?.Frame ?? 0;
		if (_options.PauseAtFrame >= 0 && frame + 1 == _options.PauseAtFrame)
		{
			_paused = true;
			_logger.LogInformation("Ion remote: paused after frame {Frame} (Ion:Remote:PauseAtFrame).", frame);
		}

		if (_stepRemaining > 0 && --_stepRemaining == 0)
		{
			_paused = true;
			if (_stepRequest is { } step)
			{
				_stepRequest = null;
				Respond(step, RemoteMessages.Result(step.Id, new JsonObject { ["frame"] = frame, ["paused"] = true }));
			}
		}

		while (_paused && !_disposed && _loop?.Loop?.IsExitRequested != true)
		{
			_signal.Wait(50);
			Drain(int.MaxValue);
			EvaluateWatches();
		}
	}

	internal void Enqueue(PendingRequest request)
	{
		if (_disposed) return;
		_queue.Enqueue(request);
		_signal.Release();
	}

	/// <summary>The access a bearer token grants, or null for an unknown or missing token. Constant-time comparison.</summary>
	internal RemoteAccess? Authenticate(string? authorization)
	{
		if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
		return AuthenticateToken(Encoding.UTF8.GetBytes(authorization["Bearer ".Length..].Trim()));
	}

	/// <summary>The access the request's <c>Authorization: Bearer</c> token grants, or null.</summary>
	internal RemoteAccess? Authenticate(HttpRequest request) =>
		HttpSecurity.TryGetBearer(request, out var token) ? AuthenticateToken(token) : null;

	private RemoteAccess? AuthenticateToken(ReadOnlySpan<byte> token)
	{
		if (_mutateTokenBytes is { } mutate && HttpSecurity.TokenEquals(token, mutate)) return RemoteAccess.Mutate;
		if (_readTokenBytes is { } read && HttpSecurity.TokenEquals(token, read)) return RemoteAccess.Read;
		return null;
	}

	/// <summary>The protocol as an HTTP endpoint: served by the HTTP transport, and mounted at <c>/rpc</c> by the web module.</summary>
	internal RemoteHttpEndpoint HttpEndpoint => _httpEndpoint ??= new RemoteHttpEndpoint(this);

	/// <summary>
	/// Parses and validates one incoming JSON-RPC message on a transport thread: errors that need no game state (parse,
	/// shape, unknown method, missing scope, watch over plain HTTP) are answered at once; valid requests are queued.
	/// Returns false when nothing will be sent (a notification).
	/// </summary>
	internal bool HandleIncoming(RemoteConnection connection, ReadOnlySpan<byte> json)
	{
		JsonNode? node;
		try
		{
			node = JsonNode.Parse(json);
		}
		catch (JsonException ex)
		{
			connection.Send(RemoteMessages.Error(null, RemoteErrorCodes.ParseError, $"Parse error: {ex.Message}"));
			return true;
		}

		if (node is not JsonObject request)
		{
			connection.Send(RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, node is JsonArray ? "Batch requests are not supported; send one request per message." : "A request must be a JSON object."));
			return true;
		}

		var id = request["id"]?.DeepClone();
		var hasId = request.ContainsKey("id");
		if (request["jsonrpc"] is not JsonValue version || version.GetValueKind() != JsonValueKind.String || version.GetValue<string>() != "2.0")
		{
			connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.InvalidRequest, "The request must have \"jsonrpc\": \"2.0\"."));
			return true;
		}

		if (request["method"] is not JsonValue methodValue || methodValue.GetValueKind() != JsonValueKind.String)
		{
			connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.InvalidRequest, "The request must have a string \"method\"."));
			return true;
		}

		var method = methodValue.GetValue<string>();
		var watch = method.EndsWith(WatchSuffix, StringComparison.Ordinal);
		var name = watch ? method[..^WatchSuffix.Length] : method;
		var parameters = request["params"]?.DeepClone();

		if (parameters is not null and not JsonObject)
		{
			if (hasId) connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.InvalidParams, "\"params\" must be an object."));
			return hasId;
		}

		var target = Methods.Find(name);
		if (target is null && name != UnwatchMethod)
		{
			if (hasId) connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.MethodNotFound, $"Method not found: '{method}'. Call rpc.discover for the list."));
			return hasId;
		}

		if (target is { Access: RemoteAccess.Mutate } && connection.Access != RemoteAccess.Mutate)
		{
			if (hasId) connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.Forbidden, MutateForbiddenMessage(connection)));
			return hasId;
		}

		if (watch)
		{
			if (target is null || !target.Watchable)
			{
				if (hasId) connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.InvalidRequest, $"'{name}' cannot be watched."));
				return hasId;
			}

			if (!connection.CanStream || !hasId)
			{
				if (hasId) connection.Send(RemoteMessages.Error(id, RemoteErrorCodes.Unsupported, "Watches need a streaming transport (WebSocket or stdio) and a request id."));
				return hasId;
			}
		}

		Enqueue(new PendingRequest(connection, hasId ? id : null, name, parameters, watch) { });
		return hasId;
	}

	private string MutateForbiddenMessage(RemoteConnection connection) => _options.AllowMutations
		? "This session has the read scope; use the mutate token for this method."
		: $"Mutations are not allowed: start the game with --remote-allow-mutations (Ion:Remote:AllowMutations=true). The {connection.Transport} session is read-only.";

	private void Drain(int budget)
	{
		while (budget-- > 0 && _queue.TryDequeue(out var request))
		{
			Dispatch(request);
		}
	}

	private void Dispatch(PendingRequest request)
	{
		if (request.Connection.IsClosed) return;
		AppliedCount++;

		if (request.Method == UnwatchMethod)
		{
			Unwatch(request);
			return;
		}

		var method = Methods.Find(request.Method)!;
		if (method.Access == RemoteAccess.Mutate)
		{
			if (request.Connection.Access != RemoteAccess.Mutate)
			{
				Respond(request, RemoteMessages.Error(request.Id, RemoteErrorCodes.Forbidden, MutateForbiddenMessage(request.Connection)));
				return;
			}

			var key = request.IdText is null ? null : IdempotencyKey(request);
			if (key is not null && _idempotency.TryGet(key, out var replay))
			{
				_logger.LogDebug("Ion remote: replaying the response of mutation {Method} with id {Id}.", request.Method, request.IdText);
				Respond(request, replay);
				return;
			}

			if (IsSceneLoading())
			{
				// Not cached: the client retries with the same id and the mutation applies once the scene is loaded.
				Respond(request, RemoteMessages.Error(request.Id, RemoteErrorCodes.SceneLoading, "A scene is loading; mutations are rejected until it has loaded. Retry on a later frame with the same id."));
				return;
			}

			if (method.Name == StepMethod)
			{
				Step(request);
				return;
			}

			var response = Invoke(method, request);
			if (key is not null) _idempotency.Add(key, response);
			Respond(request, response);
			return;
		}

		if (request.Watch)
		{
			var first = Evaluate(method, request, watch: true, out var text, out var failed);
			Respond(request, first);
			if (!failed) _watches.Add(new Watch(request, method, text));
			return;
		}

		Respond(request, Invoke(method, request));
	}

	private byte[] Invoke(RemoteMethod method, PendingRequest request) => Evaluate(method, request, watch: false, out _, out _);

	private byte[] Evaluate(RemoteMethod method, PendingRequest request, bool watch, out string? resultText, out bool failed)
	{
		resultText = null;
		failed = false;
		try
		{
			var result = method.Handler(new RemoteRequest(method.Name, request.Params, _services, request.Connection.Access) { IsWatch = watch, Id = request.IdText });
			resultText = result?.ToJsonString();
			return RemoteMessages.Result(request.Id, result);
		}
		catch (RemoteException ex)
		{
			failed = true;
			return RemoteMessages.Error(request.Id, ex.Code, ex.Message, ex.Data);
		}
		catch (Exception ex)
		{
			failed = true;
			_logger.LogWarning(ex, "Ion remote: {Method} failed.", method.Name);
			return RemoteMessages.Error(request.Id, RemoteErrorCodes.InternalError, $"{ex.GetType().Name}: {ex.Message}");
		}
	}

	private void EvaluateWatches()
	{
		for (var i = _watches.Count - 1; i >= 0; i--)
		{
			var watch = _watches[i];
			if (watch.Request.Connection.IsClosed)
			{
				_watches.RemoveAt(i);
				continue;
			}

			var message = Evaluate(watch.Method, watch.Request, watch: true, out var text, out var failed);
			if (failed)
			{
				_watches.RemoveAt(i);
				Respond(watch.Request, message);
				continue;
			}

			if (text == watch.LastText) continue;
			watch.LastText = text;
			Respond(watch.Request, message);
		}
	}

	private void Unwatch(PendingRequest request)
	{
		var target = request.Params?["id"]?.ToJsonString();
		var removed = 0;
		for (var i = _watches.Count - 1; i >= 0; i--)
		{
			var watch = _watches[i];
			if (watch.Request.Connection != request.Connection) continue;
			if (target is not null && watch.Request.IdText != target) continue;
			_watches.RemoveAt(i);
			removed++;
		}

		if (request.Id is not null) Respond(request, RemoteMessages.Result(request.Id, new JsonObject { ["removed"] = removed }));
	}

	private void Step(PendingRequest request)
	{
		var frames = request.Params?["frames"] is JsonValue value && value.TryGetValue<int>(out var n) ? n : 1;
		if (frames < 1 || frames > 1_000_000)
		{
			Respond(request, RemoteMessages.Error(request.Id, RemoteErrorCodes.InvalidParams, "'frames' must be between 1 and 1000000."));
			return;
		}

		if (_stepRequest is { } previous)
		{
			Respond(previous, RemoteMessages.Error(previous.Id, RemoteErrorCodes.InvalidRequest, "Superseded by a newer game.step."));
		}

		_stepRequest = request.Id is null ? null : request;
		_stepRemaining = frames;
		_paused = false;
	}

	private void Respond(PendingRequest request, byte[] message)
	{
		if (request.Id is null && !request.Watch) return;
		request.Connection.Send(message);
	}

	private bool IsSceneLoading()
	{
		if (!_scenesResolved)
		{
			_scenes = _services.GetService<SceneSystem>();
			_scenesResolved = true;
		}

		return _scenes?.IsLoading == true;
	}

	private void PollEvents()
	{
		var frame = _loop?.Frame ?? 0;
		foreach (var source in _eventSources) _eventLog.Poll(source, frame);
	}

	private static string IdempotencyKey(PendingRequest request) => $"{request.IdText}\n{request.Method}\n{request.Params?.ToJsonString()}";

	private void WriteTokenFile(IPAddress address)
	{
		var host = FormatHost(address);
		var info = new RemoteEndpointInfo
		{
			Pid = Environment.ProcessId,
			Title = _services.GetService<IOptions<GameConfig>>()?.Value.Title,
			Url = $"http://{host}:{Port}/",
			WebSocketUrl = $"ws://{host}:{Port}/ws",
			ReadToken = ReadToken,
			MutateToken = MutateToken,
			AllowMutations = _options.AllowMutations,
			StartedAt = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
		};

		var path = _options.TokenFilePath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (File.Exists(path)) File.Delete(path);

		var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
		if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
		using (var stream = new FileStream(path, fileOptions))
		{
			JsonSerializer.Serialize(stream, info, RemoteJsonContext.Default.RemoteEndpointInfo);
		}

		TokenFilePath = path;
	}

	internal static IPAddress ResolveBind(string bind) => HttpSecurity.TryResolveBind(bind, out var address)
		? address
		: throw new RemoteSecurityException($"Ion:Remote:Bind must be an IP address or 'localhost', not '{bind}'.");

	internal static string FormatHost(IPAddress address) => HttpSecurity.FormatHost(address);

	/// <summary>Stops the transports, releases a paused game thread and deletes the token file.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_signal.Release();

		List<IDisposable> transports;
		lock (_transports) transports = [.. _transports];
		foreach (var transport in transports)
		{
			try
			{
				transport.Dispose();
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Ion remote: error while stopping a transport.");
			}
		}

		if (TokenFilePath is { } path)
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
		}
	}

	internal const string WatchSuffix = "+watch";
	internal const string UnwatchMethod = "rpc.unwatch";
	internal const string StepMethod = "game.step";

	private sealed class Watch(PendingRequest request, RemoteMethod method, string? lastText)
	{
		public PendingRequest Request { get; } = request;
		public RemoteMethod Method { get; } = method;
		public string? LastText { get; set; } = lastText;
	}

	/// <summary>A bounded least-recently-used map from request key to response.</summary>
	private sealed class IdempotencyCache(int capacity)
	{
		private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Response)>> _map = new(StringComparer.Ordinal);
		private readonly LinkedList<(string Key, byte[] Response)> _order = new();

		public bool TryGet(string key, out byte[] response)
		{
			if (_map.TryGetValue(key, out var node))
			{
				_order.Remove(node);
				_order.AddFirst(node);
				response = node.Value.Response;
				return true;
			}

			response = [];
			return false;
		}

		public void Add(string key, byte[] response)
		{
			if (_map.TryGetValue(key, out var existing)) _order.Remove(existing);
			_map[key] = _order.AddFirst((key, response));
			while (_map.Count > capacity)
			{
				var last = _order.Last!;
				_order.RemoveLast();
				_map.Remove(last.Value.Key);
			}
		}
	}
}

/// <summary>The remote server refused a configuration that would weaken its access control.</summary>
public sealed class RemoteSecurityException(string message) : Exception(message);

/// <summary>The recent payloads of the event types registered with <c>AddRemoteEvent</c>, with a sequence number.</summary>
internal sealed class RemoteEventLog
{
	public const int Capacity = 1024;
	private readonly List<JsonNode?> _scratch = [];
	private readonly (long Seq, uint Frame, string Name, JsonNode? Payload)[] _items = new (long, uint, string, JsonNode?)[Capacity];
	private long _next;

	public long Next => _next;

	public void Poll(RemoteEventSource source, uint frame)
	{
		_scratch.Clear();
		source.Poll(_scratch);
		foreach (var payload in _scratch)
		{
			_items[_next % Capacity] = (_next, frame, source.Name, payload);
			_next++;
		}
	}

	public IEnumerable<(long Seq, uint Frame, string Name, JsonNode? Payload)> Since(long since)
	{
		for (var seq = Math.Max(since, Math.Max(0, _next - Capacity)); seq < _next; seq++) yield return _items[seq % Capacity];
	}
}
