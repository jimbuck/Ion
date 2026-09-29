using System.Buffers.Text;
using System.Text;

namespace Ion.Extensions.Http;

/// <summary>The request methods the server knows by name; anything else is <see cref="Other"/>.</summary>
public enum HttpVerb : byte
{
	/// <summary>A method token the server does not know.</summary>
	Other,
	/// <summary><c>GET</c>.</summary>
	Get,
	/// <summary><c>HEAD</c>.</summary>
	Head,
	/// <summary><c>POST</c>.</summary>
	Post,
	/// <summary><c>PUT</c>.</summary>
	Put,
	/// <summary><c>PATCH</c>.</summary>
	Patch,
	/// <summary><c>DELETE</c>.</summary>
	Delete,
	/// <summary><c>OPTIONS</c>.</summary>
	Options,
}

/// <summary>
/// One parsed HTTP/1.1 request head (and, once read, its body). The instance belongs to a connection and is reused for
/// every request on it: the spans point into the connection's buffers and are valid until the next request is read.
/// Nothing here allocates except the members documented as returning strings.
/// </summary>
public sealed class HttpRequest
{
	/// <summary>The most headers a request may carry (more is 431).</summary>
	public const int MaxHeaders = 64;

	private readonly (int NameStart, int NameLength, int ValueStart, int ValueLength)[] _headers = new (int, int, int, int)[MaxHeaders];
	private byte[] _head = [];
	private byte[] _body = [];
	private int _bodyLength;
	private int _methodLength;
	private int _targetStart, _targetLength;
	private int _pathLength;
	private int _headerCount;

	/// <summary>The method.</summary>
	public HttpVerb Method { get; private set; }

	/// <summary>The method token (<c>GET</c>, ...), ASCII.</summary>
	public ReadOnlySpan<byte> MethodBytes => _head.AsSpan(0, _methodLength);

	/// <summary>The method as a string (a constant for the known methods).</summary>
	public string MethodName => Method switch
	{
		HttpVerb.Get => "GET",
		HttpVerb.Head => "HEAD",
		HttpVerb.Post => "POST",
		HttpVerb.Put => "PUT",
		HttpVerb.Patch => "PATCH",
		HttpVerb.Delete => "DELETE",
		HttpVerb.Options => "OPTIONS",
		_ => Encoding.ASCII.GetString(MethodBytes),
	};

	/// <summary>The request target as sent (path and query, still percent-encoded).</summary>
	public ReadOnlySpan<byte> Target => _head.AsSpan(_targetStart, _targetLength);

	/// <summary>The path part of the target (before <c>?</c>), still percent-encoded.</summary>
	public ReadOnlySpan<byte> RawPath => _head.AsSpan(_targetStart, _pathLength);

	/// <summary>The query part of the target (after <c>?</c>, without it), still percent-encoded; empty when there is none.</summary>
	public ReadOnlySpan<byte> RawQuery => _pathLength < _targetLength ? _head.AsSpan(_targetStart + _pathLength + 1, _targetLength - _pathLength - 1) : [];

	/// <summary>The decoded path as a string (allocates).</summary>
	public string Path => HttpText.DecodeToString(RawPath, plusIsSpace: false);

	/// <summary>Whether the version is HTTP/1.1 (otherwise HTTP/1.0).</summary>
	public bool IsHttp11 { get; private set; }

	/// <summary>The Content-Length, or -1 when the request has none.</summary>
	public long ContentLength { get; private set; } = -1;

	/// <summary>
	/// Whether the body is sent in chunks (<c>Transfer-Encoding: chunked</c>, HTTP/1.1, no Content-Length):
	/// <see cref="ContentLength"/> is -1 and <see cref="HttpConnection.ReadBody(int)"/> decodes it.
	/// </summary>
	public bool IsChunked { get; private set; }

	/// <summary>Whether the connection stays open after the response (HTTP/1.1 unless <c>Connection: close</c>; HTTP/1.0 only with <c>keep-alive</c>).</summary>
	public bool KeepAlive { get; private set; }

	/// <summary>Whether this is a WebSocket opening handshake (GET with <c>Upgrade: websocket</c> and <c>Connection: upgrade</c>).</summary>
	public bool IsWebSocketUpgrade { get; private set; }

	/// <summary>The number of headers.</summary>
	public int HeaderCount => _headerCount;

	/// <summary>The body read by <see cref="HttpConnection.ReadBody()"/> (empty before, or without one).</summary>
	public ReadOnlySpan<byte> Body => _body.AsSpan(0, _bodyLength);

