namespace Ion.Extensions.Http;

/// <summary>
/// An HTTP endpoint one server can mount into another's listener: the remote protocol registers its JSON-RPC endpoint
/// this way, and the web module serves it at <see cref="Path"/> (<c>/rpc</c>) when both modules run, so a companion page
/// can reach the protocol on the web module's port. The hosting server applies its own Host, Origin and rate-limit checks
/// first; the endpoint applies its own authentication.
/// </summary>
public interface IHttpEndpoint
{
	/// <summary>The path the endpoint is mounted at (exact match, without the query), for example <c>/rpc</c>.</summary>
	string Path { get; }

	/// <summary>
	/// Serves the request just read on <paramref name="connection"/> (<see cref="HttpConnection.Request"/>, body not read
	/// yet), including a WebSocket upgrade, which it may keep on this thread until the session ends. Returns whether the
	/// connection may carry another request.
	/// </summary>
	bool Serve(HttpConnection connection);
}
