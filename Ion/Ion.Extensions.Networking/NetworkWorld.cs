using System.Runtime.CompilerServices;

using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// The replication side of a <see cref="NetworkSession"/>: network ids and their dense slots, the snapshot ring
/// (captured on the server, received on a client), delta encoding against each peer's acknowledged tick, applying
/// received snapshots to the world, owner-authority updates, interpolation and lag compensation.
/// </summary>
public sealed class NetworkWorld : INetworkWorld
{
	private static readonly QueryDescription Identified = new QueryDescription().WithAll<NetworkId>();

	private readonly NetworkSession _session;
	private readonly World _world;
	private readonly NetworkConfig _config;
	private readonly NetworkTypeTable _table;
	private readonly SnapshotRing _ring;
	private readonly int _history;
	private readonly bool _hasOwnerUpdates;

	// Server: the id allocated to each slot (0: free), the generations, when each was allocated, the free slots, the
	// entity found with each id at the last capture, and which slots were seen at the last capture.
	private uint[] _slotIds = new uint[64];
	private uint[] _generations = new uint[64];
	private uint[] _allocatedAt = new uint[64];
	private readonly Stack<int> _free = new();
	private int _highWater;
	private Entity[] _slotEntities = new Entity[64];
	private ulong[] _seen = new ulong[1];

	// The slots that changed between two frames (encoding against a baseline; applying after the previous snapshot).
	private ulong[] _changed = new ulong[16];
	private readonly List<Entity> _unidentified = [];
	private uint _lastSentTick;
	private uint _capturedTick;

	// Snapshot encoding buffers.
	private readonly byte[] _part;
	private readonly byte[] _record = new byte[Protocol.MaxPacketSize];

	// Client: the local entity created for each slot and the id it was created for, the newest complete and applied ticks.
	private Entity[] _clientEntities = new Entity[64];
	private uint[] _clientIds = new uint[64];
	private uint _latestComplete;
	private uint _applied;
	private TimeSpan _latestArrival;
	private double _syncError;
	private double _interpolationTick;
	private TimeSpan _lastInterpolation;

	// Lag compensation scratch.
	private SnapshotFrame? _rewindScratch;
	private readonly List<int> _rewound = [];

	internal NetworkWorld(NetworkSession session, World world, NetworkConfig config, NetworkTypeTable table)
	{
		_session = session;
		_world = world;
		_config = config;
		_table = table;
		_history = Math.Max(2, config.SnapshotHistory);
		_ring = new SnapshotRing(table, _history, 64);
		_part = new byte[Protocol.MaxPacketSize];
		for (var i = 0; i < table.Components.Count; i++)
		{
			if (table.Components[i].Authority == Authority.Owner && !table.Components[i].Predicted) _hasOwnerUpdates = true;
		}

		Ion.Extensions.Ecs.EcsComponents.Register<NetworkId>();
		Ion.Extensions.Ecs.EcsComponents.Register<NetworkLocal>();
	}

	/// <inheritdoc/>
	public uint CurrentTick { get; internal set; }

	/// <inheritdoc/>
	public uint OldestTick => CurrentTick >= (uint)_history ? CurrentTick - (uint)_history + 1 : 1;

	/// <inheritdoc/>
	public uint LastReceivedServerTick => _latestComplete;

	/// <inheritdoc/>
	public double InterpolationTick => _interpolationTick;

	/// <inheritdoc/>
	public World World => _world;

	/// <inheritdoc/>
	public NetworkPeer LocalPeer => _session.LocalPeer;

	/// <inheritdoc/>
	public IInterestPolicy? InterestPolicy { get; set; }

	/// <summary>The type table.</summary>
	public NetworkTypeTable Table => _table;

	/// <summary>The newest tick captured into the ring (server).</summary>
	public uint CapturedTick => _capturedTick;

	/// <summary>The newest snapshot tick applied to the world (client).</summary>
	public uint AppliedTick => _applied;

	/// <summary>The ack a client puts in its packet headers: the newest complete snapshot tick.</summary>
	internal uint AckTick => _session.IsClient ? _latestComplete : 0;

	/// <summary>Whether a client completed a snapshot it has not acknowledged yet.</summary>
	internal bool AckPending { get; set; }

	internal SnapshotRing Ring => _ring;

	/// <inheritdoc/>
	public uint LastAckedTick(NetworkPeer peer) => _session.StateOf(peer)?.AckTick ?? 0;

	/// <inheritdoc/>
	public bool IsOwned(in NetworkId id) => _session.LocalPeer.Id == id.OwnerPeer && _session.LocalPeer != NetworkPeer.None;

