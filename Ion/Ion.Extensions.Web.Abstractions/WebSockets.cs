using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Ion.Extensions.Http;

namespace Ion.Extensions.Web;

/// <summary>What a <see cref="WebSocketMessage"/> reports.</summary>
public enum WebSocketMessageKind : byte
{
	/// <summary>A client connected (the handshake succeeded).</summary>
	Connected,
	/// <summary>A UTF-8 text message.</summary>
	Text,
	/// <summary>A binary message.</summary>
	Binary,
	/// <summary>The client disconnected (its last message).</summary>
	Disconnected,
}

/// <summary>
/// What a <see cref="WebSocketAttribute"/> handler receives on the game thread: a client connecting, one of its messages,
/// or its disconnection. <see cref="Data"/> points into a buffer the server reuses: it is valid during the call only.
/// </summary>
public readonly ref struct WebSocketMessage
{
	/// <summary>Creates a message (the server does; tests can too).</summary>
	public WebSocketMessage(WebSocketMessageKind kind, WebSocketClient client, ReadOnlySpan<byte> data)
	{
		ArgumentNullException.ThrowIfNull(client);
		Kind = kind;
		Client = client;
		Data = data;
	}

	/// <summary>What happened.</summary>
	public WebSocketMessageKind Kind { get; }

	/// <summary>The client (reply with <see cref="WebSocketClient.Send(ReadOnlySpan{byte})"/>).</summary>
	public WebSocketClient Client { get; }

	/// <summary>The endpoint's channel (push to every client of the endpoint).</summary>
	public WebSocketChannel Channel => Client.Channel;

	/// <summary>The payload of a <see cref="WebSocketMessageKind.Text"/> or <see cref="WebSocketMessageKind.Binary"/> message.</summary>
	public ReadOnlySpan<byte> Data { get; }

	/// <summary>Whether this is a text or binary message (not a connection change).</summary>
	public bool IsMessage => Kind is WebSocketMessageKind.Text or WebSocketMessageKind.Binary;

	/// <summary>The payload as a string (allocates).</summary>
	public string Text => Encoding.UTF8.GetString(Data);

	/// <summary>Deserializes the payload with source-generated type information; false (and the default) when it is not valid JSON of that shape.</summary>
	public bool TryReadJson<T>(JsonTypeInfo<T> type, out T? value)
	{
		ArgumentNullException.ThrowIfNull(type);
		try
		{
			value = JsonSerializer.Deserialize(Data, type);
			return true;
		}
		catch (JsonException)
		{
			value = default;
			return false;
		}
	}
}

/// <summary>
/// One connected client of a <see cref="WebSocketAttribute"/> endpoint. Sending is safe from any thread and never blocks
/// on the network: the message is copied into the client's queue, drained by its writer thread (a client that stops
/// reading is disconnected).
/// </summary>
public sealed class WebSocketClient
{
	private readonly WebSocketConnection _socket;
	private readonly Lock _jsonLock = new();
	private ArrayBufferWriter<byte>? _jsonBuffer;
	private Utf8JsonWriter? _json;

	internal WebSocketClient(long id, WebSocketConnection socket, WebSocketChannel channel, IPAddress remoteAddress, bool isAuthenticated)
	{
		Id = id;
		_socket = socket;
		Channel = channel;
		RemoteAddress = remoteAddress;
		IsAuthenticated = isAuthenticated;
	}

	/// <summary>A number unique among the server's clients (from 1).</summary>
	public long Id { get; }

	/// <summary>The endpoint's channel.</summary>
	public WebSocketChannel Channel { get; }

	/// <summary>The client's address.</summary>
	public IPAddress RemoteAddress { get; }

	/// <summary>Whether the client presented the web server's token (always true when none is configured).</summary>
	public bool IsAuthenticated { get; }

	/// <summary>Whether the connection is closed.</summary>
	public bool IsClosed => _socket.IsClosed;

	/// <summary>Anything the game wants to keep per client (for example the player it controls).</summary>
	public object? State { get; set; }

	/// <summary>The number of messages sent to the client.</summary>
	public long SentCount => _socket.SentCount;

	/// <summary>Sends a text message (UTF-8 bytes). Returns false when the client is gone.</summary>
	public bool Send(ReadOnlySpan<byte> utf8) => _socket.Send(utf8, WebSocketOpcode.Text);

	/// <summary>Sends a text message.</summary>
	public bool Send(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		var max = Encoding.UTF8.GetMaxByteCount(text.Length);
		if (max <= 1024)
		{
			Span<byte> buffer = stackalloc byte[max];
			return Send(buffer[..Encoding.UTF8.GetBytes(text, buffer)]);
		}

		return Send(Encoding.UTF8.GetBytes(text));
	}

