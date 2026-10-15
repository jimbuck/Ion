using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Http;

namespace Ion.Extensions.Remote;

/// <summary>
/// The HTTP/1.1 and WebSocket transport, on the shared server core (<see cref="HttpSocketListener"/>, blocking sockets):
/// one accept thread, and one thread per connection (plus a writer thread per WebSocket). <c>POST /</c> (or <c>/rpc</c>)
/// carries one JSON-RPC request per body; <c>GET /ws</c> with the WebSocket upgrade carries one request per text message
/// and streams watch updates. Every request needs <c>Authorization: Bearer &lt;token&gt;</c>. The listener applies the
/// Host and Origin checks; <see cref="RemoteHttpEndpoint"/> (also mounted by the web module at <c>/rpc</c>) the rest.
/// </summary>
internal sealed class HttpTransport : IDisposable
{
	private readonly RemoteServer _server;
	private readonly HttpSocketListener _listener;

	public HttpTransport(RemoteServer server, IPAddress address, int port, ILogger logger)
	{
		_server = server;
		var settings = new HttpListenerSettings
		{
			Name = "Ion remote HTTP",
			MaxConnections = server.Options.MaxConnections,
			MaxHeadBytes = RemoteHttpEndpoint.MaxHeadBytes,
			BusyContentType = "application/json",
			BusyBody = RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "Too many connections."),
		};
		_listener = new HttpSocketListener(address, port, settings, Serve, logger);
	}

	public int Port => _listener.Port;

	private void Serve(HttpConnection connection)
	{
		var endpoint = _server.HttpEndpoint;
		while (connection.TryReadRequest(out var status))
		{
			if (status != HttpParseStatus.Ok)
			{
				RemoteHttpEndpoint.WriteJson(connection, HttpStatus.FromParseStatus(status), RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "Malformed HTTP request."), keepAlive: false);
				return;
			}

			var request = connection.Request;
			if (!HttpSecurity.CheckHost(request, _listener.IsLoopback))
			{
				RemoteHttpEndpoint.WriteJson(connection, 403, RemoteMessages.Error(null, RemoteErrorCodes.Forbidden, "The Host header must name a loopback host (localhost, 127.0.0.1 or [::1])."), keepAlive: false);
				return;
			}

			if (!HttpSecurity.CheckOrigin(request, _server.Options.AllowedOrigins, allowSameOrigin: false))
			{
				RemoteHttpEndpoint.WriteJson(connection, 403, RemoteMessages.Error(null, RemoteErrorCodes.Forbidden, "Requests from browser origins are refused (Ion:Remote:AllowedOrigins)."), keepAlive: false);
				return;
			}

			if (request.IsWebSocketUpgrade && !request.RawPath.StartsWith("/ws"u8) && !request.RawPath.SequenceEqual("/rpc"u8))
			{
				// Authentication first: an unauthenticated client learns nothing about the endpoints.
				if (_server.Authenticate(request) is null)
				{
					RemoteHttpEndpoint.WriteUnauthorized(connection);
					return;
				}

				RemoteHttpEndpoint.WriteJson(connection, 404, RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "The WebSocket endpoint is /ws."), keepAlive: false);
				return;
			}

			if (!endpoint.Serve(connection)) return;
		}
	}

	public void Dispose() => _listener.Dispose();
}

/// <summary>
/// The remote protocol as an HTTP endpoint (<see cref="IHttpEndpoint"/>): authenticates the bearer token, serves a
/// JSON-RPC request per <c>POST</c> body and a WebSocket session per upgrade. Used by the remote module's own listener and
/// mounted by the web module at <c>/rpc</c>; there it refuses clients that reach a non-loopback interface unless the
/// remote protocol itself allows non-loopback access (<see cref="RemoteOptions.AllowNonLoopback"/>), so hosting it on a
/// LAN-bound web server never widens the remote module's own policy.
/// </summary>
internal sealed class RemoteHttpEndpoint(RemoteServer server) : IHttpEndpoint
{
	public const int MaxHeadBytes = 16 * 1024;

	public string Path => "/rpc";

	public bool Serve(HttpConnection connection)
	{
		var request = connection.Request;
		if (!server.IsStarted)
		{
			WriteJson(connection, 503, RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "The remote protocol has not started yet."), keepAlive: false);
			return false;
		}

