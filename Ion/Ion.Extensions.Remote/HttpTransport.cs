using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Remote;

/// <summary>
/// The HTTP/1.1 and WebSocket transport, on <see cref="System.Net.Sockets"/> with blocking I/O: one accept thread, and one
/// thread per connection (plus a writer thread per WebSocket). <c>POST /</c> (or <c>/rpc</c>) carries one JSON-RPC request
/// per body; <c>GET /ws</c> with the WebSocket upgrade carries one request per text message and streams watch updates.
/// Every request needs <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
internal sealed class HttpTransport : IDisposable
{
	private const int MaxHeaderBytes = 16 * 1024;
	private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	private readonly RemoteServer _server;
	private readonly ILogger _logger;
	private readonly TcpListener _listener;
	private readonly Thread _acceptThread;
	private readonly bool _loopback;
	private readonly List<TcpClient> _clients = [];
	private volatile bool _disposed;

	public HttpTransport(RemoteServer server, IPAddress address, int port, ILogger logger)
	{
		_server = server;
		_logger = logger;
		_loopback = IPAddress.IsLoopback(address);
		_listener = new TcpListener(address, port);
		_listener.Server.NoDelay = true;
		_listener.Start(backlog: 16);
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Ion remote HTTP accept" };
		_acceptThread.Start();
	}

	public int Port { get; }

