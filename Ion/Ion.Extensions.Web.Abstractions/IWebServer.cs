namespace Ion.Extensions.Web;

/// <summary>
/// The running web server of the web module (<c>Ion.Extensions.Web</c>): where it listens and the push channels of its
/// WebSocket endpoints. Resolve it from the services (it exists whenever <c>AddWeb</c> registered the module, running or not).
/// </summary>
public interface IWebServer
{
	/// <summary>Whether the server is listening.</summary>
	bool IsRunning { get; }

	/// <summary>The bound port, or 0 when not running.</summary>
	int Port { get; }

	/// <summary>The base URL (<c>http://127.0.0.1:port/</c>), or null when not running.</summary>
	string? BaseUrl { get; }

	/// <summary>The HTTP routes served, most specific first.</summary>
	IReadOnlyList<WebRoute> Routes { get; }

	/// <summary>The WebSocket endpoints served.</summary>
	IReadOnlyList<WebSocketRoute> WebSockets { get; }

	/// <summary>The number of requests answered (by a route, a static file, <c>/rpc</c> or an error).</summary>
	long RequestCount { get; }

	/// <summary>The push channel of the WebSocket endpoint at <paramref name="path"/>.</summary>
	/// <exception cref="KeyNotFoundException">No <see cref="WebSocketAttribute"/> endpoint has that path.</exception>
	WebSocketChannel Channel(string path);
}
