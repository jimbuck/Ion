namespace Ion.Extensions.Http;

/// <summary>HTTP status codes and reason phrases.</summary>
public static class HttpStatus
{
	/// <summary>The status a parse outcome is answered with.</summary>
	public static int FromParseStatus(HttpParseStatus status) => status switch
	{
		HttpParseStatus.Ok => 200,
		HttpParseStatus.LengthRequired => 411,
		HttpParseStatus.HeaderTooLarge => 431,
		HttpParseStatus.VersionNotSupported => 505,
		_ => 400,
	};

	/// <summary>The reason phrase of <paramref name="status"/> (ASCII).</summary>
	public static ReadOnlySpan<byte> ReasonPhrase(int status) => status switch
	{
		101 => "Switching Protocols"u8,
		200 => "OK"u8,
		201 => "Created"u8,
		202 => "Accepted"u8,
		204 => "No Content"u8,
		301 => "Moved Permanently"u8,
		302 => "Found"u8,
		304 => "Not Modified"u8,
		400 => "Bad Request"u8,
		401 => "Unauthorized"u8,
		403 => "Forbidden"u8,
		404 => "Not Found"u8,
		405 => "Method Not Allowed"u8,
		408 => "Request Timeout"u8,
		409 => "Conflict"u8,
		411 => "Length Required"u8,
		413 => "Content Too Large"u8,
		414 => "URI Too Long"u8,
		415 => "Unsupported Media Type"u8,
		422 => "Unprocessable Content"u8,
		426 => "Upgrade Required"u8,
		429 => "Too Many Requests"u8,
		431 => "Request Header Fields Too Large"u8,
		500 => "Internal Server Error"u8,
		501 => "Not Implemented"u8,
		503 => "Service Unavailable"u8,
		504 => "Gateway Timeout"u8,
		505 => "HTTP Version Not Supported"u8,
		_ => "Status"u8,
	};
}