	/// <summary>
	/// The number of networked entities in the snapshot of <paramref name="tick"/> (captured on a server, received on a
	/// client), or -1 when the ring does not have it. With <see cref="INetworkWorld.TryGetAtTick{T}"/> this lets a test
	/// compare a client's state with the server's at the same tick.
	/// </summary>
	public int CountAtTick(uint tick)
	{
		var frame = _ring.Get(tick);
		if (frame is null) return -1;
		var count = 0;
		for (var slot = 0; slot < frame.SlotCount; slot++)
		{
			if (frame.IsAlive(slot)) count++;
		}

		return count;
	}

	/// <summary>The frame of <paramref name="tick"/> in the ring, if complete (captured on a server, received on a client).</summary>
	internal SnapshotFrame? Frame(uint tick) => _ring.Get(tick);

	// Ids --------------------------------------------------------------------------------------------------------------

	/// <inheritdoc/>
	public NetworkId Allocate(NetworkPeer owner)
	{
		if (!_session.IsServer) throw new InvalidOperationException("Only the server allocates network ids.");
		int slot;
		if (_free.Count > 0) slot = _free.Pop();
		else
		{
			slot = _highWater++;
			if (slot >= NetworkId.MaxSlots) throw new InvalidOperationException($"More than {NetworkId.MaxSlots} networked entities.");
			EnsureSlots(slot + 1);
		}

		var generation = (_generations[slot] + 1) & ((1u << (32 - NetworkId.SlotBits)) - 1);
		if (generation == 0) generation = 1;
		_generations[slot] = generation;
		var id = NetworkId.Make(slot, generation);
		_slotIds[slot] = id;
		_allocatedAt[slot] = CurrentTick;
		_slotEntities[slot] = Entity.Null;
		return new NetworkId(id, owner.Id);
	}

	private void EnsureSlots(int count)
	{
		if (count <= _slotIds.Length) return;
		var size = _slotIds.Length;
		while (size < count) size *= 2;
		Array.Resize(ref _slotIds, size);
		Array.Resize(ref _generations, size);
		Array.Resize(ref _allocatedAt, size);
		Array.Resize(ref _slotEntities, size);
	}

	/// <inheritdoc/>
	public bool TryGetEntity(NetworkId id, out Entity entity)
	{
		var slot = id.Slot;
		if (_session.IsServer)
		{
			if (slot < _highWater && _slotIds[slot] == id.Id && _slotEntities[slot] != Entity.Null && _world.IsAlive(_slotEntities[slot]))
			{
				entity = _slotEntities[slot];
				return true;
			}
		}
		else if (slot < _clientIds.Length && _clientIds[slot] == id.Id && id.Id != 0)
		{
			entity = _clientEntities[slot];
			return _world.IsAlive(entity);
		}

		entity = Entity.Null;
		return false;
	}

	// Ticks ------------------------------------------------------------------------------------------------------------------

	internal void BeginTick()
	{
		if (!_session.IsActive) return;
		CurrentTick++;
	}

	/// <summary>Server: gives ids to new entities, then captures every replicated component of this tick into the ring.</summary>
	internal void Capture()
	{
		if (!_session.IsServer || !_session.IsActive || CurrentTick == 0) return;

		// Entities with a replicated component and no id get a server-owned one.
		_unidentified.Clear();
		var frame = _ring.Slot(CurrentTick);
		foreach (var column in frame.Columns) column.CollectUnidentified(_world, _unidentified);
		foreach (var entity in _unidentified)
		{
			if (_world.IsAlive(entity) && !_world.Has<NetworkId>(entity)) _world.Add(entity, Allocate(NetworkPeer.Server));
		}

		frame.Reset(CurrentTick);
		frame.EnsureCapacity(_highWater);
		frame.SlotCount = _highWater;
		if (_seen.Length * 64 < _highWater) _seen = new ulong[(_highWater + 63) / 64 * 2];
		Array.Clear(_seen);

		var ids = frame.Ids;
		var owners = frame.Owners;
		foreach (ref var chunk in _world.Query(in Identified))
		{
			var networkIds = chunk.GetSpan<NetworkId>();
			ref var entities = ref chunk.Entity(0);
			var count = chunk.Count;
			for (var i = 0; i < count; i++)
			{
				var id = networkIds[i];
				var slot = id.Slot;
				if (slot >= _highWater || _slotIds[slot] != id.Id) continue;
				ids[slot] = id.Id;
				owners[slot] = id.OwnerPeer;
				_slotEntities[slot] = Unsafe.Add(ref entities, i);
				_seen[slot >> 6] |= 1UL << slot;
			}
		}

		foreach (var column in frame.Columns) column.Capture(_world, ids);

		frame.Complete = true;
		_capturedTick = CurrentTick;

		// Slots allocated a while ago whose entity is gone are freed (a new entity there gets a new generation).
		for (var slot = 0; slot < _highWater; slot++)
		{
			if (_slotIds[slot] == 0 || (_seen[slot >> 6] & (1UL << slot)) != 0) continue;
			if (CurrentTick - _allocatedAt[slot] < 2) continue;
			_slotIds[slot] = 0;
			_slotEntities[slot] = Entity.Null;
			_free.Push(slot);
		}
	}

