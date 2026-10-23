namespace Ion.Extensions.Networking;

/// <summary>What a <see cref="TransportEvent"/> reports.</summary>
public enum TransportEventKind : byte
{
	/// <summary>Nothing.</summary>
	None = 0,

	/// <summary>A connection was established (on a client: to the server, connection 0).</summary>
	Connected = 1,

	/// <summary>A packet arrived; its bytes are at the start of the buffer passed to <see cref="INetworkTransport.TryReceive"/>.</summary>
	Data = 2,

	/// <summary>A connection closed.</summary>
	Disconnected = 3,
}

/// <summary>One event from <see cref="INetworkTransport.TryReceive"/>.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Connection">The transport's connection id (a client's connection to its server is 0).</param>
/// <param name="Length">For <see cref="TransportEventKind.Data"/>, the packet length in bytes.</param>
public readonly record struct TransportEvent(TransportEventKind Kind, int Connection, int Length);

/// <summary>
/// Moves packets between processes. Polled from the game thread only: <see cref="Poll"/> and <see cref="TryReceive"/>
/// in <c>First</c>, <see cref="Send"/> and <see cref="Flush"/> in <c>Last</c>. A transport may do its socket work on a
/// thread of its own, but hands the game thread finished packets through a queue and never blocks it.
/// </summary>
/// <remarks>
/// The transport knows connections, not peers: the networking module runs its own handshake (protocol, game and registry
/// hashes, join secret) on top, so every transport gets the same security checks. A transport must not allocate on its
/// receive path in steady state.
/// </remarks>
public interface INetworkTransport : IDisposable
{
	/// <summary>A short name for logs (<c>loopback</c>, <c>litenetlib</c>).</summary>
	string Name { get; }

	/// <summary>Whether the transport has been started and not stopped.</summary>
	bool IsRunning { get; }

	/// <summary>The local port once started (the actual one when 0 was asked for), or 0.</summary>
	int LocalPort { get; }

	/// <summary>Starts listening on <paramref name="bindAddress"/>:<paramref name="port"/> for at most <paramref name="maxConnections"/> connections.</summary>
	void StartServer(string bindAddress, int port, int maxConnections);

	/// <summary>Starts connecting to the server at <paramref name="address"/>:<paramref name="port"/> (its connection id is 0).</summary>
	void StartClient(string address, int port);

	/// <summary>The largest packet <see cref="Send"/> accepts for <paramref name="delivery"/>.</summary>
	int MaxPacketSize(Delivery delivery);

	/// <summary>Queues <paramref name="packet"/> (copied before returning) to <paramref name="connection"/>.</summary>
	void Send(int connection, ReadOnlySpan<byte> packet, Delivery delivery);

	/// <summary>Pumps the transport (receives what the network delivered). Called once per frame before <see cref="TryReceive"/>.</summary>
	void Poll();

	/// <summary>
	/// Takes the next event, copying a packet into <paramref name="buffer"/> (which must hold <see cref="MaxPacketSize"/>
	/// of <see cref="Delivery.ReliableOrdered"/> bytes; a larger packet is dropped). Returns false when there is none.
	/// </summary>
	bool TryReceive(Span<byte> buffer, out TransportEvent e);

	/// <summary>Sends everything queued by <see cref="Send"/>.</summary>
	void Flush();

	/// <summary>Closes <paramref name="connection"/> (after flushing what was queued to it).</summary>
	void Disconnect(int connection);

	/// <summary>Closes every connection and stops.</summary>
	void Stop();
}
