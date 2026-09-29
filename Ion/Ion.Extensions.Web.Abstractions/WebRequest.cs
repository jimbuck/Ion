using System.Net;

using Ion.Extensions.Http;

namespace Ion.Extensions.Web;

/// <summary>
/// The request an <see cref="HttpAttribute"/> handler receives, on the game thread: method, path, route values, query,
/// headers and body, as spans over the connection's buffers (valid during the call only). Members returning strings
/// allocate; the span members do not.
/// </summary>
public readonly struct WebRequest
{
	private readonly HttpRequest _http;
	private readonly Range[] _routeValues;
	private readonly int _routeValueCount;

	/// <summary>Wraps a parsed request and the route values the router matched in its path.</summary>
	public WebRequest(HttpRequest http, Range[] routeValues, int routeValueCount, bool isAuthenticated, IPAddress? remoteAddress)
	{
		ArgumentNullException.ThrowIfNull(http);
		ArgumentNullException.ThrowIfNull(routeValues);
		_http = http;
		_routeValues = routeValues;
		_routeValueCount = routeValueCount;
		IsAuthenticated = isAuthenticated;
		RemoteAddress = remoteAddress ?? IPAddress.None;
	}

	/// <summary>The underlying parsed request.</summary>
	public HttpRequest Http => _http;

	/// <summary>The method.</summary>
	public HttpVerb Method => _http.Method;

	/// <summary>The method name (<c>GET</c>, ...).</summary>
	public string MethodName => _http.MethodName;

	/// <summary>The path, still percent-encoded.</summary>
	public ReadOnlySpan<byte> RawPath => _http.RawPath;

	/// <summary>The decoded path (allocates).</summary>
	public string Path => _http.Path;

	/// <summary>The query string without <c>?</c>, still percent-encoded.</summary>
	public ReadOnlySpan<byte> RawQuery => _http.RawQuery;

	/// <summary>The body.</summary>
	public ReadOnlySpan<byte> Body => _http.Body;

	/// <summary>Whether the request presented the web server's bearer token (always true when none is configured).</summary>
	public bool IsAuthenticated { get; }

	/// <summary>The client's address.</summary>
	public IPAddress RemoteAddress { get; }

	/// <summary>The number of route values (<c>{name}</c> segments of the route template, in order).</summary>
	public int RouteValueCount => _routeValueCount;

	/// <summary>Route value <paramref name="index"/> (in template order), still percent-encoded.</summary>
	public ReadOnlySpan<byte> RouteValue(int index)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_routeValueCount, nameof(index));
		return _http.RawPath[_routeValues[index]];
	}

	/// <summary>Finds a header (ASCII name, case-insensitive).</summary>
	public bool TryGetHeader(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value) => _http.TryGetHeader(name, out value);

	/// <summary>A header's value, or null (allocates).</summary>
	public string? Header(string name) => _http.Header(name);

	/// <summary>Finds a query parameter and returns its still-encoded value.</summary>
	public bool TryGetQuery(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> rawValue) => _http.TryGetQuery(name, out rawValue);

	/// <summary>A decoded query parameter, or null (allocates).</summary>
	public string? Query(string name) => _http.Query(name);
}