	// Server: snapshots out ---------------------------------------------------------------------------------------------------

	internal void OnPeerConnected(PeerState state)
	{
		state.AckTick = 0;
		if (InterestPolicy is not null && state.Relevance is null)
		{
			state.Relevance = new ulong[_history][];
			state.RelevanceTicks = new uint[_history];
			for (var i = 0; i < _history; i++) state.Relevance[i] = new ulong[1];
		}
	}

	internal void OnPeerDisconnected(PeerState state) => state.AckTick = 0;

	/// <summary>Sends the newest captured tick to every connected peer, as a delta against its acknowledged tick.</summary>
	internal void SendSnapshots()
	{
		var tick = _capturedTick;
		if (tick == 0 || tick == _lastSentTick) return;
		var interval = _config.SendRate > 0 ? Math.Max(1, (int)Math.Round(_session.TickRate / _config.SendRate)) : 1;
		if (_lastSentTick != 0 && tick - _lastSentTick < interval) return;
		_lastSentTick = tick;

		var current = _ring.Get(tick);
		if (current is null) return;

		foreach (var state in _session.States)
		{
			if (state.Stage != PeerStage.Connected) continue;
			var ack = state.AckTick;
			var baseline = ack != 0 && tick > ack && tick - ack < (uint)_history ? _ring.Get(ack) : null;
			if (baseline is null) _session.MutableStats.FullSnapshots++;
			Encode(state, current, baseline);
		}
	}

	private void Encode(PeerState state, SnapshotFrame current, SnapshotFrame? baseline)
	{
		// Interest: the slots this peer gets now, and the ones it had at the baseline.
		ulong[]? relevant = null;
		ulong[]? relevantBefore = null;
		if (InterestPolicy is { } policy && state.Relevance is not null && state.RelevanceTicks is not null)
		{
			var index = (int)(current.Tick % (uint)_history);
			ref var bits = ref state.Relevance[index];
			if (bits.Length * 64 < current.SlotCount) bits = new ulong[(current.SlotCount + 63) / 64];
			Array.Clear(bits);
			policy.BeginPeer(state.Peer, _world);
			for (var slot = 0; slot < current.SlotCount; slot++)
			{
				if (!current.IsAlive(slot)) continue;
				var entity = _slotEntities[slot];
				if (entity == Entity.Null || !_world.IsAlive(entity)) continue;
				if (policy.IsRelevant(state.Peer, entity, new NetworkId(current.Ids[slot], current.Owners[slot]))) bits[slot >> 6] |= 1UL << slot;
			}

			state.RelevanceTicks[index] = current.Tick;
			relevant = bits;
			if (baseline is not null)
			{
				var before = (int)(baseline.Tick % (uint)_history);
				relevantBefore = state.RelevanceTicks[before] == baseline.Tick ? state.Relevance[before] : null;
				if (relevantBefore is null) baseline = null;
			}
		}

		var columns = current.Columns;
		if (baseline is not null) current.Changed(baseline, ref _changed, Math.Max(current.SlotCount, baseline.SlotCount));
		var partIndex = 0;
		var total = 0;
		var truncated = false;
		var writer = BeginPart(current.Tick, baseline?.Tick ?? 0, partIndex);
		var count = Math.Max(current.SlotCount, baseline?.SlotCount ?? 0);

		for (var slot = 0; slot < count; slot++)
		{
			// Against a baseline, only slots whose entity, bytes or relevance changed can have a record.
			if (baseline is not null)
			{
				if ((slot & 63) == 0 && _changed[slot >> 6] == 0 && SameRelevance(relevant, relevantBefore, slot >> 6))
				{
					slot += 63;
					continue;
				}

				if (!SlotBits.Get(_changed, slot) && SameRelevance(relevant, relevantBefore, slot >> 6)) continue;
			}

			var alive = current.IsAlive(slot) && (relevant is null || SlotBits.Get(relevant, slot));
			var wasAlive = baseline is not null && baseline.IsAlive(slot) && (relevantBefore is null || SlotBits.Get(relevantBefore, slot));
			if (!alive && !wasAlive) continue;

			var record = new NetWriter(_record);
			record.WriteVarUInt32((uint)slot);
			if (!alive)
			{
				record.WriteByte(Protocol.RecordDespawn);
			}
			else if (!wasAlive || baseline!.Ids[slot] != current.Ids[slot])
			{
				record.WriteByte(Protocol.RecordSpawn);
				record.WriteUInt32(current.Ids[slot]);
				record.WriteByte(current.Owners[slot]);
				for (var c = 0; c < columns.Length; c++)
				{
					if (!columns[c].Has(slot)) continue;
					record.WriteVarUInt32((uint)c + 1);
					record.WriteByte(Protocol.OpFull);
					columns[c].WriteFull(slot, ref record);
				}

				record.WriteVarUInt32(0);
			}
			else
			{
				record.WriteByte(0);
				var header = record.Position;
				for (var c = 0; c < columns.Length; c++)
				{
					var now = columns[c].Has(slot);
					var before = baseline.Columns[c].Has(slot);
					if (!now && !before) continue;
					var mark = record.Position;
					record.WriteVarUInt32((uint)c + 1);
					if (!now)
					{
						record.WriteByte(Protocol.OpRemove);
					}
					else if (!before)
					{
						record.WriteByte(Protocol.OpFull);
						columns[c].WriteFull(slot, ref record);
					}
					else
					{
						record.WriteByte(Protocol.OpDelta);
						if (!columns[c].WriteDelta(baseline.Columns[c], slot, ref record)) record.Position = mark;
					}
				}

				// Nothing changed: no record at all.
				if (record.Position == header) continue;
				record.WriteVarUInt32(0);
			}

			if (record.Overflowed) continue;
			var bytes = record.Written;
			if (writer.Remaining < bytes.Length)
			{
				if (writer.Position == Protocol.HeaderSize + Protocol.SnapshotHeaderSize)
				{
					// A single entity larger than a packet: it cannot be sent.
					continue;
				}

				total += FinishPart(state, ref writer, last: false);
				partIndex++;
				if (partIndex >= Protocol.MaxParts - 1)
				{
					truncated = true;
					break;
				}

				writer = BeginPart(current.Tick, baseline?.Tick ?? 0, partIndex);
				if (writer.Remaining < bytes.Length) continue;
			}

			writer.WriteBytes(bytes);
		}

		if (truncated)
		{
			// Too large to send whole: the client drops it (a partial snapshot would be a wrong baseline).
			_part[Protocol.PartFlagsOffset] = Protocol.Truncated;
			_session.ReportTruncated(current.SlotCount);
		}

		total += FinishPart(state, ref writer, last: true);
		_session.MutableStats.Snapshots++;
		_session.MutableStats.LastSnapshotBytes = total;
	}

