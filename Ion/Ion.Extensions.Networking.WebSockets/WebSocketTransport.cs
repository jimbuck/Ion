using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Http;

namespace Ion.Extensions.Networking.WebSockets;

/// <summary>
/// An <see cref="INetworkTransport"/> over WebSockets (RFC 6455): binary messages, one per packet. The server listens
/// with the HTTP module (<see cref="HttpSocketListener"/>, a thread per connection) and accepts upgrades on
/// <see cref="Path"/>; the client is <see cref="ClientWebSocket"/>, receiving and sending on tasks of its own. Events reach
/// the game thread through a queue of pooled buffers, so the receive path does not allocate in steady state.
/// </summary>
/// <remarks>
/// WebSockets run over TCP: every <see cref="Delivery"/> is delivered reliably and in order (an unreliable snapshot that
/// is late arrives late rather than not at all). Use it where UDP is not available (browsers, networks that block it) or
/// for relays; prefer the UDP transport for fast-paced games on open networks.
/// </remarks>
public sealed class WebSocketTransport : INetworkTransport
{
	/// <summary>The largest packet (the server's message limit).</summary>
	public const int MaxPacket = 64 * 1024;

	/// <summary>The subprotocol the client asks for and the server echoes.</summary>
	public const string Subprotocol = "ion-net";

	private const int SmallPacket = 2048;

	private readonly ConcurrentQueue<Pending> _queue = new();
	private readonly ConcurrentBag<byte[]> _free = [];
	private readonly ConcurrentDictionary<int, WebSocketConnection> _serverConnections = new();
	private HttpSocketListener? _listener;
	private int _nextConnection;
	private int _maxConnections;
	private bool _running;

	// The client.
	private ClientWebSocket? _client;
	private CancellationTokenSource? _cancel;
	private BlockingCollection<(byte[] Data, int Length)>? _outgoing;
	private Task? _clientTask;

