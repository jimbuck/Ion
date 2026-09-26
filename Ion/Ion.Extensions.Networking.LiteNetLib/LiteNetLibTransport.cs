using System.Buffers;
using System.Net;
using System.Net.Sockets;

using LiteNetLib;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ion.Extensions.Networking.LiteNetLib;

/// <summary>
/// An <see cref="INetworkTransport"/> over UDP on LiteNetLib's <see cref="LiteNetManager"/>. LiteNetLib receives and
/// resends on its own threads; its events are delivered on the game thread by <see cref="Poll"/> (no unsynchronized
/// events), where each packet is copied into a pooled buffer of the transport's queue, and <see cref="Flush"/> asks the
/// logic thread to send at once instead of at its next update. Only the transport layer of LiteNetLib is used: the
/// networking module runs its own handshake, security checks and serialization on top.
/// </summary>
public sealed class LiteNetLibTransport : INetworkTransport, ILiteNetEventListener
{
	/// <summary>The MTU forced on every peer (MTU discovery is off, so the largest unreliable packet is known up front).</summary>
	public const int Mtu = 1200;

	/// <summary>The largest unreliable or sequenced packet (the MTU minus LiteNetLib's headers).</summary>
	public const int MaxUnreliablePacket = 1150;

	/// <summary>The largest reliable packet (LiteNetLib fragments it).</summary>
	public const int MaxReliablePacket = 32 * 1024;

	// Not a secret: a tag that keeps stray LiteNetLib clients of other applications out before Ion's own handshake.
	private const string ConnectionKey = "ion-net";

	private readonly LiteNetManager _manager;
	// Received packets waiting for TryReceive, in buffers of a free list (packets up to one MTU, nearly all of them;
	// PooledBuffers are made when the transport starts) or rented (larger, fragmented reliable ones), so the receive path
	// does not allocate.
	private const int SmallPacket = Mtu;
	private const int PooledBuffers = 128;
	private readonly Queue<Pending> _queue = new(256);
	private readonly Stack<byte[]> _free = new(256);
	private LiteNetPeer?[] _connections = new LiteNetPeer?[8];
	private bool _server;
	private int _maxConnections;
	private bool _running;

	/// <summary>Creates a stopped transport.</summary>
	public LiteNetLibTransport()
	{
		_manager = new LiteNetManager(this)
		{
			AutoRecycle = true,
			UnsyncedEvents = false,
			MtuOverride = Mtu,
			UpdateTime = 5,
			DisconnectTimeout = 10_000,
		};
	}

	/// <inheritdoc/>
	public string Name => "litenetlib";

	/// <inheritdoc/>
	public bool IsRunning => _running;

	/// <inheritdoc/>
	public int LocalPort => _running ? _manager.LocalPort : 0;

	/// <summary>Packets larger than the unreliable limit sent reliably instead.</summary>
	public long Oversized { get; private set; }

	/// <inheritdoc/>
	public void StartServer(string bindAddress, int port, int maxConnections)
	{
		ThrowIfRunning();
		Prewarm();
		var address = Resolve(bindAddress);
		_server = true;
		_maxConnections = maxConnections;
		var started = address.AddressFamily == AddressFamily.InterNetworkV6
			? _manager.Start(IPAddress.Any, address, port)
			: _manager.Start(address, IPAddress.IPv6None, port);
		if (!started) throw new InvalidOperationException($"Could not listen on {bindAddress}:{port}.");
		_running = true;
	}

	/// <inheritdoc/>
	public void StartClient(string address, int port)
	{
		ThrowIfRunning();
		Prewarm();
		_server = false;
		if (!_manager.Start()) throw new InvalidOperationException("Could not open a UDP socket.");
		_running = true;
		var peer = _manager.Connect(address, port, ConnectionKey);
		SetConnection(0, peer);
	}

	/// <inheritdoc/>
	public int MaxPacketSize(Delivery delivery) => delivery is Delivery.Unreliable or Delivery.Sequenced ? MaxUnreliablePacket : MaxReliablePacket;

	/// <inheritdoc/>
	public void Send(int connection, ReadOnlySpan<byte> packet, Delivery delivery)
	{
		var peer = GetConnection(connection);
		if (!_running || peer is null || peer.ConnectionState != ConnectionState.Connected) return;
		var method = Method(delivery);
		if (method is DeliveryMethod.Unreliable or DeliveryMethod.Sequenced && packet.Length > peer.GetMaxSinglePacketSize(method))
		{
			Oversized++;
			method = DeliveryMethod.ReliableUnordered;
		}

		if (packet.Length > MaxReliablePacket) return;
		peer.Send(packet, method);
	}

