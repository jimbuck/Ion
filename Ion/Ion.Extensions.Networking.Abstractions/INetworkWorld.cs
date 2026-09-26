using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>The state of a network session.</summary>
public enum NetworkState
{
	/// <summary>Not started, or <see cref="NetworkMode.Offline"/>.</summary>
	Offline = 0,

	/// <summary>A client waiting for the transport connection or the handshake.</summary>
	Connecting = 1,

	/// <summary>A client whose handshake was accepted, or a running server.</summary>
	Connected = 2,

	/// <summary>A client refused by the server or disconnected (see <see cref="INetworkSession.DisconnectReason"/>).</summary>
	Disconnected = 3,
}

/// <summary>Counters of a network session, since it started.</summary>
public struct NetworkStats
{
	/// <summary>Bytes received.</summary>
	public long BytesIn;

	/// <summary>Bytes sent.</summary>
	public long BytesOut;

	/// <summary>Packets received.</summary>
	public long PacketsIn;

	/// <summary>Packets sent.</summary>
	public long PacketsOut;

	/// <summary>Messages received and delivered.</summary>
	public long MessagesIn;

	/// <summary>Messages sent.</summary>
	public long MessagesOut;

	/// <summary>Inbound messages or updates dropped as unauthorized (wrong direction, not the owner, not owner-authority).</summary>
	public long Rejected;

	/// <summary>Inbound packets dropped because they could not be decoded.</summary>
	public long Malformed;

	/// <summary>Inbound packets dropped by the per-peer rate limits.</summary>
	public long RateLimited;

	/// <summary>Handshakes refused.</summary>
	public long HandshakesRejected;

	/// <summary>Snapshots sent (server) or completed (client).</summary>
	public long Snapshots;

	/// <summary>The size in bytes of the last snapshot sent or completed (all parts).</summary>
	public int LastSnapshotBytes;

	/// <summary>Snapshots sent as full snapshots because the peer's acknowledged tick was not usable as a baseline.</summary>
	public long FullSnapshots;

	/// <summary>Snapshot ticks a client never completed (lost or superseded).</summary>
	public long SnapshotsLost;

	/// <summary>Lag compensation rewinds done, and refused (beyond <see cref="NetworkConfig.MaxRewindTicks"/> or implausible for the peer's round trip).</summary>
	public long Rewinds, RewindsRejected;

	/// <summary>Prediction corrections (a predicted value differed from the server's) and client tick resynchronizations.</summary>
	public long Corrections, Resyncs;
}

/// <summary>
/// The connection side of the networking module: the role, the peers, their round trip times and the counters.
/// </summary>
public interface INetworkSession
{
	/// <summary>The configured role.</summary>
	NetworkMode Mode { get; }

	/// <summary>Whether this process is the server (<see cref="NetworkMode.Server"/> or <see cref="NetworkMode.ListenServer"/>).</summary>
	bool IsServer { get; }

	/// <summary>Whether this process is a client.</summary>
	bool IsClient { get; }

	/// <summary>The session state.</summary>
	NetworkState State { get; }

	/// <summary>This process's peer: <see cref="NetworkPeer.Server"/> on a server, the id the server assigned on a connected client, else <see cref="NetworkPeer.None"/>.</summary>
	NetworkPeer LocalPeer { get; }

	/// <summary>Why the client was disconnected or refused (<see cref="DisconnectReason.None"/> while connected).</summary>
	DisconnectReason DisconnectReason { get; }

	/// <summary>The connected peers: on a server its clients, on a client the server.</summary>
	IReadOnlyList<NetworkPeer> Peers { get; }

	/// <summary>The last measured round trip time to <paramref name="peer"/> (zero before the first measurement).</summary>
	TimeSpan RoundTripTime(NetworkPeer peer);

	/// <summary>The counters.</summary>
	ref readonly NetworkStats Stats { get; }

	/// <summary>Disconnects <paramref name="peer"/> (server) or leaves the server (client).</summary>
	void Disconnect(NetworkPeer peer, DisconnectReason reason = DisconnectReason.Kicked);
}

/// <summary>
/// The replication side of the networking module: ticks, network ids, the snapshot ring and lag compensation.
/// </summary>
/// <remarks>
/// The server captures every replicated component of every networked entity at the end of each fixed step (a
/// <c>FixedUpdate</c> <c>End</c> scope at <see cref="StageOrder.Network"/>) into a ring of
/// <see cref="NetworkConfig.SnapshotHistory"/> ticks, indexed by the entities' dense slots (<see cref="NetworkId.Slot"/>).
/// A client keeps the snapshots it received in the same shape, indexed by server tick.
/// </remarks>
public interface INetworkWorld
{
	/// <summary>The current tick (incremented at the start of every fixed step; on a client, ahead of the server by the input lead).</summary>
	uint CurrentTick { get; }

	/// <summary>The oldest tick still in the ring.</summary>
	uint OldestTick { get; }

	/// <summary>The newest tick <paramref name="peer"/> acknowledged (server), 0 if none.</summary>
	uint LastAckedTick(NetworkPeer peer);

	/// <summary>On a client, the server tick of the newest complete snapshot received.</summary>
	uint LastReceivedServerTick { get; }

	/// <summary>On a client, the server tick (fractional) remote entities are drawn at: the newest snapshot minus the interpolation delay.</summary>
	double InterpolationTick { get; }

