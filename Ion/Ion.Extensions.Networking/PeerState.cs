namespace Ion.Extensions.Networking;

/// <summary>Where a connection is in its life.</summary>
internal enum PeerStage : byte
{
	/// <summary>Transport connected, handshake not accepted yet.</summary>
	Handshaking,

	/// <summary>Handshake accepted.</summary>
	Connected,

	/// <summary>Disconnect sent; the transport connection closes shortly (so the reason reaches the peer).</summary>
	Closing,
}

/// <summary>The packed outbound messages of one peer for one delivery method.</summary>
internal sealed class Outbox(int size)
{
	public readonly byte[] Buffer = new byte[size];

	/// <summary>The bytes used, header included (the header is written when the packet is sent).</summary>
	public int Length = Protocol.HeaderSize;

	public int Count;

	public void Reset()
	{
		Length = Protocol.HeaderSize;
		Count = 0;
	}
}

/// <summary>The state of one connection: a client on the server, the server on a client.</summary>
internal sealed class PeerState
{
	public PeerState(int connection, int mtu, TimeSpan now)
	{
		Connection = connection;
		ConnectedAt = now;
		LastReceived = now;
		for (var i = 0; i < Outboxes.Length; i++) Outboxes[i] = new Outbox(mtu);
	}

	public readonly int Connection;

	public NetworkPeer Peer = NetworkPeer.None;

	public PeerStage Stage;

	public readonly byte[] Nonce = new byte[Protocol.NonceSize];

	public TimeSpan ConnectedAt;

	public TimeSpan LastReceived;

	public TimeSpan LastPing;

	public TimeSpan CloseAt;

	public TimeSpan Rtt;

	/// <summary>Server: the newest snapshot tick the client completed.</summary>
	public uint AckTick;

	public int Violations;

	public double MessageTokens;

	public double ByteTokens;

	public TimeSpan TokensAt;

	public DisconnectReason Reason;

	/// <summary>One outbox per <see cref="Delivery"/>.</summary>
	public readonly Outbox[] Outboxes = new Outbox[4];

	/// <summary>Interest management: the slots the peer received per tick (ring by tick), and the ticks they belong to.</summary>
	public ulong[][]? Relevance;

	public uint[]? RelevanceTicks;
}