	private static bool SameRelevance(ulong[]? relevant, ulong[]? relevantBefore, int block)
	{
		if (relevant is null) return true;
		var now = block < relevant.Length ? relevant[block] : 0;
		var before = relevantBefore is not null && block < relevantBefore.Length ? relevantBefore[block] : 0;
		return now == before;
	}

	private NetWriter BeginPart(uint tick, uint baseline, int index)
	{
		var writer = new NetWriter(_part.AsSpan(0, _session.Mtu));
		Protocol.WriteHeader(ref writer, PacketKind.Snapshot, tick, 0);
		writer.WriteUInt32(baseline);
		writer.WriteUInt16((ushort)index);
		writer.WriteByte(0);
		return writer;
	}

	private int FinishPart(PeerState state, ref NetWriter writer, bool last)
	{
		if (last) _part[Protocol.PartFlagsOffset] |= Protocol.LastPart;
		var length = writer.Position;
		_session.SendRaw(state, new ReadOnlySpan<byte>(_part, 0, length), Delivery.Unreliable);
		return length;
	}

	// Client: snapshots in ------------------------------------------------------------------------------------------------------

	internal void OnAccepted(uint serverTick, TimeSpan rtt)
	{
		// The server is half a round trip past the tick it stamped the accept with, and an input takes another half to
		// reach it: a full round trip ahead, plus the lead.
		CurrentTick = serverTick + TicksFor(rtt.TotalSeconds) + (uint)Math.Max(0, _config.InputLeadTicks);
		_syncError = 0;
		_latestComplete = 0;
		_applied = 0;
		_interpolationTick = 0;
	}

	private uint TicksFor(double seconds) => (uint)Math.Max(0, Math.Ceiling(seconds * _session.TickRate));