	/// <summary>The world replicated.</summary>
	World World { get; }

	/// <summary>The peer this process is (see <see cref="INetworkSession.LocalPeer"/>).</summary>
	NetworkPeer LocalPeer { get; }

	/// <summary>Whether the local peer owns the entity with <paramref name="id"/>.</summary>
	bool IsOwned(in NetworkId id);

	/// <summary>
	/// On the server, allocates a network id owned by <paramref name="owner"/>; add it to an entity (directly or with
	/// <c>Commands</c>) to network it with that owner. Entities with replicated components get a server-owned one
	/// automatically.
	/// </summary>
	NetworkId Allocate(NetworkPeer owner);

	/// <summary>The local entity with the network id <paramref name="id"/>, if it exists.</summary>
	bool TryGetEntity(NetworkId id, out Entity entity);

	/// <summary>The value of <typeparamref name="T"/> on <paramref name="entity"/> at <paramref name="tick"/>, if the ring has it.</summary>
	bool TryGetAtTick<T>(Entity entity, uint tick, out T value) where T : unmanaged;

	/// <summary>
	/// The value of <typeparamref name="T"/> on <paramref name="entity"/> at <paramref name="tick"/> (a lag compensation
	/// read: two array indexings).
	/// </summary>
	/// <exception cref="InvalidOperationException">The ring does not have it.</exception>
	ref readonly T GetAtTick<T>(Entity entity, uint tick) where T : unmanaged;

	/// <summary>
	/// On the server, whether a client's claim that it acted at <paramref name="claimedTick"/> (a tick it saw, typically
	/// its <see cref="InterpolationTick"/>) is plausible: no older than <see cref="NetworkConfig.MaxRewindTicks"/> and no
	/// older than the peer's measured round trip plus the interpolation delay allow. Returns the tick to rewind to.
	/// </summary>
	bool TryGetRewindTick(NetworkPeer peer, uint claimedTick, out uint tick);

	/// <summary>
	/// Rewinds every replicated component of every networked entity to its value at <paramref name="tick"/> (clamped to
	/// <see cref="NetworkConfig.MaxRewindTicks"/> back), runs <paramref name="action"/> (for example physics queries),
	/// then restores the current values. Entities that did not exist then keep their current values.
	/// </summary>
	void WithWorldAtTick(uint tick, Action<World> action);

	/// <summary>Decides which entities each peer receives (null: every entity to every peer). Set before clients connect.</summary>
	IInterestPolicy? InterestPolicy { get; set; }
}

/// <summary>
/// Decides which networked entities a peer receives (interest management). An entity that stops being relevant is
/// despawned on that peer and spawned again when it becomes relevant.
/// </summary>
public interface IInterestPolicy
{
	/// <summary>Called once per peer per snapshot, before <see cref="IsRelevant"/>.</summary>
	void BeginPeer(NetworkPeer peer, World world);

	/// <summary>Whether <paramref name="peer"/> receives <paramref name="entity"/> in this snapshot.</summary>
	bool IsRelevant(NetworkPeer peer, Entity entity, in NetworkId id);
}

/// <summary>A prediction step: applies one tick of <paramref name="input"/> to <paramref name="component"/>. Must be deterministic and depend only on its arguments.</summary>
public delegate void PredictionStep<TComponent, TInput>(ref TComponent component, in TInput input, float delta)
	where TComponent : unmanaged where TInput : unmanaged;

/// <summary>
/// Client-side prediction with server reconciliation, for <see cref="PredictedAttribute"/> components on the entities a
/// client owns.
/// </summary>
/// <remarks>
/// <para>
/// Register a step for a (component, input) pair on both sides (the same game code). In every fixed step
/// (<see cref="StageOrder.Network"/> + 10): a client samples its input, keeps it with the tick, sends it (with the two
/// previous inputs, against loss) and applies the step to the component of each entity it owns; the server applies each
/// client's input for that tick (or its latest one when the input is late) to that client's entities. The same step on
/// both sides is what makes the prediction right.
/// </para>
/// <para>
/// In <c>First</c> (<see cref="StageOrder.Network"/> + 10), after a new snapshot arrived, a client compares the server's
/// value for its entities at the snapshot's tick with the value it predicted for that tick; when they differ it takes the
/// server's value and replays its stored inputs up to the current tick (<see cref="NetworkStats.Corrections"/>). Only the
/// registered steps are replayed, not the whole fixed schedule.
/// </para>
/// </remarks>
public interface INetworkPrediction
{
	/// <summary>
	/// Registers <paramref name="step"/> for <typeparamref name="TComponent"/> driven by <typeparamref name="TInput"/> (a
	/// <see cref="NetworkMessageAttribute"/> message with <see cref="MessageDirection.ClientToServer"/>).
	/// <paramref name="sample"/> gives the local input of the current tick on a client (and on a listen server).
	/// </summary>
	[SendsNetworkMessage(1)]
	[ReadsNetworkMessage(1)]
	void Register<TComponent, TInput>(PredictionStep<TComponent, TInput> step, Func<TInput>? sample = null)
		where TComponent : unmanaged where TInput : unmanaged;

	/// <summary>
	/// The input applied for <paramref name="peer"/> in the current tick (server), or the local input of the current tick
	/// (client). False before any input was received.
	/// </summary>
	bool TryGetInput<TInput>(NetworkPeer peer, out TInput input) where TInput : unmanaged;
}
