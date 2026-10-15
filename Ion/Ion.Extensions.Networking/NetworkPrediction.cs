using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// Client-side prediction with server reconciliation (<see cref="INetworkPrediction"/>). A client applies its sampled
/// input to the predicted components of the entities it owns in every fixed step, keeps the input and the predicted
/// value per tick, and when a snapshot arrives compares the server's value at the snapshot tick with its prediction for
/// that tick, correcting and replaying the stored inputs on a mismatch. The server applies each client's input for the
/// tick (or its latest one, when the input for the tick is late or lost) to that client's entities.
/// </summary>
/// <remarks>
/// A player has one input per tick, whatever worlds are live: the step is applied to the player's entities in every live
/// networked world, and each world is reconciled against its own snapshots.
/// </remarks>
public sealed class NetworkPrediction : INetworkPrediction
{
	private readonly NetworkSession _session;
	private readonly IEvents _events;
	private readonly List<Binding> _bindings = [];

	internal NetworkPrediction(NetworkSession session, IEvents events)
	{
		_session = session;
		_events = events;
	}

	/// <summary>The number of registered prediction steps.</summary>
	public int Count => _bindings.Count;

	/// <inheritdoc/>
	public bool IsResimulating { get; private set; }

	/// <inheritdoc/>
	public uint ResimulatedTick { get; private set; }

	/// <summary>
	/// Runs the whole fixed schedule once for a tick being resimulated (<see cref="RollbackMode.FixedSchedule"/>): the game
	/// loop's <c>ResimulateFixedStep</c>, wired by the module. Without it a client falls back to replaying the prediction steps.
	/// </summary>
	internal Action? Resimulator { get; set; }

	/// <summary>The number of ticks resimulated so far (with <see cref="RollbackMode.FixedSchedule"/>).</summary>
	internal long ResimulatedTicks { get; private set; }

	/// <inheritdoc/>
	[SendsNetworkMessage(1)]
	[ReadsNetworkMessage(1)]
	public void Register<TComponent, TInput>(PredictionStep<TComponent, TInput> step, Func<TInput>? sample = null)
		where TComponent : unmanaged where TInput : unmanaged
	{
		ArgumentNullException.ThrowIfNull(step);
		var component = NetworkTypes<TComponent>.Component
			?? throw new InvalidOperationException($"{typeof(TComponent).Name} is not a registered replicated component: mark it [Replicated] (the networking generator registers it).");
		var input = NetworkTypes<TInput>.Message
			?? throw new InvalidOperationException($"{typeof(TInput).Name} is not a registered network message: mark it [NetworkMessage(Direction = MessageDirection.ClientToServer)].");
		if (!input.ClientMaySend) throw new InvalidOperationException($"{input.Name} is the input of a prediction step, so clients must be allowed to send it (Direction = ClientToServer).");
		foreach (var existing in _bindings)
		{
			if (existing.ComponentType == typeof(TComponent)) throw new InvalidOperationException($"{typeof(TComponent).Name} already has a prediction step.");
		}

		_bindings.Add(new Binding<TComponent, TInput>(this, component, input, step, sample));
	}

	/// <inheritdoc/>
	public bool TryGetInput<TInput>(NetworkPeer peer, out TInput input) where TInput : unmanaged
	{
		foreach (var binding in _bindings)
		{
			if (binding is IInputSource<TInput> source && source.TryGetInput(peer, out input)) return true;
		}

		input = default;
		return false;
	}

	/// <summary>FixedUpdate at <see cref="StageOrder.Network"/> + 10: samples, sends and applies inputs (client), or applies the received ones (server).</summary>
	internal void FixedStep(float delta)
	{
		if (!_session.IsActive || _bindings.Count == 0) return;
		var tick = _session.Tick;
		var worlds = _session.Worlds.LiveWorlds;
		if (IsResimulating)
		{
			// The stored input of the tick, applied again (nothing is sampled or sent).
			foreach (var binding in _bindings)
			{
				foreach (var world in worlds) binding.ClientReplay(world, ResimulatedTick, delta);
			}

			return;
		}

		foreach (var binding in _bindings)
		{
			if (_session.IsServer)
			{
				binding.ServerReceive(tick);
				foreach (var world in worlds) binding.ServerApply(world, tick, delta);
			}
			else if (binding.ClientSample(tick))
			{
				foreach (var world in worlds) binding.ClientApply(world, tick, delta);
			}
		}
	}