	internal void OnSnapshotPart(uint tick, ref NetReader reader)
	{
		var baselineTick = reader.ReadUInt32();
		var index = reader.ReadUInt16();
		var flags = reader.ReadByte();
		if (reader.Failed || tick == 0 || index >= Protocol.MaxParts)
		{
			_session.MutableStats.Malformed++;
			return;
		}

		if ((flags & Protocol.Truncated) != 0)
		{
			// The server could not send this snapshot whole: forget what arrived of it.
			var partial = _ring.Slot(tick);
			if (partial.Tick == tick && !partial.Complete) partial.Tick = 0;
			_session.MutableStats.SnapshotsLost++;
			return;
		}

		// Older than what is already complete: useless.
		if (tick <= _latestComplete) return;

		var frame = _ring.Slot(tick);
		if (frame.Tick != tick || frame.Complete)
		{
			if (frame.Tick == tick) return;
			if (baselineTick == 0)
			{
				frame.Reset(tick);
			}
			else
			{
				var baseline = _ring.Get(baselineTick);
				if (baseline is null)
				{
					_session.MutableStats.SnapshotsLost++;
					return;
				}

				frame.CopyFrom(baseline, tick);
			}

			frame.Baseline = baselineTick;
		}
		else if (frame.Baseline != baselineTick)
		{
			return;
		}

		if (!frame.MarkPart(index)) return;
		frame.Bytes += reader.Remaining + Protocol.HeaderSize + Protocol.SnapshotHeaderSize;

		if (!Decode(frame, ref reader))
		{
			frame.Tick = 0;
			frame.Complete = false;
			_session.MutableStats.Malformed++;
			return;
		}

		if ((flags & Protocol.LastPart) != 0) frame.PartCount = index + 1;
		if (!frame.AllParts()) return;

		frame.Complete = true;
		if (_latestComplete != 0 && tick > _latestComplete + 1) _session.MutableStats.SnapshotsLost += tick - _latestComplete - 1;
		_latestComplete = tick;
		_latestArrival = _session.Clock.Elapsed;
		AckPending = true;
		_session.MutableStats.Snapshots++;
		_session.MutableStats.LastSnapshotBytes = frame.Bytes;
		SyncTick(tick);
	}

	private bool Decode(SnapshotFrame frame, ref NetReader reader)
	{
		var columns = frame.Columns;
		while (!reader.End)
		{
			var slotValue = reader.ReadVarUInt32();
			var flags = reader.ReadByte();
			if (reader.Failed || slotValue >= NetworkId.MaxSlots) return false;
			var slot = (int)slotValue;
			frame.EnsureCapacity(slot + 1);
			if (slot >= frame.SlotCount) frame.SlotCount = slot + 1;

			if ((flags & Protocol.RecordDespawn) != 0)
			{
				frame.Despawn(slot);
				continue;
			}

			if ((flags & Protocol.RecordSpawn) != 0)
			{
				var id = reader.ReadUInt32();
				var owner = reader.ReadByte();
				if (id == 0) return false;
				frame.Despawn(slot);
				frame.Ids[slot] = id;
				frame.Owners[slot] = owner;
			}
			else if (frame.Ids[slot] == 0)
			{
				return false;
			}

			while (true)
			{
				var ordinal = reader.ReadVarUInt32();
				if (reader.Failed) return false;
				if (ordinal == 0) break;
				if (ordinal > (uint)columns.Length) return false;
				var column = columns[ordinal - 1];
				switch (reader.ReadByte())
				{
					case Protocol.OpFull:
						column.ReadFull(slot, ref reader);
						break;
					case Protocol.OpDelta:
						if (!column.Has(slot)) return false;
						column.ReadDelta(slot, ref reader);
						break;
					case Protocol.OpRemove:
						column.Remove(slot);
						break;
					default:
						return false;
				}

				if (reader.Failed) return false;
			}
		}

		return !reader.Failed;
	}

	// The client runs ahead of the server's snapshot by a full round trip (the snapshot took half of it to arrive, and an
	// input takes the other half to reach the server) plus the input lead, so its inputs for tick N arrive before the
	// server simulates N. The error is smoothed; beyond 3 ticks the tick is reset (and predictions cleared).
	private void SyncTick(uint serverTick)
	{
		var rtt = _session.RoundTripTime(NetworkPeer.Server).TotalSeconds;
		var target = serverTick + rtt * _session.TickRate + Math.Max(0, _config.InputLeadTicks);
		var error = CurrentTick - target;
		_syncError = _syncError * 0.8 + error * 0.2;
		if (Math.Abs(_syncError) <= 3 && Math.Abs(error) <= 12) return;
		CurrentTick = (uint)Math.Max(1, Math.Round(target));
		_syncError = 0;
		_session.MutableStats.Resyncs++;
		_session.Prediction.OnResync();
	}

