namespace Ion.Extensions.Web;

/// <summary>
/// Web server options, bound from <c>Ion:Web</c>. The policy is the remote protocol's (off by default, loopback by
/// default, an explicit logged opt-in for anything else), plus an origin allow-list for browser clients and a bearer
/// token for mutating endpoints.
/// </summary>
public sealed class WebOptions
{
	/// <summary>The default port.</summary>
	public const int DefaultPort = 15780;

	/// <summary>Whether the server runs. Default false: nothing listens unless this is set.</summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// The address to bind: an IP address, <c>localhost</c>, or <c>*</c> for every interface. Default <c>127.0.0.1</c>.
	/// Anything that is not a loopback address is refused (the game fails at Init) unless <see cref="AllowNonLoopback"/>
	/// is also set.
	/// </summary>
	public string Bind { get; set; } = "127.0.0.1";

	/// <summary>The explicit opt-in for a LAN (non-loopback) <see cref="Bind"/>, logged as a warning when used.</summary>
	public bool AllowNonLoopback { get; set; }

	/// <summary>The port. Default <see cref="DefaultPort"/>; 0 picks a free port (see <see cref="IWebServer.Port"/>).</summary>
	public int Port { get; set; } = DefaultPort;

	/// <summary>
	/// The bearer token mutating endpoints need (<see cref="WebAccess.Mutate"/>: every method but GET and HEAD, and
	/// WebSocket endpoints, unless declared otherwise). Null: no token, unless the server binds to a non-loopback address,
	/// where one is generated per run (printed once) unless <see cref="AllowAnonymousMutations"/> is set.
	/// </summary>
	public string? Token { get; set; }

	/// <summary>Generate a per-run token even on loopback (printed once at startup).</summary>
	public bool GenerateToken { get; set; }

	/// <summary>On a non-loopback bind without a <see cref="Token"/>, let anyone on the network call mutating endpoints (logged as a warning).</summary>
	public bool AllowAnonymousMutations { get; set; }

	/// <summary>
	/// Browser origins allowed besides the server's own (exact <c>Origin</c> values such as <c>http://localhost:5173</c>).
	/// Pages the server serves itself are always allowed; any other request carrying an <c>Origin</c> header is refused
	/// with 403. Allowed cross origins get CORS headers.
	/// </summary>
	public List<string> AllowedOrigins { get; set; } = [];

	/// <summary>The folder static files are served from (relative to the application's base directory), or null for none.</summary>
	public string? StaticFiles { get; set; }

	/// <summary>The file served for a folder (<c>/</c>). Default <c>index.html</c>.</summary>
	public string DefaultFile { get; set; } = "index.html";

	/// <summary>The largest static file served, in bytes. Default 16 MiB.</summary>
	public int MaxStaticFileBytes { get; set; } = 16 * 1024 * 1024;

	/// <summary>Mount the <c>IHttpEndpoint</c>s other modules register (the remote protocol at <c>/rpc</c>). Default true.</summary>
	public bool HostEndpoints { get; set; } = true;

	/// <summary>The most concurrent connections (HTTP and WebSocket). Default 32.</summary>
	public int MaxConnections { get; set; } = 32;

	/// <summary>The largest request body, in bytes. Default 1 MiB.</summary>
	public int MaxRequestBytes { get; set; } = 1024 * 1024;

	/// <summary>The largest WebSocket message from a client, in bytes. Default 64 KiB.</summary>
	public int MaxWebSocketMessageBytes { get; set; } = 64 * 1024;

	/// <summary>How many messages of one WebSocket client may wait for the game thread; more closes it (1008). Default 256.</summary>
	public int MaxPendingMessagesPerClient { get; set; } = 256;

	/// <summary>The capacity of the queue into the game thread (requests and WebSocket messages). Default 1024.</summary>
	public int QueueCapacity { get; set; } = 1024;

	/// <summary>The most requests and messages handled per frame (the rest wait for the next one). Default 256.</summary>
	public int MaxRequestsPerFrame { get; set; } = 256;

	/// <summary>How long a request waits for the game thread, in milliseconds. Default 10000.</summary>
	public int RequestTimeoutMs { get; set; } = 10_000;

	/// <summary>Requests (and WebSocket messages) per second per client address; 0 or less disables the limit. Default 100.</summary>
	public double RateLimit { get; set; } = 100;

	/// <summary>The burst allowed above <see cref="RateLimit"/>. Default 200.</summary>
	public double RateLimitBurst { get; set; } = 200;

	/// <summary>Serve the routes of every assembly's generated table (<see cref="WebRoutes"/>). Default true; false serves only tables added with <c>AddWebRoutes</c>.</summary>
	public bool UseGeneratedRoutes { get; set; } = true;

	/// <summary>Print the URL (and a generated token) once to standard error at startup. Default true.</summary>
	public bool PrintUrl { get; set; } = true;
}

/// <summary>Thrown at startup when the web server's configuration breaks its security policy (a non-loopback bind without the opt-in).</summary>
public sealed class WebSecurityException(string message) : Exception(message);
