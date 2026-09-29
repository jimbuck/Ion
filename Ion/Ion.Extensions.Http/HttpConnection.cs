using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Ion.Extensions.Http;

/// <summary>
/// One client connection of an <see cref="HttpSocketListener"/>: blocking reads of request heads and bodies into reusable
/// buffers, and responses written in one send. Used by one thread at a time (the connection's thread). In steady state
/// (keep-alive, requests within the buffers' sizes) reading a request and writing a response allocate nothing.
/// </summary>
public sealed class HttpConnection : IDisposable
{
	private static readonly byte[] WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"u8.ToArray();

	private readonly Socket _socket;
	private readonly byte[] _in;
	private byte[] _out = new byte[4096];
	private int _length;
	private int _consumed;
	// The end of the current request's head: the head's spans point into the input buffer below it, so reading the
	// body never moves bytes below it.
	private int _floor;
	private IPAddress? _remoteAddress;
	private bool _disposed;

	/// <summary>Wraps an accepted socket. <paramref name="maxHeadBytes"/> bounds a request head (a larger one is 431).</summary>
	public HttpConnection(Socket socket, int maxHeadBytes = 16 * 1024)
	{
		ArgumentNullException.ThrowIfNull(socket);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxHeadBytes, 256);
		_socket = socket;
		_in = new byte[maxHeadBytes];
	}

	/// <summary>The socket.</summary>
	public Socket Socket => _socket;

	/// <summary>The client's address (loopback for local clients).</summary>
	public IPAddress RemoteAddress => _remoteAddress ??= (_socket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;

	/// <summary>The request read last by <see cref="TryReadRequest"/>.</summary>
	public HttpRequest Request { get; } = new();

	/// <summary>The number of requests read on this connection.</summary>
	public long RequestCount { get; private set; }

	/// <summary>Per-connection state an endpoint may keep (for example its rate-limit bucket).</summary>
	public object? State { get; set; }

	/// <summary>Whether the peer closed or the connection was disposed.</summary>
	public bool IsClosed => _disposed;

	/// <summary>
	/// Reads the next request head (skipping the rest of the previous request). Returns false when the peer closed the
	/// connection cleanly between requests; otherwise <paramref name="status"/> says whether <see cref="Request"/> parsed.
	/// Throws <see cref="IOException"/> or <see cref="SocketException"/> when the connection fails or times out.
	/// </summary>
	public bool TryReadRequest(out HttpParseStatus status)
	{
		Compact();
		var scanned = 0;
		while (true)
		{
			// RFC 9112 2.2: ignore empty lines before a request line.
			var leading = 0;
			while (leading + 1 < _length && _in[leading] == (byte)'\r' && _in[leading + 1] == (byte)'\n') leading += 2;
			if (leading > 0)
			{
				_consumed = leading;
				Compact();
				scanned = 0;
			}

			var from = Math.Max(0, scanned - 3);
			var index = _in.AsSpan(from, _length - from).IndexOf("\r\n\r\n"u8);
			if (index >= 0)
			{
				var headLength = from + index + 4;
				status = Request.Parse(_in, headLength);
				_consumed = headLength;
				_floor = headLength;
				if (status == HttpParseStatus.Ok) RequestCount++;
				return true;
			}

			scanned = _length;
			if (_length == _in.Length)
			{
				status = HttpParseStatus.HeaderTooLarge;
				return true;
			}

			var read = _socket.Receive(_in.AsSpan(_length));
			if (read <= 0)
			{
				if (_length == 0)
				{
					status = HttpParseStatus.Ok;
					return false;
				}

				throw new IOException("The connection closed in the middle of a request.");
			}

			_length += read;
		}
	}

	/// <summary>
	/// Reads the request's body (<see cref="HttpRequest.ContentLength"/> bytes) into <see cref="HttpRequest.Body"/>. The
	/// caller checks the length against its limit first. The body buffer is reused and grows as needed.
	/// </summary>
	public void ReadBody()
	{
		var length = (int)Math.Max(0, Request.ContentLength);
		if (length == 0)
		{
			Request.SetBodyLength(0);
			return;
		}

		if (Request.BodyBuffer.Length < length) Request.BodyBuffer = new byte[Math.Max(length, Math.Min(Request.BodyBuffer.Length * 2, int.MaxValue / 2))];
		var body = Request.BodyBuffer.AsSpan(0, length);
		var buffered = Math.Min(length, _length - _consumed);
		_in.AsSpan(_consumed, buffered).CopyTo(body);
		_consumed += buffered;
		var total = buffered;
		while (total < length)
		{
			var read = _socket.Receive(body[total..]);
			if (read <= 0) throw new IOException("The connection closed in the middle of a request body.");
			total += read;
		}

		Request.SetBodyLength(length);
	}

	/// <summary>
	/// Reads the request's body into <see cref="HttpRequest.Body"/>, whether framed by Content-Length or chunked
	/// (<see cref="HttpRequest.IsChunked"/>; chunk extensions and trailers are skipped), refusing one larger than
	/// <paramref name="maxBytes"/>. After <see cref="HttpBodyStatus.TooLarge"/> or <see cref="HttpBodyStatus.Malformed"/> the
	/// connection must be closed. The body buffer is reused and grows as needed.
	/// </summary>
	public HttpBodyStatus ReadBody(int maxBytes)
	{
		if (!Request.IsChunked)
		{
			if (Request.ContentLength > maxBytes) return HttpBodyStatus.TooLarge;
			ReadBody();
			return HttpBodyStatus.Ok;
		}

		var total = 0;
		while (true)
		{
			if (!TryReadLine(out var line)) return HttpBodyStatus.Malformed;
			var text = _in.AsSpan(line);
			var semicolon = text.IndexOf((byte)';');
			if (semicolon >= 0) text = text[..semicolon];
			text = text.Trim(" \t"u8);
			if (text.IsEmpty || text.Length > 7 || !Utf8Parser.TryParse(text, out int size, out var consumed, 'x') || consumed != text.Length) return HttpBodyStatus.Malformed;
			if (size == 0) break;
			if (total + (long)size > maxBytes) return HttpBodyStatus.TooLarge;
			EnsureBody(total + size);
			ReadExactly(Request.BodyBuffer.AsSpan(total, size));
			total += size;
			if (!TryReadLine(out var end) || end.End.Value != end.Start.Value) return HttpBodyStatus.Malformed;
		}

		// Trailers, up to the blank line.
		for (var i = 0; i <= HttpRequest.MaxHeaders; i++)
		{
			if (!TryReadLine(out var trailer)) return HttpBodyStatus.Malformed;
			if (trailer.End.Value == trailer.Start.Value)
			{
				Request.SetBodyLength(total);
				return HttpBodyStatus.Ok;
			}
		}

		return HttpBodyStatus.Malformed;
	}

	private void EnsureBody(int length)
	{
		if (Request.BodyBuffer.Length >= length) return;
		var grown = new byte[Math.Max(length, Math.Min(Math.Max(Request.BodyBuffer.Length * 2, 256), int.MaxValue / 2))];
		Request.BodyBuffer.CopyTo(grown, 0);
		Request.BodyBuffer = grown;
	}

	/// <summary>Reads one CRLF-terminated line of the input: its range in the input buffer, without the CRLF. False when a line does not fit the buffer.</summary>
	private bool TryReadLine(out Range line)
	{
		var scanned = 0;
		while (true)
		{
			var index = _in.AsSpan(_consumed + scanned, _length - _consumed - scanned).IndexOf("\r\n"u8);
			if (index >= 0)
			{
				var start = _consumed;
				var end = _consumed + scanned + index;
				_consumed = end + 2;
				line = new Range(start, end);
				return true;
			}

			scanned = Math.Max(0, _length - _consumed - 1);
			if (_consumed > _floor)
			{
				// Keep the head (the request's spans) and drop the body bytes already read.
				var remaining = _length - _consumed;
				_in.AsSpan(_consumed, remaining).CopyTo(_in.AsSpan(_floor));
				_consumed = _floor;
				_length = _floor + remaining;
			}
			else if (_length == _in.Length)
			{
				line = default;
				return false;
			}

			var read = _socket.Receive(_in.AsSpan(_length));
			if (read <= 0) throw new IOException("The connection closed in the middle of a request body.");
			_length += read;
		}
	}

	private void ReadExactly(Span<byte> destination)
	{
		var buffered = Math.Min(destination.Length, _length - _consumed);
		_in.AsSpan(_consumed, buffered).CopyTo(destination);
		_consumed += buffered;
		var total = buffered;
		while (total < destination.Length)
		{
			var read = _socket.Receive(destination[total..]);
			if (read <= 0) throw new IOException("The connection closed in the middle of a request body.");
			total += read;
		}
	}

	/// <summary>
	/// Writes a response: status line, <c>Content-Type</c> (when given), <c>Content-Length</c>, <c>Connection</c>,
	/// <c>X-Content-Type-Options: nosniff</c>, <paramref name="extraHeaders"/> (complete lines, each ending in CRLF) and the
	/// body (left out when <paramref name="headOnly"/>, for HEAD requests, with the length still announced).
	/// </summary>
	public void WriteResponse(int status, ReadOnlySpan<byte> contentType, ReadOnlySpan<byte> body, bool keepAlive, ReadOnlySpan<byte> extraHeaders = default, bool headOnly = false)
	{
		var reason = HttpStatus.ReasonPhrase(status);
		var capacity = 160 + reason.Length + contentType.Length + extraHeaders.Length + (headOnly ? 0 : body.Length);
		if (_out.Length < capacity) _out = new byte[Math.Max(capacity, _out.Length * 2)];

		var writer = new SpanWriter(_out);
		writer.Write("HTTP/1.1 "u8);
		writer.WriteInt(status);
		writer.Write(" "u8);
		writer.Write(reason);
		writer.Write("\r\n"u8);
		if (!contentType.IsEmpty)
		{
			writer.Write("Content-Type: "u8);
			writer.Write(contentType);
			writer.Write("\r\n"u8);
		}

		writer.Write("Content-Length: "u8);
		writer.WriteInt(body.Length);
		writer.Write(keepAlive ? "\r\nConnection: keep-alive\r\n"u8 : "\r\nConnection: close\r\n"u8);
		writer.Write("X-Content-Type-Options: nosniff\r\n"u8);
		writer.Write(extraHeaders);
		writer.Write("\r\n"u8);
		if (!headOnly) writer.Write(body);
		Send(_out.AsSpan(0, writer.Position));
	}

	/// <summary>Writes raw bytes to the socket (all of them).</summary>
	public void Send(ReadOnlySpan<byte> bytes)
	{
		while (bytes.Length > 0)
		{
			var sent = _socket.Send(bytes);
			if (sent <= 0) throw new IOException("The connection closed while sending.");
			bytes = bytes[sent..];
		}
	}

	/// <summary>
	/// Completes a WebSocket opening handshake for the current request (checked with <see cref="HttpRequest.IsWebSocketUpgrade"/>):
	/// validates <c>Sec-WebSocket-Key</c> and version 13, writes <c>101 Switching Protocols</c> (echoing
	/// <paramref name="subprotocol"/> when given) and returns the connection's WebSocket, which takes over the socket.
	/// Returns null (after answering 400 or 426) when the handshake is invalid.
	/// </summary>
	public WebSocketConnection? AcceptWebSocket(WebSocketSettings settings, ReadOnlySpan<byte> subprotocol = default)
	{
		ArgumentNullException.ThrowIfNull(settings);
		if (!Request.TryGetHeader("Sec-WebSocket-Version"u8, out var version) || !version.SequenceEqual("13"u8))
		{
			WriteResponse(426, "text/plain"u8, "WebSocket version 13 is required."u8, keepAlive: false, "Sec-WebSocket-Version: 13\r\n"u8);
			return null;
		}

		if (!Request.TryGetHeader("Sec-WebSocket-Key"u8, out var key) || key.Length != 24)
		{
			WriteResponse(400, "text/plain"u8, "A WebSocket handshake needs a Sec-WebSocket-Key."u8, keepAlive: false);
			return null;
		}

		Span<byte> material = stackalloc byte[24 + 36];
		key.CopyTo(material);
		WebSocketGuid.CopyTo(material[24..]);
		Span<byte> hash = stackalloc byte[20];
		SHA1.HashData(material, hash);
		Span<byte> accept = stackalloc byte[28];
		Base64.EncodeToUtf8(hash, accept, out _, out _);

		var writer = new SpanWriter(_out.Length >= 256 + subprotocol.Length ? _out : _out = new byte[256 + subprotocol.Length]);
		writer.Write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "u8);
		writer.Write(accept);
		writer.Write("\r\n"u8);
		if (!subprotocol.IsEmpty)
		{
			writer.Write("Sec-WebSocket-Protocol: "u8);
			writer.Write(subprotocol);
			writer.Write("\r\n"u8);
		}

		writer.Write("\r\n"u8);
		Send(_out.AsSpan(0, writer.Position));

		var leftover = _in.AsSpan(_consumed, _length - _consumed);
		var socket = new WebSocketConnection(_socket, leftover, settings);
		_consumed = _length;
		return socket;
	}

	private void Compact()
	{
		_floor = 0;
		if (_consumed == 0) return;
		var remaining = _length - _consumed;
		if (remaining > 0) _in.AsSpan(_consumed, remaining).CopyTo(_in);
		_length = remaining;
		_consumed = 0;
	}

	/// <summary>Closes the socket.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		try
		{
			_socket.Shutdown(SocketShutdown.Both);
		}
		catch (SocketException)
		{
		}
		catch (ObjectDisposedException)
		{
		}

		_socket.Dispose();
	}
}

/// <summary>Appends bytes and integers to a span (the response head builder).</summary>
internal ref struct SpanWriter(Span<byte> buffer)
{
	private readonly Span<byte> _buffer = buffer;

	public int Position { get; private set; }

	public void Write(scoped ReadOnlySpan<byte> bytes)
	{
		bytes.CopyTo(_buffer[Position..]);
		Position += bytes.Length;
	}

	public void WriteInt(long value)
	{
		Utf8Formatter.TryFormat(value, _buffer[Position..], out var written);
		Position += written;
	}
}