	private void AcceptLoop()
	{
		while (!_disposed)
		{
			TcpClient client;
			try
			{
				client = _listener.AcceptTcpClient();
			}
			catch (SocketException)
			{
				if (_disposed) return;
				continue;
			}
			catch (ObjectDisposedException)
			{
				return;
			}

			bool accepted;
			lock (_clients)
			{
				accepted = _clients.Count < _server.Options.MaxConnections;
				if (accepted) _clients.Add(client);
			}

			if (!accepted)
			{
				try
				{
					using var stream = client.GetStream();
					WriteResponse(stream, 503, "Service Unavailable", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "Too many connections."), keepAlive: false);
				}
				catch (IOException)
				{
				}
				finally
				{
					client.Dispose();
				}

				continue;
			}

			var thread = new Thread(() => Serve(client)) { IsBackground = true, Name = "Ion remote HTTP connection" };
			thread.Start();
		}
	}

	private void Serve(TcpClient client)
	{
		try
		{
			client.NoDelay = true;
			using var stream = client.GetStream();
			stream.ReadTimeout = 120_000;
			while (!_disposed)
			{
				var request = HttpRequestHead.Read(stream, MaxHeaderBytes);
				if (request is null) return;

				if (!CheckHost(request))
				{
					WriteResponse(stream, 403, "Forbidden", RemoteMessages.Error(null, RemoteErrorCodes.Forbidden, "The Host header must name a loopback host (localhost, 127.0.0.1 or [::1])."), keepAlive: false);
					return;
				}

				if (!_server.IsOriginAllowed(request.Header("Origin")))
				{
					WriteResponse(stream, 403, "Forbidden", RemoteMessages.Error(null, RemoteErrorCodes.Forbidden, "Requests from browser origins are refused (Ion:Remote:AllowedOrigins)."), keepAlive: false);
					return;
				}

				var access = _server.Authenticate(request.Header("Authorization"));
				if (request.IsWebSocketUpgrade)
				{
					if (access is null)
					{
						WriteUnauthorized(stream);
						return;
					}

					if (!request.Path.StartsWith("/ws", StringComparison.Ordinal))
					{
						WriteResponse(stream, 404, "Not Found", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "The WebSocket endpoint is /ws."), keepAlive: false);
						return;
					}

					ServeWebSocket(client, stream, request, access.Value);
					return;
				}

				if (request.Method != "POST")
				{
					WriteResponse(stream, 405, "Method Not Allowed", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "POST a JSON-RPC 2.0 request to /, or open a WebSocket on /ws."), keepAlive: false);
					return;
				}

				var length = request.ContentLength;
				if (length is null)
				{
					WriteResponse(stream, 411, "Length Required", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "Content-Length is required."), keepAlive: false);
					return;
				}

				if (length > _server.Options.MaxRequestBytes)
				{
					WriteResponse(stream, 413, "Payload Too Large", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, $"Requests are limited to {_server.Options.MaxRequestBytes} bytes."), keepAlive: false);
					return;
				}

				// Checked before reading the body: an unauthenticated client never gets its payload parsed.
				if (access is null)
				{
					WriteUnauthorized(stream);
					return;
				}

				var body = new byte[length.Value];
				stream.ReadExactly(body);

				var exchange = new HttpExchange(access.Value);
				var expectsResponse = _server.HandleIncoming(exchange, body);
				var keepAlive = request.KeepAlive;
				if (!expectsResponse)
				{
					WriteResponse(stream, 204, "No Content", null, keepAlive);
				}
				else if (exchange.Wait(_server.Options.RequestTimeoutMs) is { } response)
				{
					var forbidden = exchange.ErrorCode == RemoteErrorCodes.Forbidden;
					WriteResponse(stream, forbidden ? 403 : 200, forbidden ? "Forbidden" : "OK", response, keepAlive);
				}
				else
				{
					WriteResponse(stream, 504, "Gateway Timeout", RemoteMessages.Error(null, RemoteErrorCodes.Timeout, "The game thread did not answer in time (is the game loop running?)."), keepAlive: false);
					return;
				}

				if (!keepAlive) return;
			}
		}
		catch (IOException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		catch (SocketException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Ion remote: HTTP connection failed.");
		}
		finally
		{
			lock (_clients) _clients.Remove(client);
			client.Dispose();
		}
	}

	private bool CheckHost(HttpRequestHead request)
	{
		if (!_loopback) return true;
		var host = request.Header("Host");
		if (host is null) return false;

		// Strip the port.
		var name = host;
		if (name.StartsWith('['))
		{
			var end = name.IndexOf(']', StringComparison.Ordinal);
			name = end > 0 ? name[..(end + 1)] : name;
		}
		else if (name.IndexOf(':', StringComparison.Ordinal) is var colon and > 0)
		{
			name = name[..colon];
		}

		return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name is "127.0.0.1" or "[::1]";
	}

	private static void WriteUnauthorized(Stream stream) =>
		WriteResponse(stream, 401, "Unauthorized", RemoteMessages.Error(null, RemoteErrorCodes.Unauthorized, "Missing or unknown bearer token. Read it from the token file (Ion:Remote:RunDirectory/remote.json) and send 'Authorization: Bearer <token>'."), keepAlive: false, extraHeaders: "WWW-Authenticate: Bearer realm=\"ion-remote\"\r\n");

	private static void WriteResponse(Stream stream, int status, string reason, byte[]? body, bool keepAlive, string? extraHeaders = null)
	{
		var head = new StringBuilder();
		head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
		head.Append("Content-Type: application/json\r\n");
		head.Append("Cache-Control: no-store\r\n");
		head.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
		head.Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");
		if (extraHeaders is not null) head.Append(extraHeaders);
		head.Append("\r\n");
		stream.Write(Encoding.ASCII.GetBytes(head.ToString()));
		if (body is not null) stream.Write(body);
		stream.Flush();
	}

	private void ServeWebSocket(TcpClient client, NetworkStream stream, HttpRequestHead request, RemoteAccess access)
	{
		var key = request.Header("Sec-WebSocket-Key");
		if (key is null || request.Header("Sec-WebSocket-Version") != "13")
		{
			WriteResponse(stream, 400, "Bad Request", RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "A WebSocket upgrade needs Sec-WebSocket-Key and version 13."), keepAlive: false);
			return;
		}

		var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
		var head = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
		stream.Write(Encoding.ASCII.GetBytes(head));
		stream.Flush();
		stream.ReadTimeout = Timeout.Infinite;

		using var connection = new WebSocketConnection(stream, access);
		var max = _server.Options.MaxRequestBytes;
		var message = new MemoryStream();
		while (!_disposed && !connection.IsClosed)
		{
			var frame = WebSocketFrame.Read(stream, max);
			if (frame is null) return;
			var (opcode, fin, payload) = frame.Value;
			switch (opcode)
			{
				case 0x0: // continuation
				case 0x1: // text
				case 0x2: // binary (accepted as UTF-8 JSON too)
					message.Write(payload);
					if (message.Length > max) return;
					if (!fin) break;
					_server.HandleIncoming(connection, message.GetBuffer().AsSpan(0, (int)message.Length));
					message.SetLength(0);
					break;
				case 0x8: // close
					connection.SendControl(0x8, payload.Length >= 2 ? payload[..2] : []);
					return;
				case 0x9: // ping
					connection.SendControl(0xA, payload);
					break;
				case 0xA: // pong
					break;
				default:
					return;
			}
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_listener.Stop();
		lock (_clients)
		{
			foreach (var client in _clients) client.Dispose();
			_clients.Clear();
		}
	}

	/// <summary>One HTTP request's connection: the response is handed back to the connection thread.</summary>
	private sealed class HttpExchange(RemoteAccess access) : RemoteConnection("http", access)
	{
		private readonly ManualResetEventSlim _done = new();
		private byte[]? _response;

		public override bool CanStream => false;

		public override bool IsClosed => _done.IsSet;

		public int? ErrorCode { get; private set; }

		public override void Send(byte[] message)
		{
			if (_done.IsSet) return;
			_response = message;
			ErrorCode = ReadErrorCode(message);
			_done.Set();
		}

		public byte[]? Wait(int timeoutMs) => _done.Wait(timeoutMs) ? _response : null;

		private static int? ReadErrorCode(byte[] message)
		{
			var reader = new System.Text.Json.Utf8JsonReader(message);
			var depth = 0;
			var inError = false;
			while (reader.Read())
			{
				switch (reader.TokenType)
				{
					case System.Text.Json.JsonTokenType.StartObject: depth++; break;
					case System.Text.Json.JsonTokenType.EndObject: depth--; inError = false; break;
					case System.Text.Json.JsonTokenType.PropertyName when depth == 1 && reader.ValueTextEquals("error"u8):
						inError = true;
						break;
					case System.Text.Json.JsonTokenType.PropertyName when depth == 2 && inError && reader.ValueTextEquals("code"u8):
						reader.Read();
						return reader.GetInt32();
					case System.Text.Json.JsonTokenType.PropertyName when depth == 1:
						reader.Skip();
						break;
				}
			}

			return null;
		}
	}

	/// <summary>A WebSocket session: text frames out, written by the writer thread.</summary>
	private sealed class WebSocketConnection : StreamingConnection
	{
		private readonly NetworkStream _stream;
		private readonly Lock _writeLock = new();

		public WebSocketConnection(NetworkStream stream, RemoteAccess access) : base("ws", access, "Ion remote WebSocket writer")
		{
			_stream = stream;
			StartWriter();
		}

		protected override void WriteMessage(byte[] message)
		{
			lock (_writeLock) WebSocketFrame.Write(_stream, 0x1, message);
		}

		public void SendControl(byte opcode, ReadOnlySpan<byte> payload)
		{
			lock (_writeLock) WebSocketFrame.Write(_stream, opcode, payload);
		}

		protected override void CloseStream()
		{
		}
	}
}