	/// <summary>The header at <paramref name="index"/>.</summary>
	public void GetHeader(int index, out ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_headerCount, nameof(index));
		var h = _headers[index];
		name = _head.AsSpan(h.NameStart, h.NameLength);
		value = _head.AsSpan(h.ValueStart, h.ValueLength);
	}

	/// <summary>Finds the first header named <paramref name="name"/> (ASCII, case-insensitive).</summary>
	public bool TryGetHeader(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
	{
		for (var i = 0; i < _headerCount; i++)
		{
			var h = _headers[i];
			if (Ascii.EqualsIgnoreCase(_head.AsSpan(h.NameStart, h.NameLength), name))
			{
				value = _head.AsSpan(h.ValueStart, h.ValueLength);
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>The value of the first header named <paramref name="name"/> as a string, or null (allocates).</summary>
	public string? Header(string name)
	{
		Span<byte> ascii = stackalloc byte[Math.Min(name.Length, 256)];
		if (name.Length > ascii.Length || Encoding.ASCII.GetBytes(name, ascii) != name.Length) return null;
		return TryGetHeader(ascii, out var value) ? Encoding.Latin1.GetString(value) : null;
	}

	/// <summary>Whether the comma-separated header <paramref name="name"/> contains <paramref name="token"/> (case-insensitive).</summary>
	public bool HeaderContainsToken(ReadOnlySpan<byte> name, ReadOnlySpan<byte> token)
	{
		for (var i = 0; i < _headerCount; i++)
		{
			var h = _headers[i];
			if (!Ascii.EqualsIgnoreCase(_head.AsSpan(h.NameStart, h.NameLength), name)) continue;
			if (HttpText.ContainsToken(_head.AsSpan(h.ValueStart, h.ValueLength), token)) return true;
		}

		return false;
	}

	/// <summary>
	/// Finds query parameter <paramref name="name"/> (compared after percent-decoding the key) and returns its raw,
	/// still-encoded value. A key without <c>=</c> has an empty value.
	/// </summary>
	public bool TryGetQuery(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> rawValue) => HttpText.TryGetQuery(RawQuery, name, out rawValue);

	/// <summary>The decoded value of query parameter <paramref name="name"/>, or null (allocates).</summary>
	public string? Query(string name)
	{
		var key = Encoding.UTF8.GetBytes(name);
		return TryGetQuery(key, out var raw) ? HttpText.DecodeToString(raw, plusIsSpace: true) : null;
	}

	internal byte[] BodyBuffer
	{
		get => _body;
		set => _body = value;
	}

	internal void SetBodyLength(int length) => _bodyLength = length;

	/// <summary>
	/// Parses the request head in <paramref name="head"/> (from the request line to the blank line, CRLF-terminated) into
	/// this instance. The array is kept: the spans point into it.
	/// </summary>
	public HttpParseStatus Parse(byte[] head, int length)
	{
		ArgumentNullException.ThrowIfNull(head);
		_head = head;
		_headerCount = 0;
		_bodyLength = 0;
		ContentLength = -1;
		KeepAlive = false;
		IsWebSocketUpgrade = false;
		IsChunked = false;
		Method = HttpVerb.Other;
		_methodLength = _targetStart = _targetLength = _pathLength = 0;

		var span = head.AsSpan(0, length);
		// Every line ends in CRLF; a bare CR or LF anywhere is malformed.
		var lineEnd = span.IndexOf("\r\n"u8);
		if (lineEnd <= 0) return HttpParseStatus.BadRequest;

		var status = ParseRequestLine(span[..lineEnd]);
		if (status != HttpParseStatus.Ok) return status;

		var position = lineEnd + 2;
		var hasHost = false;
		var hasTransferEncoding = false;
		var transferEncodings = 0;
		while (true)
		{
			var rest = span[position..];
			var end = rest.IndexOf("\r\n"u8);
			if (end < 0) return HttpParseStatus.BadRequest;
			if (end == 0) break; // the blank line
			var line = rest[..end];
			if (line[0] is (byte)' ' or (byte)'\t') return HttpParseStatus.BadRequest; // obsolete line folding
			var colon = line.IndexOf((byte)':');
			if (colon <= 0) return HttpParseStatus.BadRequest;
			var name = line[..colon];
			if (!HttpText.IsToken(name)) return HttpParseStatus.BadRequest; // includes whitespace before the colon
			var valueStart = colon + 1;
			var valueEnd = line.Length;
			while (valueStart < valueEnd && line[valueStart] is (byte)' ' or (byte)'\t') valueStart++;
			while (valueEnd > valueStart && line[valueEnd - 1] is (byte)' ' or (byte)'\t') valueEnd--;
			var value = line[valueStart..valueEnd];
			if (!HttpText.IsFieldValue(value)) return HttpParseStatus.BadRequest;
			if (_headerCount == MaxHeaders) return HttpParseStatus.HeaderTooLarge;
			_headers[_headerCount++] = (position, colon, position + valueStart, valueEnd - valueStart);

			if (Ascii.EqualsIgnoreCase(name, "Content-Length"u8))
			{
				if (!Utf8Parser.TryParse(value, out long contentLength, out var consumed) || consumed != value.Length || value.Length == 0 || !HttpText.AllDigits(value))
				{
					return HttpParseStatus.BadRequest;
				}

				if (ContentLength >= 0 && ContentLength != contentLength) return HttpParseStatus.BadRequest;
				ContentLength = contentLength;
			}
			else if (Ascii.EqualsIgnoreCase(name, "Host"u8))
			{
				if (hasHost) return HttpParseStatus.BadRequest;
				hasHost = true;
			}
			else if (Ascii.EqualsIgnoreCase(name, "Transfer-Encoding"u8))
			{
				hasTransferEncoding = true;
				transferEncodings++;
			}

			position += end + 2;
		}

		if (position + 2 != length) return HttpParseStatus.BadRequest;

		// Request smuggling: never accept both framings; the only transfer coding accepted is a lone "chunked" on HTTP/1.1.
		if (hasTransferEncoding)
		{
			if (ContentLength >= 0 || !IsHttp11) return HttpParseStatus.BadRequest;
			if (transferEncodings != 1 || !TryGetHeader("Transfer-Encoding"u8, out var coding) || !Ascii.EqualsIgnoreCase(coding, "chunked"u8)) return HttpParseStatus.LengthRequired;
			IsChunked = true;
		}

		if (IsHttp11 && !hasHost) return HttpParseStatus.BadRequest;

		var close = HeaderContainsToken("Connection"u8, "close"u8);
		KeepAlive = IsHttp11 ? !close : !close && HeaderContainsToken("Connection"u8, "keep-alive"u8);
		IsWebSocketUpgrade = Method == HttpVerb.Get
			&& TryGetHeader("Upgrade"u8, out var upgrade) && Ascii.EqualsIgnoreCase(upgrade, "websocket"u8)
			&& HeaderContainsToken("Connection"u8, "upgrade"u8);
		return HttpParseStatus.Ok;
	}

	private HttpParseStatus ParseRequestLine(ReadOnlySpan<byte> line)
	{
		if (line.IndexOfAny((byte)'\r', (byte)'\n') >= 0) return HttpParseStatus.BadRequest;
		var firstSpace = line.IndexOf((byte)' ');
		if (firstSpace <= 0) return HttpParseStatus.BadRequest;
		var method = line[..firstSpace];
		if (!HttpText.IsToken(method)) return HttpParseStatus.BadRequest;

		var rest = line[(firstSpace + 1)..];
		var secondSpace = rest.IndexOf((byte)' ');
		if (secondSpace <= 0) return HttpParseStatus.BadRequest;
		var target = rest[..secondSpace];
		var version = rest[(secondSpace + 1)..];

		// Origin form only (a path starting with '/'); no absolute form, no authority form. OPTIONS * is accepted.
		if (!(target[0] == (byte)'/' || (target.Length == 1 && target[0] == (byte)'*'))) return HttpParseStatus.BadRequest;
		foreach (var b in target)
		{
			if (b <= 0x20 || b >= 0x7F) return HttpParseStatus.BadRequest;
		}

		if (version.SequenceEqual("HTTP/1.1"u8)) IsHttp11 = true;
		else if (version.SequenceEqual("HTTP/1.0"u8)) IsHttp11 = false;
		else if (version.Length == 8 && version.StartsWith("HTTP/"u8) && char.IsAsciiDigit((char)version[5]) && version[6] == (byte)'.' && char.IsAsciiDigit((char)version[7])) return HttpParseStatus.VersionNotSupported;
		else return HttpParseStatus.BadRequest;

		_methodLength = method.Length;
		Method = method switch
		{
			_ when method.SequenceEqual("GET"u8) => HttpVerb.Get,
			_ when method.SequenceEqual("HEAD"u8) => HttpVerb.Head,
			_ when method.SequenceEqual("POST"u8) => HttpVerb.Post,
			_ when method.SequenceEqual("PUT"u8) => HttpVerb.Put,
			_ when method.SequenceEqual("PATCH"u8) => HttpVerb.Patch,
			_ when method.SequenceEqual("DELETE"u8) => HttpVerb.Delete,
			_ when method.SequenceEqual("OPTIONS"u8) => HttpVerb.Options,
			_ => HttpVerb.Other,
		};

		_targetStart = firstSpace + 1;
		_targetLength = target.Length;
		var question = target.IndexOf((byte)'?');
		_pathLength = question < 0 ? target.Length : question;
		return HttpParseStatus.Ok;
	}
}

/// <summary>The outcome of reading a request body with <see cref="HttpConnection.ReadBody(int)"/>.</summary>
public enum HttpBodyStatus
{
	/// <summary>Read.</summary>
	Ok,
	/// <summary>413: larger than the limit (the connection must be closed).</summary>
	TooLarge,
	/// <summary>400: a malformed chunk (the connection must be closed).</summary>
	Malformed,
}

/// <summary>The outcome of parsing a request head; anything but <see cref="Ok"/> is answered with the matching status and the connection is closed.</summary>
public enum HttpParseStatus
{
	/// <summary>Parsed.</summary>
	Ok,
	/// <summary>400: malformed request line or header, a missing or repeated Host, conflicting lengths.</summary>
	BadRequest,
	/// <summary>411: a transfer coding other than a lone <c>chunked</c>.</summary>
	LengthRequired,
	/// <summary>431: the head is larger than the limit, or has too many headers.</summary>
	HeaderTooLarge,
	/// <summary>505: an HTTP version other than 1.0 and 1.1.</summary>
	VersionNotSupported,
}
