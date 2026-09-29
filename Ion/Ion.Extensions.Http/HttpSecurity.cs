using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Ion.Extensions.Http;

/// <summary>
/// The security policy shared by the remote protocol and the web module (roadmap 4.10 item 6 and 4.12): loopback binds
/// by default with an explicit opt-in for anything else, the <c>Host</c> check that defeats DNS rebinding on loopback,
/// the browser <c>Origin</c> allow-list, and bearer tokens compared in constant time.
/// </summary>
public static class HttpSecurity
{
	/// <summary>Parses a bind setting: an IP address, <c>localhost</c> (loopback) or <c>*</c>/<c>+</c> (every interface).</summary>
	public static bool TryResolveBind(string? bind, out IPAddress address)
	{
		if (string.IsNullOrWhiteSpace(bind) || bind.Equals("localhost", StringComparison.OrdinalIgnoreCase))
		{
			address = IPAddress.Loopback;
			return true;
		}

		if (bind is "*" or "+")
		{
			address = IPAddress.Any;
			return true;
		}

		return IPAddress.TryParse(bind, out address!);
	}

	/// <summary>Formats an address for a URL (<c>[::1]</c> for IPv6).</summary>
	public static string FormatHost(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
	}

	/// <summary>Whether a <c>Host</c> header names a loopback host (<c>localhost</c>, <c>127.0.0.1</c> or <c>[::1]</c>, any port).</summary>
	public static bool IsLoopbackHost(ReadOnlySpan<byte> host)
	{
		var name = host;
		if (name.Length > 0 && name[0] == (byte)'[')
		{
			var end = name.IndexOf((byte)']');
			if (end < 0) return false;
			name = name[..(end + 1)];
		}
		else if (name.IndexOf((byte)':') is var colon and >= 0)
		{
			name = name[..colon];
		}

		return Ascii.EqualsIgnoreCase(name, "localhost"u8) || name.SequenceEqual("127.0.0.1"u8) || name.SequenceEqual("[::1]"u8);
	}

	/// <summary>
	/// The <c>Host</c> check of a request: always true when the server is not bound to loopback; otherwise the request must
	/// carry a <c>Host</c> naming a loopback host, which keeps a page on another site that re-resolved its name to
	/// 127.0.0.1 (DNS rebinding) from reaching the server.
	/// </summary>
	public static bool CheckHost(HttpRequest request, bool loopbackBind)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!loopbackBind) return true;
		return request.TryGetHeader("Host"u8, out var host) && IsLoopbackHost(host);
	}

	/// <summary>
	/// The <c>Origin</c> check of a request: a request without an <c>Origin</c> header (not from a browser page) passes; a
	/// browser request passes when its origin is in <paramref name="allowedOrigins"/> (exact match), or, with
	/// <paramref name="allowSameOrigin"/>, when it is the server's own origin (<c>http://</c> plus the <c>Host</c> header:
	/// pages the server itself serves).
	/// </summary>
	public static bool CheckOrigin(HttpRequest request, IReadOnlyList<string> allowedOrigins, bool allowSameOrigin)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(allowedOrigins);
		if (!request.TryGetHeader("Origin"u8, out var origin)) return true;
		for (var i = 0; i < allowedOrigins.Count; i++)
		{
			if (Ascii.Equals(origin, allowedOrigins[i])) return true;
		}

		if (!allowSameOrigin || !request.TryGetHeader("Host"u8, out var host)) return false;
		return origin.StartsWith("http://"u8) && origin[7..].SequenceEqual(host);
	}

	/// <summary>The token of an <c>Authorization: Bearer &lt;token&gt;</c> header, if the request has one.</summary>
	public static bool TryGetBearer(HttpRequest request, out ReadOnlySpan<byte> token)
	{
		ArgumentNullException.ThrowIfNull(request);
		token = default;
		if (!request.TryGetHeader("Authorization"u8, out var value) || value.Length < 7 || !Ascii.EqualsIgnoreCase(value[..7], "Bearer "u8)) return false;
		token = value[7..].Trim(" \t"u8);
		return token.Length > 0;
	}

	/// <summary>Compares a presented token with an expected one in constant time (for tokens of equal length).</summary>
	public static bool TokenEquals(ReadOnlySpan<byte> presented, ReadOnlySpan<byte> expected) =>
		presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);

	/// <summary>A new random token: 32 bytes, base64url without padding (43 characters).</summary>
	public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