	/// <inheritdoc/>
	public void Poll()
	{
		if (_running) _manager.PollEvents();
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
		if (_running) _manager.TriggerUpdate();
	}

	/// <inheritdoc/>
	public void Disconnect(int connection)
	{
		var peer = GetConnection(connection);
		if (peer is null) return;
		_manager.DisconnectPeer(peer);
	}

	/// <inheritdoc/>
	public void Stop()
	{
		if (!_running) return;
		_manager.Stop(true);
		_running = false;
		while (_queue.TryDequeue(out var pending))
		{
			if (pending.Data is not null) Release(pending.Data);
		}

		Array.Clear(_connections);
	}

	/// <inheritdoc/>
	public void Dispose() => Stop();

	// Listener (called on the game thread from PollEvents) ----------------------------------------------------------------

	void ILiteNetEventListener.OnConnectionRequest(LiteConnectionRequest request)
	{
		if (_server && _manager.ConnectedPeersCount < _maxConnections) request.AcceptIfKey(ConnectionKey);
		else request.Reject();
	}

	void ILiteNetEventListener.OnPeerConnected(LiteNetPeer peer)
	{
		var connection = ConnectionOf(peer);
		SetConnection(connection, peer);
		_queue.Enqueue(new Pending(TransportEventKind.Connected, connection, null, 0));
	}

	void ILiteNetEventListener.OnPeerDisconnected(LiteNetPeer peer, DisconnectInfo disconnectInfo)
	{
		var connection = ConnectionOf(peer);
		if (GetConnection(connection) == peer) SetConnection(connection, null);
		_queue.Enqueue(new Pending(TransportEventKind.Disconnected, connection, null, 0));
	}

	void ILiteNetEventListener.OnNetworkReceive(LiteNetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
	{
		var bytes = reader.GetRemainingBytesSpan();
		var data = bytes.Length <= SmallPacket ? (_free.TryPop(out var free) ? free : new byte[SmallPacket]) : ArrayPool<byte>.Shared.Rent(bytes.Length);
		bytes.CopyTo(data);
		_queue.Enqueue(new Pending(TransportEventKind.Data, ConnectionOf(peer), data, bytes.Length));
	}

	void ILiteNetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
	{
	}

	void ILiteNetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
	{
	}

	void ILiteNetEventListener.OnNetworkLatencyUpdate(LiteNetPeer peer, int latency)
	{
	}

	void ILiteNetEventListener.OnPeerAddressChanged(LiteNetPeer peer, IPEndPoint previousAddress)
	{
	}

	void ILiteNetEventListener.OnMessageDelivered(LiteNetPeer peer, object userData)
	{
	}

	// Helpers -----------------------------------------------------------------------------------------------------------

	private void Prewarm()
	{
		while (_free.Count < PooledBuffers) _free.Push(new byte[SmallPacket]);
	}

	private void Release(byte[] data)
	{
		if (data.Length == SmallPacket) _free.Push(data);
		else ArrayPool<byte>.Shared.Return(data);
	}

	private int ConnectionOf(LiteNetPeer peer) => _server ? peer.Id + 1 : 0;

	private LiteNetPeer? GetConnection(int connection) => (uint)connection < (uint)_connections.Length ? _connections[connection] : null;

	private void SetConnection(int connection, LiteNetPeer? peer)
	{
		if (connection >= _connections.Length) Array.Resize(ref _connections, Math.Max(_connections.Length * 2, connection + 1));
		_connections[connection] = peer;
	}

	private static DeliveryMethod Method(Delivery delivery) => delivery switch
	{
		Delivery.Unreliable => DeliveryMethod.Unreliable,
		Delivery.Sequenced => DeliveryMethod.Sequenced,
		Delivery.ReliableUnordered => DeliveryMethod.ReliableUnordered,
		_ => DeliveryMethod.ReliableOrdered,
	};

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

/// <summary>Registration of the LiteNetLib transport.</summary>
public static class LiteNetLibTransportExtensions
{
	/// <summary>Registers the <see cref="LiteNetLibTransport"/> as the networking module's <see cref="INetworkTransport"/>.</summary>
	public static IServiceCollection AddLiteNetLibTransport(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<INetworkTransport>(static _ => new LiteNetLibTransport());
		return services;
	}
}