	/// <summary>Applies the newest complete snapshot to the world: spawns, despawns, component values.</summary>
	internal void ApplyLatest()
	{
		if (_latestComplete <= _applied) return;
		var frame = _ring.Get(_latestComplete);
		if (frame is null) return;
		var previous = _ring.Get(_applied);
		var local = _session.LocalPeer.Id;
		var count = Math.Max(frame.SlotCount, _clientIds.Length);
		if (previous is not null) frame.Changed(previous, ref _changed, count);
		var events = _session.Events;

		if (frame.SlotCount > _clientIds.Length)
		{
			var size = _clientIds.Length;
			while (size < frame.SlotCount) size *= 2;
			Array.Resize(ref _clientIds, size);
			Array.Resize(ref _clientEntities, size);
		}

		for (var slot = 0; slot < count; slot++)
		{
			// A slot with the same entity and bytes as in the snapshot applied last is already in the world.
			if (previous is not null)
			{
				if ((slot & 63) == 0 && _changed[slot >> 6] == 0 && SnapshotFrame.BlockEqual(_clientIds, frame.Ids, slot >> 6))
				{
					slot += 63;
					continue;
				}

				if (!SlotBits.Get(_changed, slot) && slot < _clientIds.Length && _clientIds[slot] == frame.Ids[slot]) continue;
			}

			var alive = frame.IsAlive(slot);
			var id = alive ? frame.Ids[slot] : 0;
			var existing = slot < _clientIds.Length ? _clientIds[slot] : 0;

			if (existing != 0 && existing != id)
			{
				var old = _clientEntities[slot];
				if (_world.IsAlive(old)) _world.Destroy(old);
				_clientIds[slot] = 0;
				_clientEntities[slot] = Entity.Null;
				events.Emit(new NetworkEntityDespawned(new NetworkId(existing, 0)));
			}

			if (!alive) continue;

			var owner = frame.Owners[slot];
			var spawned = false;
			if (_clientIds[slot] == 0 || !_world.IsAlive(_clientEntities[slot]))
			{
				_clientEntities[slot] = _world.Create(new NetworkId(id, owner));
				_clientIds[slot] = id;
				spawned = true;
			}

			var entity = _clientEntities[slot];
			var owned = owner == local;
			var same = !spawned && previous is not null && previous.IsAlive(slot) && previous.Ids[slot] == id;
			for (var c = 0; c < frame.Columns.Length; c++)
			{
				var column = frame.Columns[c];
				if (column.Has(slot))
				{
					if (!spawned)
					{
						if (owned && (column.Info.Predicted || column.Info.Authority == Authority.Owner)) continue;
						if (!owned && column.Info.Interpolated) continue;

						// Unchanged since the snapshot applied last: the world already has it (a value is written when it
						// changes, so a client-side edit of a server-authority component lasts until the server's changes).
						if (same && column.SameAs(previous!.Columns[c], slot)) continue;
					}

					column.ApplyTo(_world, entity, slot);
				}
				else if (!spawned && (previous is null || previous.Columns[c].Has(slot)))
				{
					column.RemoveFrom(_world, entity);
				}
			}

			if (spawned) events.Emit(new NetworkEntitySpawned(entity, new NetworkId(id, owner)));
		}

		_applied = frame.Tick;
	}

	/// <summary>Client: writes the interpolated value of every interpolated component of the remote entities (Render).</summary>
	internal void Interpolate()
	{
		if (!_session.IsClient || !_session.IsActive || _latestComplete == 0) return;
		var now = _session.Clock.Elapsed;
		var rate = _session.TickRate;
		var delay = Math.Max(0, _config.InterpolationDelay);

		// Where the newest snapshot says the server is now, minus the delay.
		var target = _latestComplete + (now - _latestArrival).TotalSeconds * rate - delay;
		var elapsed = _lastInterpolation == TimeSpan.Zero ? 0 : (now - _lastInterpolation).TotalSeconds * rate;
		_lastInterpolation = now;

		if (_interpolationTick == 0 || Math.Abs(target - _interpolationTick) > 4) _interpolationTick = target;
		else
		{
			// Adaptive: advance at the tick rate, and close a tenth of the error each frame.
			_interpolationTick += elapsed;
			_interpolationTick += (target - _interpolationTick) * 0.1;
		}

		_interpolationTick = Math.Min(_interpolationTick, _latestComplete + Math.Max(0, _config.MaxExtrapolationTicks));
		if (_interpolationTick < 1) return;

		// The complete frames around the interpolation tick.
		var at = _interpolationTick;
		var floor = (uint)Math.Floor(at);
		SnapshotFrame? from = null;
		for (var back = 0u; back < 16 && floor > back && from is null; back++) from = _ring.Get(floor - back);
		if (from is null) return;

		SnapshotFrame? to = null;
		for (var tick = from.Tick + 1; tick <= _latestComplete && to is null; tick++) to = _ring.Get(tick);

		float t;
		if (to is not null)
		{
			t = (float)((at - from.Tick) / (to.Tick - from.Tick));
		}
		else
		{
			// Past the newest snapshot: extrapolate from the one before it, for at most MaxExtrapolationTicks, then hold.
			SnapshotFrame? before = null;
			for (var back = 1u; back < 16 && from.Tick > back && before is null; back++) before = _ring.Get(from.Tick - back);
			if (before is null)
			{
				to = from;
				t = 1;
			}
			else
			{
				to = from;
				from = before;
				t = (float)((at - from.Tick) / (to.Tick - from.Tick));
			}
		}

		var local = _session.LocalPeer.Id;
		var count = Math.Min(Math.Min(from.SlotCount, to.SlotCount), _clientIds.Length);
		for (var c = 0; c < to.Columns.Length; c++)
		{
			var b = to.Columns[c];
			if (!b.Info.Interpolated) continue;
			var a = from.Columns[c];
			for (var slot = 0; slot < count; slot++)
			{
				var id = to.Ids[slot];
				if (id == 0 || from.Ids[slot] != id || _clientIds[slot] != id || to.Owners[slot] == local) continue;
				if (!a.Has(slot) || !b.Has(slot)) continue;
				var entity = _clientEntities[slot];
				if (t >= 1f && to.Tick == _latestComplete && t <= 1.0001f) b.ApplyTo(_world, entity, slot);
				else if (t <= 0f) a.ApplyTo(_world, entity, slot);
				else b.InterpolateTo(_world, entity, a, b, slot, t);
			}
		}
	}