	/// <summary>Sends a binary message.</summary>
	public bool SendBinary(ReadOnlySpan<byte> data) => _socket.Send(data, WebSocketOpcode.Binary);

	/// <summary>Sends <paramref name="value"/> as a JSON text message.</summary>
	public bool SendJson<T>(T value, JsonTypeInfo<T> type)
	{
		ArgumentNullException.ThrowIfNull(type);
		lock (_jsonLock)
		{
			var payload = WebJsonScratch.Serialize(ref _jsonBuffer, ref _json, value, type);
			return Send(payload);
		}
	}

	/// <summary>Closes the connection with <paramref name="code"/> (1000: normal closure).</summary>
	public void Close(ushort code = 1000) => _socket.Close(code);

	internal WebSocketConnection Socket => _socket;
}

/// <summary>
/// The clients of one <see cref="WebSocketAttribute"/> endpoint, for pushing: <see cref="Broadcast(ReadOnlySpan{byte})"/>
/// copies a message into every client's queue. Get it from <see cref="IWebServer.Channel"/> or
/// <see cref="WebSocketMessage.Channel"/>. Safe from any thread; allocation-free in steady state.
/// </summary>
public sealed class WebSocketChannel
{
	private readonly List<WebSocketClient> _clients = [];
	private readonly Lock _jsonLock = new();
	private ArrayBufferWriter<byte>? _jsonBuffer;
	private Utf8JsonWriter? _json;

	/// <summary>Creates the channel of <paramref name="path"/>.</summary>
	public WebSocketChannel(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		Path = path;
	}

	/// <summary>The endpoint's path.</summary>
	public string Path { get; }

	/// <summary>The number of connected clients.</summary>
	public int Count
	{
		get
		{
			lock (_clients) return _clients.Count;
		}
	}

	/// <summary>The number of messages queued by broadcasts (one per client reached).</summary>
	public long BroadcastCount { get; private set; }

	/// <summary>A copy of the connected clients (allocates).</summary>
	public WebSocketClient[] Clients()
	{
		lock (_clients) return [.. _clients];
	}

	/// <summary>Sends a text message (UTF-8) to every client; returns how many it was queued for.</summary>
	public int Broadcast(ReadOnlySpan<byte> utf8) => BroadcastFrame(utf8, WebSocketOpcode.Text);

	/// <summary>Sends a text message to every client.</summary>
	public int Broadcast(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		var max = Encoding.UTF8.GetMaxByteCount(text.Length);
		if (max <= 1024)
		{
			Span<byte> buffer = stackalloc byte[max];
			return Broadcast(buffer[..Encoding.UTF8.GetBytes(text, buffer)]);
		}

		return Broadcast(Encoding.UTF8.GetBytes(text));
	}

	/// <summary>Sends a binary message to every client.</summary>
	public int BroadcastBinary(ReadOnlySpan<byte> data) => BroadcastFrame(data, WebSocketOpcode.Binary);

	/// <summary>Sends <paramref name="value"/> as JSON to every client.</summary>
	public int BroadcastJson<T>(T value, JsonTypeInfo<T> type)
	{
		ArgumentNullException.ThrowIfNull(type);
		lock (_jsonLock)
		{
			var payload = WebJsonScratch.Serialize(ref _jsonBuffer, ref _json, value, type);
			return Broadcast(payload);
		}
	}

	private int BroadcastFrame(ReadOnlySpan<byte> payload, WebSocketOpcode opcode)
	{
		var sent = 0;
		lock (_clients)
		{
			for (var i = 0; i < _clients.Count; i++)
			{
				if (_clients[i].Socket.Send(payload, opcode)) sent++;
			}

			BroadcastCount += sent;
		}

		return sent;
	}

	internal void Add(WebSocketClient client)
	{
		lock (_clients) _clients.Add(client);
	}

	internal void Remove(WebSocketClient client)
	{
		lock (_clients) _clients.Remove(client);
	}

	internal void CloseAll()
	{
		lock (_clients)
		{
			foreach (var client in _clients) client.Close(1001);
		}
	}
}

/// <summary>JSON serialization into a reused buffer.</summary>
internal static class WebJsonScratch
{
	public static ReadOnlySpan<byte> Serialize<T>(ref ArrayBufferWriter<byte>? buffer, ref Utf8JsonWriter? writer, T value, JsonTypeInfo<T> type)
	{
		buffer ??= new ArrayBufferWriter<byte>(512);
		buffer.ResetWrittenCount();
		if (writer is null) writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });
		else writer.Reset(buffer);
		JsonSerializer.Serialize(writer, value, type);
		writer.Flush();
		return buffer.WrittenSpan;
	}
}
