namespace Ion.Extensions.Remote;

/// <summary>Which transports the remote server runs.</summary>
public enum RemoteTransport
{
	/// <summary>HTTP/1.1 (POST JSON-RPC) and WebSocket (<c>GET /ws</c>) on <see cref="RemoteOptions.Bind"/>:<see cref="RemoteOptions.Port"/>, with bearer tokens.</summary>
	Http,

	/// <summary>Newline-delimited JSON-RPC on the process's standard input and output; no token (the parent process is trusted).</summary>
	Stdio,

	/// <summary>Both.</summary>
	Both,
}

/// <summary>
/// Remote inspection protocol options, bound from <c>Ion:Remote</c>. The command line switches <c>--remote</c>,
/// <c>--remote-allow-mutations</c> and <c>--remote-stdio</c> set <see cref="Enabled"/>, <see cref="AllowMutations"/> and
/// <see cref="Transport"/>.
/// </summary>
public sealed class RemoteOptions
{
	/// <summary>The default port (the Bevy Remote Protocol's).</summary>
	public const int DefaultPort = 15702;

	/// <summary>Whether the server runs. Default false: nothing listens unless this is set (or <c>--remote</c> is passed).</summary>
	public bool Enabled { get; set; }

	/// <summary>The transports. Default <see cref="RemoteTransport.Http"/>.</summary>
	public RemoteTransport Transport { get; set; } = RemoteTransport.Http;

	/// <summary>
	/// The address the HTTP transport binds to. Default <c>127.0.0.1</c>. Any address that is not a loopback address is
	/// refused (the server does not start and the game fails at Init) unless <see cref="AllowNonLoopback"/> is also set.
	/// </summary>
	public string Bind { get; set; } = "127.0.0.1";

	/// <summary>The explicit opt-in for a non-loopback <see cref="Bind"/>; logged as a warning when used.</summary>
	public bool AllowNonLoopback { get; set; }

	/// <summary>The HTTP port. Default <see cref="DefaultPort"/>; 0 picks a free port (written to the token file).</summary>
	public int Port { get; set; } = DefaultPort;

	/// <summary>
	/// Whether the mutate scope exists (<c>--remote-allow-mutations</c>). Default false: every session is read-only, and
	/// HTTP clients get only the read token.
	/// </summary>
	public bool AllowMutations { get; set; }

	/// <summary>
	/// The run directory: the token file is written here. Default <c>.ion/run</c> under the current directory
	/// (<c>ion run</c> passes its own). Created if needed.
	/// </summary>
	public string RunDirectory { get; set; } = Path.Combine(".ion", "run");

	/// <summary>The token file name inside <see cref="RunDirectory"/>. Default <c>remote.json</c>.</summary>
	public string TokenFile { get; set; } = "remote.json";

	/// <summary>
	/// Whether to print the endpoint and tokens once to standard error at startup. Default true. The token file has the
	/// same information.
	/// </summary>
	public bool PrintToken { get; set; } = true;

	/// <summary>
	/// Browser origins allowed to call the HTTP transport (exact <c>Origin</c> header values). Default none: any request
	/// that carries an <c>Origin</c> header is refused, which keeps web pages from reaching the server through the browser.
	/// </summary>
	public List<string> AllowedOrigins { get; set; } = [];

	/// <summary>The most concurrent HTTP and WebSocket connections. Default 8.</summary>
	public int MaxConnections { get; set; } = 8;

	/// <summary>The largest request body or WebSocket message, in bytes. Default 4 MiB.</summary>
	public int MaxRequestBytes { get; set; } = 4 * 1024 * 1024;

	/// <summary>The most requests applied per frame while the game runs (the rest wait for the next frame). Default 64.</summary>
	public int MaxRequestsPerFrame { get; set; } = 64;

	/// <summary>How long an HTTP request waits for the game thread, in milliseconds. Default 30000.</summary>
	public int RequestTimeoutMs { get; set; } = 30_000;

	/// <summary>How many completed mutation responses are kept for idempotent replays. Default 1024.</summary>
	public int IdempotencyCacheSize { get; set; } = 1024;

	/// <summary>Whether the game starts paused (serving requests at the end of the first frame until <c>game.resume</c>).</summary>
	public bool StartPaused { get; set; }

	/// <summary>Pauses the game at the end of this frame number (0-based, so 600 means after 600 frames ran). Negative: never. Default -1.</summary>
	public long PauseAtFrame { get; set; } = -1;

	/// <summary>The full path of the token file.</summary>
	public string TokenFilePath => Path.GetFullPath(Path.Combine(RunDirectory, TokenFile));
}