	/// <summary>First at <see cref="StageOrder.Network"/> + 10: compares each world's newest snapshot with the predictions and replays on a mismatch (client).</summary>
	internal void Reconcile()
	{
		if (!_session.IsClient || !_session.IsActive || _bindings.Count == 0) return;
		var delta = (float)(1.0 / _session.TickRate);
		// With the fixed schedule, a correction only resets the corrected values here; the ticks after it are resimulated below.
		var schedule = _session.Config.Rollback == RollbackMode.FixedSchedule && Resimulator is not null;
		var current = _session.Tick;
		var earliest = 0u;
		foreach (var world in _session.Worlds.LiveWorlds)
		{
			var serverTick = world.LastReceivedServerTick;
			if (serverTick == 0 || serverTick <= world.ReconciledTick) continue;
			world.ReconciledTick = serverTick;
			var frame = world.Frame(serverTick);
			if (frame is null) continue;
			var corrected = false;
			foreach (var binding in _bindings) corrected |= binding.Reconcile(world, frame, serverTick, current, delta, replay: !schedule);
			if (corrected && (earliest == 0 || serverTick < earliest)) earliest = serverTick;
		}

		if (!schedule || earliest == 0 || earliest >= current) return;
		Resimulate(earliest, current);
	}

	/// <summary>Client, at the end of a tick (and of a resimulated one): records the predicted values of the local entities.</summary>
	internal void RecordTick()
	{
		if (!_session.IsActive || _bindings.Count == 0) return;
		var tick = IsResimulating ? ResimulatedTick : _session.Tick;
		if (tick == 0) return;
		foreach (var binding in _bindings)
		{
			foreach (var world in _session.Worlds.LiveWorlds) binding.RecordTick(world, tick);
		}
	}

	/// <summary>
	/// Rewinds every predicted value to tick <paramref name="from"/> (the corrected ones hold the server's value of it, the
	/// others their recorded prediction) and runs the whole fixed schedule for each tick after it up to
	/// <paramref name="to"/>, with the stored inputs.
	/// </summary>
	private void Resimulate(uint from, uint to)
	{
		foreach (var world in _session.Worlds.LiveWorlds)
		{
			foreach (var binding in _bindings) binding.Rewind(world, from);
		}

		IsResimulating = true;
		try
		{
			for (var tick = from + 1; tick <= to; tick++)
			{
				ResimulatedTick = tick;
				Resimulator!();
				ResimulatedTicks++;
				_session.MutableStats.ResimulatedTicks++;
			}
		}
		finally
		{
			IsResimulating = false;
			ResimulatedTick = 0;
		}
	}

	/// <summary>The client's tick was reset: every stored prediction is for a timeline that no longer exists.</summary>
	internal void OnResync()
	{
		foreach (var binding in _bindings) binding.Clear();
		foreach (var world in _session.Worlds.LiveWorlds) world.ReconciledTick = 0;
	}

	/// <summary>A world's state was reset or it went away: its predictions are forgotten.</summary>
	internal void OnWorldReset(NetworkWorld world)
	{
		foreach (var binding in _bindings) binding.Forget(world.Index);
	}

	private interface IInputSource<TInput> where TInput : unmanaged
	{
		bool TryGetInput(NetworkPeer peer, out TInput input);
	}

	private abstract class Binding
	{
		public abstract Type ComponentType { get; }

		/// <summary>Server: buffers the inputs received since the last step (and samples a listen server's own).</summary>
		public abstract void ServerReceive(uint tick);

		/// <summary>Server: applies each peer's input of the tick to its entities in <paramref name="world"/>.</summary>
		public abstract void ServerApply(NetworkWorld world, uint tick, float delta);

		/// <summary>Client: samples, keeps and sends the input of the tick; false without a sampler.</summary>
		public abstract bool ClientSample(uint tick);

		/// <summary>Client: applies the input of the tick to the local entities in <paramref name="world"/> and records the predictions.</summary>
		public abstract void ClientApply(NetworkWorld world, uint tick, float delta);

		/// <summary>Client: corrects mispredicted values to the server's at <paramref name="serverTick"/>, replaying the stored inputs after it when <paramref name="replay"/>. Returns whether anything was corrected.</summary>
		public abstract bool Reconcile(NetworkWorld world, SnapshotFrame frame, uint serverTick, uint currentTick, float delta, bool replay);

		/// <summary>Client: sets the local entities' values back to tick <paramref name="tick"/> (the server's for those corrected at it, else their prediction).</summary>
		public abstract void Rewind(NetworkWorld world, uint tick);

		/// <summary>Client, resimulating: applies the stored input of <paramref name="tick"/> to the local entities and records the predictions.</summary>
		public abstract void ClientReplay(NetworkWorld world, uint tick, float delta);

		/// <summary>Client: records the local entities' values as their prediction for <paramref name="tick"/> (the end of the tick).</summary>
		public abstract void RecordTick(NetworkWorld world, uint tick);

		public abstract void Clear();

		public abstract void Forget(byte world);
	}

