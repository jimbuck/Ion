namespace Ion.Extensions.Networking;

/// <summary>The first byte of every packet.</summary>
internal enum PacketKind : byte
{
	/// <summary>Server to a new connection: protocol version and a random nonce.</summary>
	Challenge = 1,

	/// <summary>Client: protocol, game and registry hashes, tick rate, the join proof.</summary>
	Handshake = 2,

	/// <summary>Server: the client's peer id, the server tick and the tick rate.</summary>
	Accept = 3,

	/// <summary>Server: the handshake was refused (a <see cref="DisconnectReason"/>).</summary>
	Reject = 4,

	/// <summary>Either side: typed messages.</summary>
	Messages = 5,

	/// <summary>Server: one part of a (delta) snapshot.</summary>
	Snapshot = 6,

	/// <summary>Client: owner-authority component values of the entities it owns.</summary>
	OwnerUpdate = 7,

	/// <summary>Either side: a round trip measurement.</summary>
	Ping = 8,

	/// <summary>Either side: the answer to a ping.</summary>
	Pong = 9,

	/// <summary>Either side: the connection is closing (a <see cref="DisconnectReason"/>).</summary>
	Disconnect = 10,
}

/// <summary>Packet layout constants.</summary>
internal static class Protocol
{
	/// <summary>Kind (1), the sender's tick (4) and, from a client, its newest complete snapshot tick (4).</summary>
	public const int HeaderSize = 9;

	/// <summary>Snapshot part header after the packet header: baseline tick (4), part index (2), flags (1).</summary>
	public const int SnapshotHeaderSize = 7;

	/// <summary>The offset of the part flags in a snapshot packet.</summary>
	public const int PartFlagsOffset = HeaderSize + 6;

	/// <summary>The part flag of the last part of a snapshot.</summary>
	public const byte LastPart = 1;

	/// <summary>
	/// The part flag of a snapshot the server could not send whole (more than <see cref="MaxParts"/> parts): the client
	/// drops it, and the server keeps its baseline so the next snapshot is not a delta against it.
	/// </summary>
	public const byte Truncated = 2;

	/// <summary>The most parts one snapshot may have (about 4.9 MB at the default MTU).</summary>
	public const int MaxParts = 4096;

	/// <summary>The largest packet the module handles (the receive buffer).</summary>
	public const int MaxPacketSize = 64 * 1024;

	/// <summary>Record flags of a snapshot entity record.</summary>
	public const byte RecordSpawn = 1, RecordDespawn = 2;

	/// <summary>Component operations in a snapshot entity record.</summary>
	public const byte OpFull = 0, OpDelta = 1, OpRemove = 2;

	/// <summary>The size of the join proof (HMAC-SHA256) and of the challenge nonce.</summary>
	public const int ProofSize = 32, NonceSize = 16;

	public static void WriteHeader(ref NetWriter writer, PacketKind kind, uint tick, uint ack)
	{
		writer.WriteByte((byte)kind);
		writer.WriteUInt32(tick);
		writer.WriteUInt32(ack);
	}
}