	// Owner authority ---------------------------------------------------------------------------------------------------------

	/// <summary>Client: sends the owner-authority components (not predicted ones) of the entities it owns.</summary>
	internal void SendOwnerUpdates(PeerState? server)
	{
		if (server is null || server.Stage != PeerStage.Connected) return;
		if (!_hasOwnerUpdates) return;
		var local = _session.LocalPeer.Id;

		var frame = _ring.Get(_applied);
		if (frame is null) return;
		var writer = BeginOwnerPacket();
		for (var slot = 0; slot < Math.Min(frame.SlotCount, _clientIds.Length); slot++)
		{
			if (_clientIds[slot] == 0 || frame.Owners[slot] != local || frame.Ids[slot] != _clientIds[slot]) continue;
			var entity = _clientEntities[slot];
			if (!_world.IsAlive(entity)) continue;
			for (var c = 0; c < frame.Columns.Length; c++)
			{
				var column = frame.Columns[c];
				if (column.Info.Authority != Authority.Owner || column.Info.Predicted) continue;
				var record = new NetWriter(_record);
				record.WriteVarUInt32((uint)slot);
				record.WriteUInt32(_clientIds[slot]);
				record.WriteVarUInt32((uint)c);
				if (!column.WriteEntity(_world, entity, ref record) || record.Overflowed) continue;
				if (writer.Remaining < record.Position)
				{
					_session.SendRaw(server, writer.Written, Delivery.Unreliable);
					writer = BeginOwnerPacket();
				}

				writer.WriteBytes(record.Written);
			}
		}

		if (writer.Position > Protocol.HeaderSize) _session.SendRaw(server, writer.Written, Delivery.Unreliable);
	}

	private NetWriter BeginOwnerPacket()
	{
		var writer = new NetWriter(_part.AsSpan(0, _session.Mtu));
		Protocol.WriteHeader(ref writer, PacketKind.OwnerUpdate, CurrentTick, AckTick);
		return writer;
	}

	/// <summary>Server: applies a client's owner-authority values after checking it owns each entity and each type is owner-authority.</summary>
	internal void OnOwnerUpdate(PeerState state, ref NetReader reader)
	{
		var columns = _ring.Slot(CurrentTick).Columns;
		while (!reader.End)
		{
			var slot = reader.ReadVarUInt32();
			var id = reader.ReadUInt32();
			var ordinal = reader.ReadVarUInt32();
			if (reader.Failed || ordinal >= (uint)columns.Length)
			{
				_session.Malformed(state);
				return;
			}

			var column = columns[ordinal];
			var authorized = column.Info.Authority == Authority.Owner && !column.Info.Predicted
				&& slot < (uint)_highWater && _slotIds[slot] == id
				&& _slotEntities[slot] is var entity && entity != Entity.Null && _world.IsAlive(entity)
				&& _world.TryGet<NetworkId>(entity, out var networkId) && networkId.Id == id && networkId.OwnerPeer == state.Peer.Id;
			if (!authorized)
			{
				// Not the owner, or not an owner-authority component: the rest of the packet is not trusted either.
				_session.Rejected(state);
				return;
			}

			column.ReadInto(_world, _slotEntities[slot], ref reader);
			if (reader.Failed)
			{
				_session.Malformed(state);
				return;
			}
		}
	}