	/// <summary>Creates a stopped transport accepting upgrades on <paramref name="path"/>.</summary>
	public WebSocketTransport(string path = "/ion-net")
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		Path = path.StartsWith('/') ? path : "/" + path;
	}

	/// <inheritdoc/>
	public string Name => "websocket";

	/// <summary>The URL path WebSocket upgrades go to (the client connects to <c>ws://address:port</c> plus it).</summary>
	public string Path { get; }

	/// <inheritdoc/>
	public bool IsRunning => _running;

	/// <inheritdoc/>
	public int LocalPort => _running && _listener is not null ? _listener.Port : 0;

	/// <inheritdoc/>
	public void StartServer(string bindAddress, int port, int maxConnections)
	{
		ThrowIfRunning();
		_maxConnections = Math.Max(1, maxConnections);
		// One spare connection slot: an extra client gets a clean 503 from the HTTP layer instead of a refused socket.
		_listener = new HttpSocketListener(Resolve(bindAddress), port, new HttpListenerSettings { Name = "Ion WebSocket transport", MaxConnections = _maxConnections + 1, ReceiveTimeoutMs = 10_000 }, Serve);
		_running = true;
	}

	/// <inheritdoc/>
	public void StartClient(string address, int port)
	{
		ThrowIfRunning();
		var host = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
		var uri = new Uri($"ws://{(host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host)}:{port}{Path}");
		_client = new ClientWebSocket();
		_client.Options.AddSubProtocol(Subprotocol);
		_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
		_cancel = new CancellationTokenSource();
		_outgoing = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
		_running = true;
		_clientTask = Task.Run(() => RunClient(uri, _client, _outgoing, _cancel.Token));
	}

	/// <inheritdoc/>
	public int MaxPacketSize(Delivery delivery) => MaxPacket;

	/// <inheritdoc/>
	public void Send(int connection, ReadOnlySpan<byte> packet, Delivery delivery)
	{
		if (!_running || packet.Length > MaxPacket) return;
		if (_listener is not null)
		{
			// The WebSocket copies the message into its send ring; its writer thread sends it.
			if (_serverConnections.TryGetValue(connection, out var socket)) socket.Send(packet, WebSocketOpcode.Binary);
			return;
		}

		if (connection != 0 || _outgoing is null || _client?.State != WebSocketState.Open) return;
		var data = Rent(packet.Length);
		packet.CopyTo(data);
		if (!_outgoing.TryAdd((data, packet.Length))) Release(data);
	}

	/// <inheritdoc/>
	public void Poll()
	{
	}

	/// <inheritdoc/>
	public bool TryReceive(Span<byte> buffer, out TransportEvent e)
	{
		while (_queue.TryDequeue(out var next))
		{
			if (next.Kind != TransportEventKind.Data)
			{
				e = new TransportEvent(next.Kind, next.Connection, 0);
				return true;
			}

			var fits = next.Length <= buffer.Length;
			if (fits) next.Data.AsSpan(0, next.Length).CopyTo(buffer);
			Release(next.Data!);
			if (!fits) continue;
			e = new TransportEvent(TransportEventKind.Data, next.Connection, next.Length);
			return true;
		}

		e = default;
		return false;
	}

	/// <inheritdoc/>
	public void Flush()
	{
		// Sends go out as they are queued (the server's writer threads, the client's send loop).
	}

	/// <inheritdoc/>
	public void Disconnect(int connection)
	{
		if (_listener is not null)
		{
			if (_serverConnections.TryRemove(connection, out var socket)) socket.Dispose();
			return;
		}

		if (connection == 0) _cancel?.Cancel();
	}

	/// <inheritdoc/>
	public void Stop()
	{
		if (!_running) return;
		_running = false;
		_listener?.Dispose();
		_listener = null;
		foreach (var socket in _serverConnections.Values) socket.Dispose();
		_serverConnections.Clear();

		_cancel?.Cancel();
		_outgoing?.CompleteAdding();
		try
		{
			_clientTask?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_client?.Dispose();
		_client = null;
		_outgoing?.Dispose();
		_outgoing = null;
		_cancel?.Dispose();
		_cancel = null;
		_clientTask = null;
		while (_queue.TryDequeue(out var pending))
		{
			if (pending.Data is not null) Release(pending.Data);
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Stop();

	// Server (one thread per connection, from the HTTP listener) ---------------------------------------------------------

	private void Serve(HttpConnection connection)
	{
		if (!connection.TryReadRequest(out _)) return;
		var request = connection.Request;
		if (!request.IsWebSocketUpgrade || request.Path != Path)
		{
			connection.WriteResponse(404, "text/plain"u8, "Not an Ion networking endpoint."u8, keepAlive: false);
			return;
		}

		if (_serverConnections.Count >= _maxConnections)
		{
			connection.WriteResponse(503, "text/plain"u8, "The server is full."u8, keepAlive: false);
			return;
		}

		var socket = connection.AcceptWebSocket(new WebSocketSettings { MaxMessageBytes = MaxPacket, WriterThreadName = "Ion WebSocket transport writer" }, System.Text.Encoding.ASCII.GetBytes(Subprotocol));
		if (socket is null) return;
		var id = Interlocked.Increment(ref _nextConnection);
		_serverConnections[id] = socket;
		_queue.Enqueue(new Pending(TransportEventKind.Connected, id, null, 0));
		try
		{
			while (_running && socket.ReadMessage(out var opcode, out var payload))
			{
				if (opcode != WebSocketOpcode.Binary || payload.Length > MaxPacket) continue;
				var data = Rent(payload.Length);
				payload.CopyTo(data);
				_queue.Enqueue(new Pending(TransportEventKind.Data, id, data, payload.Length));
			}
		}
		finally
		{
			_serverConnections.TryRemove(id, out _);
			socket.Dispose();
			_queue.Enqueue(new Pending(TransportEventKind.Disconnected, id, null, 0));
		}
	}

	// Client (tasks) -----------------------------------------------------------------------------------------------------

	private async Task RunClient(Uri uri, ClientWebSocket client, BlockingCollection<(byte[] Data, int Length)> outgoing, CancellationToken cancel)
	{
		Task? sender = null;
		try
		{
			await client.ConnectAsync(uri, cancel).ConfigureAwait(false);
			_queue.Enqueue(new Pending(TransportEventKind.Connected, 0, null, 0));
			sender = Task.Run(() => SendLoop(client, outgoing, cancel), CancellationToken.None);

			var message = new byte[SmallPacket];
			var length = 0;
			while (!cancel.IsCancellationRequested && client.State == WebSocketState.Open)
			{
				if (length == message.Length) Array.Resize(ref message, Math.Min(MaxPacket + 1, message.Length * 2));
				var result = await client.ReceiveAsync(message.AsMemory(length), cancel).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close) break;
				length += result.Count;
				if (length > MaxPacket) break;
				if (!result.EndOfMessage) continue;
				if (result.MessageType == WebSocketMessageType.Binary)
				{
					var data = Rent(length);
					message.AsSpan(0, length).CopyTo(data);
					_queue.Enqueue(new Pending(TransportEventKind.Data, 0, data, length));
				}

				length = 0;
			}
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
		{
			// The server went away, refused the connection, or the transport stopped.
		}
		finally
		{
			outgoing.CompleteAdding();
			if (client.State == WebSocketState.Open)
			{
				try
				{
					using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
					await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
				{
				}
			}

			if (sender is not null)
			{
				try
				{
					await sender.ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
				{
				}
			}

			_queue.Enqueue(new Pending(TransportEventKind.Disconnected, 0, null, 0));
		}
	}

	private async Task SendLoop(ClientWebSocket client, BlockingCollection<(byte[] Data, int Length)> outgoing, CancellationToken cancel)
	{
		foreach (var (data, length) in outgoing.GetConsumingEnumerable(CancellationToken.None))
		{
			try
			{
				if (!cancel.IsCancellationRequested && client.State == WebSocketState.Open)
				{
					await client.SendAsync(data.AsMemory(0, length), WebSocketMessageType.Binary, endOfMessage: true, cancel).ConfigureAwait(false);
				}
			}
			catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
			{
			}
			finally
			{
				Release(data);
			}
		}
	}

	// Helpers -----------------------------------------------------------------------------------------------------------

	private byte[] Rent(int length) => length <= SmallPacket ? (_free.TryTake(out var free) ? free : new byte[SmallPacket]) : ArrayPool<byte>.Shared.Rent(length);

	private void Release(byte[] data)
	{
		if (data.Length == SmallPacket) _free.Add(data);
		else ArrayPool<byte>.Shared.Return(data);
	}

	private static IPAddress Resolve(string address)
	{
		if (string.IsNullOrEmpty(address) || address == "localhost") return IPAddress.Loopback;
		if (IPAddress.TryParse(address, out var parsed)) return parsed;
		throw new ArgumentException($"'{address}' is not an IP address.", nameof(address));
	}

	private void ThrowIfRunning()
	{
		if (_running) throw new InvalidOperationException("The transport is already started.");
	}

	private readonly record struct Pending(TransportEventKind Kind, int Connection, byte[]? Data, int Length);
}

/// <summary>Registration of the WebSocket transport.</summary>
public static class WebSocketTransportExtensions
{
	/// <summary>Registers the <see cref="WebSocketTransport"/> (upgrades on <paramref name="path"/>) as the networking module's <see cref="INetworkTransport"/>.</summary>
	public static IServiceCollection AddWebSocketTransport(this IServiceCollection services, string path = "/ion-net")
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<INetworkTransport>(_ => new WebSocketTransport(path));
		return services;
	}

	/// <summary>Registers the <see cref="WebSocketTransport"/> and the networking module (<c>AddNetworking</c>).</summary>
	public static IonApplicationBuilder AddWebSocketTransport(this IonApplicationBuilder builder, string path = "/ion-net")
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddNetworking();
		builder.Services.AddWebSocketTransport(path);
		return builder;
	}
}
