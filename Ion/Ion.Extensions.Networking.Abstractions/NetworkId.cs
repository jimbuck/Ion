using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// A peer of a network session: the server (<see cref="Server"/>, id 0) or a client (ids 1 to 254, assigned by the server
/// when it accepts the client's handshake). <see cref="None"/> (255) is no peer.
/// </summary>
/// <param name="Id">The peer id.</param>
public readonly record struct NetworkPeer(byte Id)
{
	/// <summary>The server.</summary>
	public static NetworkPeer Server => new(0);

	/// <summary>No peer (a client before its handshake is accepted, or an entity without an owner).</summary>
	public static NetworkPeer None => new(byte.MaxValue);

	/// <summary>The largest client id.</summary>
	public const byte MaxClientId = 254;

	/// <summary>Whether this is the server.</summary>
	public bool IsServer => Id == 0;

	/// <summary>Whether this is a client.</summary>
	public bool IsClient => Id is > 0 and < byte.MaxValue;

	/// <inheritdoc/>
	public override string ToString() => Id switch
	{
		0 => "server",
		byte.MaxValue => "none",
		_ => $"peer {Id}",
	};
}

/// <summary>
/// The network identity of a replicated entity, the same on the server and on every client. The server adds it to every
/// entity with a replicated component (or the game adds one from <see cref="INetworkWorld.Allocate"/>, to give the entity
/// to a client). <see cref="Id"/> packs a dense slot (the low 20 bits, used to index the snapshot ring) and a generation
/// (the high 12 bits) so that a reused slot is a different id.
/// </summary>
/// <param name="Id">The id; 0 is no id.</param>
/// <param name="OwnerPeer">The id of the owning peer (<see cref="NetworkPeer.Id"/>; 0 is the server).</param>
public readonly record struct NetworkId(uint Id, byte OwnerPeer)
{
	/// <summary>The number of bits of <see cref="Id"/> that hold the slot.</summary>
	public const int SlotBits = 20;

	/// <summary>The largest number of networked entities alive at once.</summary>
	public const int MaxSlots = 1 << SlotBits;

	/// <summary>The dense slot of the entity (index into the snapshot ring).</summary>
	public int Slot => (int)(Id & (MaxSlots - 1));

	/// <summary>The generation of the slot.</summary>
	public uint Generation => Id >> SlotBits;

	/// <summary>The owner.</summary>
	public NetworkPeer Owner => new(OwnerPeer);

	/// <summary>Whether this is a valid id.</summary>
	public bool IsValid => Id != 0;

	/// <summary>Makes an id from a slot and a generation (1 or more).</summary>
	public static uint Make(int slot, uint generation) => (generation << SlotBits) | (uint)slot;

	/// <inheritdoc/>
	public override string ToString() => $"net#{Slot}.{Generation} ({Owner})";
}

/// <summary>
/// A tag component: the entity is local to this process and never gets a <see cref="NetworkId"/>, even when it has
/// replicated components (for example a wall that both sides create themselves).
/// </summary>
public record struct NetworkLocal;

/// <summary>A client connected and its handshake was accepted (emitted on the server's <see cref="IEvents"/>; on a client, for itself).</summary>
/// <param name="Peer">The peer.</param>
public readonly record struct PeerConnected(NetworkPeer Peer);

/// <summary>A peer disconnected (emitted on <see cref="IEvents"/>; on a client, <see cref="NetworkPeer.Server"/> when it lost the server).</summary>
/// <param name="Peer">The peer.</param>
/// <param name="Reason">Why.</param>
public readonly record struct PeerDisconnected(NetworkPeer Peer, DisconnectReason Reason);

/// <summary>A client created the entity of a replicated spawn (emitted on the client's <see cref="IEvents"/>).</summary>
/// <param name="Entity">The local entity.</param>
/// <param name="Id">Its network id.</param>
public readonly record struct NetworkEntitySpawned(Entity Entity, NetworkId Id);

/// <summary>A client destroyed the entity of a replicated despawn (emitted on the client's <see cref="IEvents"/>).</summary>
/// <param name="Id">The network id of the destroyed entity.</param>
public readonly record struct NetworkEntityDespawned(NetworkId Id);

/// <summary>Why a connection ended or was refused. Sent to the peer in the disconnect or reject packet.</summary>
public enum DisconnectReason : byte
{
	/// <summary>No reason given (the transport lost the connection).</summary>
	None = 0,

	/// <summary>The session was stopped (the game exited).</summary>
	Shutdown = 1,

	/// <summary>The peer's protocol version differs.</summary>
	ProtocolMismatch = 2,

	/// <summary>The peer runs a different game (<see cref="NetworkConfig.GameId"/>).</summary>
	GameMismatch = 3,

	/// <summary>The peer's replicated types or messages differ (the registry hash).</summary>
	RegistryMismatch = 4,

	/// <summary>The peer's tick rate differs.</summary>
	TickRateMismatch = 5,

	/// <summary>The join secret is missing or wrong.</summary>
	WrongSecret = 6,

	/// <summary>The server has <see cref="NetworkConfig.MaxPeers"/> peers.</summary>
	ServerFull = 7,

	/// <summary>Nothing was received for <see cref="NetworkConfig.IdleTimeout"/>, or the handshake did not complete in time.</summary>
	Timeout = 8,

	/// <summary>Too many protocol violations (unauthorized messages or mutations, malformed packets, rate limits).</summary>
	Violations = 9,

	/// <summary>The server or the game disconnected the peer.</summary>
	Kicked = 10,
}