	// Lag compensation ----------------------------------------------------------------------------------------------------------

	private ReplicatedColumn<T> ColumnOf<T>(SnapshotFrame frame) where T : unmanaged
	{
		var info = NetworkTypes<T>.Component;
		if (info is null || info.Ordinal < 0 || info.Ordinal >= frame.Columns.Length) throw new InvalidOperationException($"{typeof(T).Name} is not a replicated component.");
		return (ReplicatedColumn<T>)frame.Columns[info.Ordinal];
	}

	/// <inheritdoc/>
	public bool TryGetAtTick<T>(Entity entity, uint tick, out T value) where T : unmanaged
	{
		value = default;
		var frame = _ring.Get(tick);
		if (frame is null || !_world.IsAlive(entity) || !_world.TryGet<NetworkId>(entity, out var id)) return false;
		var slot = id.Slot;
		if (!frame.IsAlive(slot) || frame.Ids[slot] != id.Id) return false;
		var column = ColumnOf<T>(frame);
		if (!column.Has(slot)) return false;
		value = column.At(slot);
		return true;
	}

	/// <inheritdoc/>
	public ref readonly T GetAtTick<T>(Entity entity, uint tick) where T : unmanaged
	{
		var frame = _ring.Get(tick) ?? throw new InvalidOperationException($"Tick {tick} is not in the snapshot ring (oldest {OldestTick}, newest {CurrentTick}).");
		if (!_world.IsAlive(entity) || !_world.TryGet<NetworkId>(entity, out var id)) throw new InvalidOperationException($"{entity} is not a networked entity.");
		var slot = id.Slot;
		var column = ColumnOf<T>(frame);
		if (!frame.IsAlive(slot) || frame.Ids[slot] != id.Id || !column.Has(slot)) throw new InvalidOperationException($"{entity} had no {typeof(T).Name} at tick {tick}.");
		return ref column.At(slot);
	}

	/// <inheritdoc/>
	public bool TryGetRewindTick(NetworkPeer peer, uint claimedTick, out uint tick)
	{
		tick = 0;
		var current = CurrentTick;
		if (claimedTick == 0 || claimedTick > current)
		{
			_session.MutableStats.RewindsRejected++;
			return false;
		}

		var age = current - claimedTick;
		var rttTicks = _session.RoundTripTime(peer).TotalSeconds * _session.TickRate;
		var plausible = Math.Ceiling(rttTicks + Math.Max(0, _config.InterpolationDelay) + Math.Max(0, _config.InputLeadTicks) + 2);
		if (age > (uint)Math.Max(0, _config.MaxRewindTicks) || age > plausible || _ring.Get(claimedTick) is null)
		{
			_session.MutableStats.RewindsRejected++;
			return false;
		}

		tick = claimedTick;
		return true;
	}

	/// <inheritdoc/>
	public void WithWorldAtTick(uint tick, Action<World> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		var oldest = CurrentTick > (uint)Math.Max(0, _config.MaxRewindTicks) ? CurrentTick - (uint)Math.Max(0, _config.MaxRewindTicks) : 1;
		if (tick < oldest) tick = oldest;
		var frame = tick >= _capturedTick ? null : _ring.Get(tick);
		if (frame is null)
		{
			action(_world);
			return;
		}

		var scratch = _rewindScratch ??= new SnapshotFrame(_table, _highWater);
		scratch.EnsureCapacity(_highWater);
		_rewound.Clear();
		_session.MutableStats.Rewinds++;

		// Save the current values, then write the values of the tick.
		for (var slot = 0; slot < Math.Min(frame.SlotCount, _highWater); slot++)
		{
			if (!frame.IsAlive(slot) || frame.Ids[slot] != _slotIds[slot]) continue;
			var entity = _slotEntities[slot];
			if (entity == Entity.Null || !_world.IsAlive(entity)) continue;
			_rewound.Add(slot);
			for (var c = 0; c < frame.Columns.Length; c++)
			{
				scratch.Columns[c].CaptureEntity(_world, entity, slot);
				if (scratch.Columns[c].Has(slot) && frame.Columns[c].Has(slot)) frame.Columns[c].ApplyTo(_world, entity, slot);
			}
		}

		try
		{
			action(_world);
		}
		finally
		{
			foreach (var slot in _rewound)
			{
				var entity = _slotEntities[slot];
				for (var c = 0; c < scratch.Columns.Length; c++)
				{
					if (scratch.Columns[c].Has(slot) && frame.Columns[c].Has(slot)) scratch.Columns[c].ApplyTo(_world, entity, slot);
					scratch.Columns[c].Remove(slot);
				}
			}
		}
	}
}