/// <summary>An HTTP/1.1 request line and headers.</summary>
internal sealed class HttpRequestHead
{
	private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

	public string Method { get; private init; } = "";

	public string Path { get; private init; } = "/";

	public string Version { get; private init; } = "HTTP/1.1";

	public string? Header(string name) => _headers.GetValueOrDefault(name);

	public int? ContentLength => int.TryParse(Header("Content-Length"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length) ? length : null;

	public bool KeepAlive => Header("Connection") is { } connection
		? !connection.Contains("close", StringComparison.OrdinalIgnoreCase)
		: Version == "HTTP/1.1";

	public bool IsWebSocketUpgrade => Method == "GET"
		&& Header("Upgrade")?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true
		&& Header("Connection")?.Contains("upgrade", StringComparison.OrdinalIgnoreCase) == true;

	/// <summary>Reads a request head; null at the end of the stream. Throws <see cref="IOException"/> on malformed or oversized input.</summary>
	public static HttpRequestHead? Read(Stream stream, int maxBytes)
	{
		var bytes = new List<byte>(512);
		var matched = 0;
		while (true)
		{
			var b = stream.ReadByte();
			if (b < 0)
			{
				if (bytes.Count == 0) return null;
				throw new IOException("Connection closed in the middle of a request.");
			}

			bytes.Add((byte)b);
			if (bytes.Count > maxBytes) throw new IOException("Request head too large.");
			matched = b switch
			{
				'\r' when matched is 0 or 2 => matched + 1,
				'\n' when matched is 1 or 3 => matched + 1,
				'\r' => 1,
				_ => 0,
			};
			if (matched == 4) break;
		}

		var text = Encoding.ASCII.GetString(bytes.ToArray());
		var lines = text.Split("\r\n", StringSplitOptions.None);
		var parts = lines[0].Split(' ');
		if (parts.Length != 3) throw new IOException("Malformed request line.");

		var head = new HttpRequestHead { Method = parts[0], Path = parts[1], Version = parts[2] };
		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Length == 0) continue;
			var colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0) throw new IOException("Malformed header.");
			head._headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
		}

		return head;
	}
}