		if (!server.Options.AllowNonLoopback && connection.Socket.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address))
		{
			WriteJson(connection, 403, RemoteMessages.Error(null, RemoteErrorCodes.Forbidden, "The remote protocol only answers loopback clients (Ion:Remote:AllowNonLoopback)."), keepAlive: false);
			return false;
		}

		var access = server.Authenticate(request);
		if (request.IsWebSocketUpgrade)
		{
			if (access is null)
			{
				WriteUnauthorized(connection);
				return false;
			}

			ServeWebSocket(connection, access.Value);
			return false;
		}

		if (request.Method != HttpVerb.Post)
		{
			WriteJson(connection, 405, RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "POST a JSON-RPC 2.0 request to /, or open a WebSocket on /ws."), keepAlive: false);
			return false;
		}

		var length = request.ContentLength;
		if (length < 0)
		{
			WriteJson(connection, 411, RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, "Content-Length is required."), keepAlive: false);
			return false;
		}

		if (length > server.Options.MaxRequestBytes)
		{
			WriteJson(connection, 413, RemoteMessages.Error(null, RemoteErrorCodes.InvalidRequest, $"Requests are limited to {server.Options.MaxRequestBytes} bytes."), keepAlive: false);
			return false;
		}

		// Checked before reading the body: an unauthenticated client never gets its payload parsed.
		if (access is null)
		{
			WriteUnauthorized(connection);
			return false;
		}

		connection.ReadBody();

		var exchange = new HttpExchange(access.Value);
		var expectsResponse = server.HandleIncoming(exchange, request.Body);
		var keepAlive = request.KeepAlive;
		if (!expectsResponse)
		{
			connection.WriteResponse(204, default, default, keepAlive, NoStore);
		}
		else if (exchange.Wait(server.Options.RequestTimeoutMs) is { } response)
		{
			var forbidden = exchange.ErrorCode == RemoteErrorCodes.Forbidden;
			WriteJson(connection, forbidden ? 403 : 200, response, keepAlive);
		}
		else
		{
			WriteJson(connection, 504, RemoteMessages.Error(null, RemoteErrorCodes.Timeout, "The game thread did not answer in time (is the game loop running?)."), keepAlive: false);
			return false;
		}

		return keepAlive;
	}

	private static ReadOnlySpan<byte> NoStore => "Cache-Control: no-store\r\n"u8;

	internal static void WriteJson(HttpConnection connection, int status, byte[] body, bool keepAlive) =>
		connection.WriteResponse(status, "application/json"u8, body, keepAlive, NoStore);

	internal static void WriteUnauthorized(HttpConnection connection) =>
		connection.WriteResponse(401, "application/json"u8, RemoteMessages.Error(null, RemoteErrorCodes.Unauthorized, "Missing or unknown bearer token. Read it from the token file (Ion:Remote:RunDirectory/remote.json) and send 'Authorization: Bearer <token>'."), keepAlive: false,
			"Cache-Control: no-store\r\nWWW-Authenticate: Bearer realm=\"ion-remote\"\r\n"u8);

	private void ServeWebSocket(HttpConnection connection, RemoteAccess access)
	{
		var settings = new WebSocketSettings { MaxMessageBytes = server.Options.MaxRequestBytes, SendQueueCapacity = 4096, WriterThreadName = "Ion remote WebSocket writer" };
		using var socket = connection.AcceptWebSocket(settings);
		if (socket is null) return;
		var session = new WebSocketSession(socket, access);
		while (socket.ReadMessage(out _, out var payload))
		{
			server.HandleIncoming(session, payload);
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
			var reader = new Utf8JsonReader(message);
			var depth = 0;
			var inError = false;
			while (reader.Read())
			{
				switch (reader.TokenType)
				{
					case JsonTokenType.StartObject: depth++; break;
					case JsonTokenType.EndObject: depth--; inError = false; break;
					case JsonTokenType.PropertyName when depth == 1 && reader.ValueTextEquals("error"u8):
						inError = true;
						break;
					case JsonTokenType.PropertyName when depth == 2 && inError && reader.ValueTextEquals("code"u8):
						reader.Read();
						return reader.GetInt32();
					case JsonTokenType.PropertyName when depth == 1:
						reader.Skip();
						break;
				}
			}

			return null;
		}
	}

	/// <summary>A WebSocket session: text frames out, written by the socket's writer thread.</summary>
	private sealed class WebSocketSession(WebSocketConnection socket, RemoteAccess access) : RemoteConnection("ws", access)
	{
		public override bool CanStream => true;

		public override bool IsClosed => socket.IsClosed;

		public override void Send(byte[] message) => socket.Send(message);
	}
}