	private sealed class Binding<TComponent, TInput> : Binding, IInputSource<TInput>
		where TComponent : unmanaged where TInput : unmanaged
	{
		private static readonly QueryDescription Owned = new QueryDescription().WithAll<NetworkId, TComponent>();

		private readonly NetworkPrediction _owner;
		private readonly ReplicatedTypeInfo<TComponent> _component;
		private readonly MessageTypeInfo<TInput> _inputInfo;
		private readonly PredictionStep<TComponent, TInput> _step;
		private readonly Func<TInput>? _sample;
		private readonly int _history;
		private NetworkReader<TInput> _reader;

		// Client: the local input per tick, and the predicted value per tick for each owned entity (by world and slot).
		private readonly TInput[] _inputs;
		private readonly uint[] _inputTicks;
		private readonly Dictionary<int, Predicted> _predicted = [];

		// Server (and a listen server's own peer): the inputs received per peer, by tick, and each peer's latest.
		private readonly PeerInputs?[] _peers = new PeerInputs?[256];

		public Binding(NetworkPrediction owner, ReplicatedTypeInfo<TComponent> component, MessageTypeInfo<TInput> input, PredictionStep<TComponent, TInput> step, Func<TInput>? sample)
		{
			_owner = owner;
			_component = component;
			_inputInfo = input;
			_step = step;
			_sample = sample;
			_history = Math.Max(8, owner._session.Config.SnapshotHistory);
			_inputs = new TInput[_history];
			_inputTicks = new uint[_history];
			_reader = owner._session.Reader<TInput>();
		}

		public override Type ComponentType => typeof(TComponent);

		public bool TryGetInput(NetworkPeer peer, out TInput input)
		{
			if (_owner._session.IsClient)
			{
				var tick = _owner.IsResimulating ? _owner.ResimulatedTick : _owner._session.Tick;
				var index = (int)(tick % (uint)_history);
				input = _inputs[index];
				return _inputTicks[index] == tick && tick != 0;
			}

			var inputs = _peers[peer.Id];
			input = inputs?.Latest ?? default;
			return inputs is not null && inputs.HasLatest;
		}

		public override void ServerReceive(uint tick)
		{
			// Buffer every input received since the last step, by the tick the client stamped it with.
			while (_reader.TryRead(out var received))
			{
				var peer = received.From;
				if (!peer.IsClient) continue;
				var inputs = _peers[peer.Id] ??= new PeerInputs(_history);
				inputs.Store(received.Tick, received.Message);
			}

			// A listen server predicts nothing, but its own entities take its local input directly.
			if (_owner._session.Mode == NetworkMode.ListenServer && _sample is not null)
			{
				var local = _peers[0] ??= new PeerInputs(_history);
				local.Store(tick, _sample());
			}
		}

		public override void ServerApply(NetworkWorld network, uint tick, float delta)
		{
			var world = network.World;
			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					var owner = ids[i].OwnerPeer;
					if (ids[i].World != network.Index || (owner == 0 && _owner._session.Mode != NetworkMode.ListenServer)) continue;
					var inputs = _peers[owner];
					if (inputs is null || !inputs.TryGet(tick, out var input)) continue;
					_step(ref values[i], input, delta);
				}
			}
		}

		public override bool ClientSample(uint tick)
		{
			if (_sample is null) return false;
			var input = _sample();
			var index = (int)(tick % (uint)_history);
			_inputs[index] = input;
			_inputTicks[index] = tick;

			// The input of this tick and the two before it, so a lost packet costs nothing.
			for (var back = 2u; back > 0; back--)
			{
				var previous = tick - back;
				var previousIndex = (int)(previous % (uint)_history);
				if (previous != 0 && _inputTicks[previousIndex] == previous) _owner._session.SendTicked(_inputInfo, previous, _inputs[previousIndex]);
			}

			_owner._session.SendTicked(_inputInfo, tick, input);
			return true;
		}

		public override void ClientReplay(NetworkWorld network, uint tick, float delta)
		{
			if (_inputTicks[(int)(tick % (uint)_history)] != tick) return;
			ClientApply(network, tick, delta);
		}

		public override void RecordTick(NetworkWorld network, uint tick)
		{
			// Only once a step has predicted the entity: entities this client never stepped are left to the snapshots.
			if (_inputTicks[(int)(tick % (uint)_history)] != tick) return;
			var local = _owner._session.LocalPeer.Id;
			foreach (ref var chunk in network.World.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					if (ids[i].OwnerPeer != local || ids[i].World != network.Index) continue;
					Record(ids[i], tick, values[i]);
				}
			}
		}

		public override void Rewind(NetworkWorld network, uint tick)
		{
			var local = _owner._session.LocalPeer.Id;
			var index = (int)(tick % (uint)_history);
			foreach (ref var chunk in network.World.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					var id = ids[i];
					if (id.OwnerPeer != local || id.World != network.Index) continue;
					if (!_predicted.TryGetValue(Key(id), out var predicted) || predicted.Id != id.Id) continue;
					// Corrected at this tick: the value is already the server's; else the prediction recorded for it.
					if (predicted.CorrectedAt == tick) continue;
					if (predicted.Ticks[index] == tick) values[i] = predicted.Values[index];
				}
			}
		}

		public override void ClientApply(NetworkWorld network, uint tick, float delta)
		{
			var input = _inputs[(int)(tick % (uint)_history)];
			var local = _owner._session.LocalPeer.Id;
			var world = network.World;
			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					if (ids[i].OwnerPeer != local || ids[i].World != network.Index) continue;
					_step(ref values[i], input, delta);
					Record(ids[i], tick, values[i]);
				}
			}
		}

		// Predictions are kept per world and slot.
		private static int Key(in NetworkId id) => (id.World << NetworkId.SlotBits) | id.Slot;

		private void Record(NetworkId id, uint tick, in TComponent value)
		{
			var key = Key(id);
			if (!_predicted.TryGetValue(key, out var predicted) || predicted.Id != id.Id)
			{
				predicted = new Predicted(id.Id, _history);
				_predicted[key] = predicted;
			}

			var index = (int)(tick % (uint)_history);
			predicted.Values[index] = value;
			predicted.Ticks[index] = tick;
		}

		public override bool Reconcile(NetworkWorld network, SnapshotFrame frame, uint serverTick, uint currentTick, float delta, bool replay)
		{
			var corrected = false;
			var column = (ReplicatedColumn<TComponent>)frame.Columns[_component.Ordinal];
			var serializer = _component.Serializer;
			var local = _owner._session.LocalPeer.Id;
			var world = network.World;

			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					var id = ids[i];
					var slot = id.Slot;
					if (id.OwnerPeer != local || id.World != network.Index || !frame.IsAlive(slot) || frame.Ids[slot] != id.Id || !column.Has(slot)) continue;
					ref readonly var authoritative = ref column.At(slot);

					var index = (int)(serverTick % (uint)_history);
					var hasPrediction = _predicted.TryGetValue(Key(id), out var predicted) && predicted.Id == id.Id && predicted.Ticks[index] == serverTick;
					if (hasPrediction && serializer.Equal(predicted!.Values[index], authoritative)) continue;

					// Mispredicted (or never predicted): take the server's value and replay the stored inputs after it.
					if (hasPrediction) _owner._session.MutableStats.Corrections++;
					corrected = true;
					var value = authoritative;
					Record(id, serverTick, value);
					_predicted[Key(id)].CorrectedAt = serverTick;
					if (replay)
					{
						for (var tick = serverTick + 1; tick <= currentTick; tick++)
						{
							var inputIndex = (int)(tick % (uint)_history);
							if (_inputTicks[inputIndex] != tick) continue;
							_step(ref value, _inputs[inputIndex], delta);
							Record(id, tick, value);
						}
					}

					values[i] = value;
				}
			}

			return corrected;
		}

		public override void Clear()
		{
			Array.Clear(_inputTicks);
			_predicted.Clear();
		}

		public override void Forget(byte world)
		{
			List<int>? stale = null;
			foreach (var key in _predicted.Keys)
			{
				if (key >> NetworkId.SlotBits == world) (stale ??= []).Add(key);
			}

			if (stale is null) return;
			foreach (var key in stale) _predicted.Remove(key);
		}

		private sealed class Predicted(uint id, int history)
		{
			public readonly uint Id = id;
			public readonly TComponent[] Values = new TComponent[history];
			public readonly uint[] Ticks = new uint[history];
			public uint CorrectedAt;
		}

		private sealed class PeerInputs(int history)
		{
			private readonly TInput[] _inputs = new TInput[history];
			private readonly uint[] _ticks = new uint[history];
			private uint _latestTick;

			public TInput Latest;

			public bool HasLatest;

			public void Store(uint tick, in TInput input)
			{
				if (tick == 0) return;
				var index = (int)(tick % (uint)_ticks.Length);
				_inputs[index] = input;
				_ticks[index] = tick;
				if (tick >= _latestTick)
				{
					_latestTick = tick;
					Latest = input;
					HasLatest = true;
				}
			}

			/// <summary>The input stamped with <paramref name="tick"/>, else the latest one received before it (a late input repeats the last one).</summary>
			public bool TryGet(uint tick, out TInput input)
			{
				var index = (int)(tick % (uint)_ticks.Length);
				if (_ticks[index] == tick)
				{
					input = _inputs[index];
					return true;
				}

				for (var back = 1u; back < (uint)_ticks.Length && tick > back; back++)
				{
					var earlier = tick - back;
					var earlierIndex = (int)(earlier % (uint)_ticks.Length);
					if (_ticks[earlierIndex] == earlier)
					{
						input = _inputs[earlierIndex];
						return true;
					}
				}

				input = Latest;
				return HasLatest;
			}
		}
	}
}