/// <summary>RFC 6455 framing (server side: reads masked frames, writes unmasked ones).</summary>
internal static class WebSocketFrame
{
	public static (byte Opcode, bool Fin, byte[] Payload)? Read(Stream stream, int maxPayload)
	{
		Span<byte> header = stackalloc byte[2];
		if (!TryReadExactly(stream, header)) return null;
		var fin = (header[0] & 0x80) != 0;
		var opcode = (byte)(header[0] & 0x0F);
		var masked = (header[1] & 0x80) != 0;
		long length = header[1] & 0x7F;
		if (length == 126)
		{
			Span<byte> ext = stackalloc byte[2];
			if (!TryReadExactly(stream, ext)) return null;
			length = BinaryPrimitives.ReadUInt16BigEndian(ext);
		}
		else if (length == 127)
		{
			Span<byte> ext = stackalloc byte[8];
			if (!TryReadExactly(stream, ext)) return null;
			length = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
		}

		if (length < 0 || length > maxPayload) throw new IOException("WebSocket frame too large.");
		if (!masked) throw new IOException("Client WebSocket frames must be masked.");

		Span<byte> mask = stackalloc byte[4];
		if (!TryReadExactly(stream, mask)) return null;
		var payload = new byte[length];
		if (!TryReadExactly(stream, payload)) return null;
		for (var i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
		return (opcode, fin, payload);
	}

	public static void Write(Stream stream, byte opcode, ReadOnlySpan<byte> payload)
	{
		Span<byte> header = stackalloc byte[10];
		header[0] = (byte)(0x80 | opcode);
		int headerLength;
		if (payload.Length < 126)
		{
			header[1] = (byte)payload.Length;
			headerLength = 2;
		}
		else if (payload.Length <= ushort.MaxValue)
		{
			header[1] = 126;
			BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)payload.Length);
			headerLength = 4;
		}
		else
		{
			header[1] = 127;
			BinaryPrimitives.WriteUInt64BigEndian(header[2..], (ulong)payload.Length);
			headerLength = 10;
		}

		stream.Write(header[..headerLength]);
		stream.Write(payload);
		stream.Flush();
	}

	private static bool TryReadExactly(Stream stream, Span<byte> buffer)
	{
		var total = 0;
		while (total < buffer.Length)
		{
			var read = stream.Read(buffer[total..]);
			if (read <= 0) return false;
			total += read;
		}

		return true;
	}
}
