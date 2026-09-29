using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// Client-side prediction with server reconciliation (<see cref="INetworkPrediction"/>). A client applies its sampled
/// input to the predicted components of the entities it owns in every fixed step, keeps the input and the predicted
/// value per tick, and when a snapshot arrives compares the server's value at the snapshot tick with its prediction for
/// that tick, correcting and replaying the stored inputs on a mismatch. The server applies each client's input for the
/// tick (or its latest one, when the input for the tick is late or lost) to that client's entities.
/// </summary>
public sealed class NetworkPrediction : INetworkPrediction
{
	private readonly NetworkSession _session;
	private readonly NetworkWorld _world;
	private readonly IEvents _events;
	private readonly List<Binding> _bindings = [];
	private uint _reconciled;

	internal NetworkPrediction(NetworkSession session, NetworkWorld world, IEvents events)
	{
		_session = session;
		_world = world;
		_events = events;
	}

	/// <summary>The number of registered prediction steps.</summary>
	public int Count => _bindings.Count;

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
		var tick = _world.CurrentTick;
		foreach (var binding in _bindings)
		{
			if (_session.IsServer) binding.ServerStep(tick, delta);
			else binding.ClientStep(tick, delta);
		}
	}

	/// <summary>First at <see cref="StageOrder.Network"/> + 10: compares the newest snapshot with the predictions and replays on a mismatch (client).</summary>
	internal void Reconcile()
	{
		if (!_session.IsClient || !_session.IsActive || _bindings.Count == 0) return;
		var serverTick = _world.LastReceivedServerTick;
		if (serverTick == 0 || serverTick <= _reconciled) return;
		_reconciled = serverTick;
		var frame = _world.Frame(serverTick);
		if (frame is null) return;
		var delta = (float)(1.0 / _session.TickRate);
		foreach (var binding in _bindings) binding.Reconcile(frame, serverTick, _world.CurrentTick, delta);
	}

	/// <summary>The client's tick was reset: every stored prediction is for a timeline that no longer exists.</summary>
	internal void OnResync()
	{
		foreach (var binding in _bindings) binding.Clear();
		_reconciled = 0;
	}

	private interface IInputSource<TInput> where TInput : unmanaged
	{
		bool TryGetInput(NetworkPeer peer, out TInput input);
	}

	private abstract class Binding
	{
		public abstract Type ComponentType { get; }

		public abstract void ServerStep(uint tick, float delta);

		public abstract void ClientStep(uint tick, float delta);

		public abstract void Reconcile(SnapshotFrame frame, uint serverTick, uint currentTick, float delta);

		public abstract void Clear();
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

		// Client: the local input per tick, and the predicted value per tick for each owned entity (by slot).
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
				var tick = _owner._world.CurrentTick;
				var index = (int)(tick % (uint)_history);
				input = _inputs[index];
				return _inputTicks[index] == tick && tick != 0;
			}

			var inputs = _peers[peer.Id];
			input = inputs?.Latest ?? default;
			return inputs is not null && inputs.HasLatest;
		}

		public override void ServerStep(uint tick, float delta)
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

			var world = _owner._world.World;
			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					var owner = ids[i].OwnerPeer;
					if (owner == 0 && _owner._session.Mode != NetworkMode.ListenServer) continue;
					var inputs = _peers[owner];
					if (inputs is null || !inputs.TryGet(tick, out var input)) continue;
					_step(ref values[i], input, delta);
				}
			}
		}

		public override void ClientStep(uint tick, float delta)
		{
			if (_sample is null) return;
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

			var local = _owner._session.LocalPeer.Id;
			var world = _owner._world.World;
			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					if (ids[i].OwnerPeer != local) continue;
					_step(ref values[i], input, delta);
					Record(ids[i], tick, values[i]);
				}
			}
		}

		private void Record(NetworkId id, uint tick, in TComponent value)
		{
			if (!_predicted.TryGetValue(id.Slot, out var predicted) || predicted.Id != id.Id)
			{
				predicted = new Predicted(id.Id, _history);
				_predicted[id.Slot] = predicted;
			}

			var index = (int)(tick % (uint)_history);
			predicted.Values[index] = value;
			predicted.Ticks[index] = tick;
		}

		public override void Reconcile(SnapshotFrame frame, uint serverTick, uint currentTick, float delta)
		{
			var column = (ReplicatedColumn<TComponent>)frame.Columns[_component.Ordinal];
			var serializer = _component.Serializer;
			var local = _owner._session.LocalPeer.Id;
			var world = _owner._world.World;

			foreach (ref var chunk in world.Query(in Owned))
			{
				var ids = chunk.GetSpan<NetworkId>();
				var values = chunk.GetSpan<TComponent>();
				for (var i = 0; i < chunk.Count; i++)
				{
					var id = ids[i];
					var slot = id.Slot;
					if (id.OwnerPeer != local || !frame.IsAlive(slot) || frame.Ids[slot] != id.Id || !column.Has(slot)) continue;
					ref readonly var authoritative = ref column.At(slot);

					var index = (int)(serverTick % (uint)_history);
					var hasPrediction = _predicted.TryGetValue(slot, out var predicted) && predicted.Id == id.Id && predicted.Ticks[index] == serverTick;
					if (hasPrediction && serializer.Equal(predicted!.Values[index], authoritative)) continue;

					// Mispredicted (or never predicted): take the server's value and replay the stored inputs after it.
					if (hasPrediction) _owner._session.MutableStats.Corrections++;
					var value = authoritative;
					for (var tick = serverTick + 1; tick <= currentTick; tick++)
					{
						var inputIndex = (int)(tick % (uint)_history);
						if (_inputTicks[inputIndex] != tick) continue;
						_step(ref value, _inputs[inputIndex], delta);
						Record(id, tick, value);
					}

					values[i] = value;
				}
			}
		}

		public override void Clear()
		{
			Array.Clear(_inputTicks);
			_predicted.Clear();
		}

		private sealed class Predicted(uint id, int history)
		{
			public readonly uint Id = id;
			public readonly TComponent[] Values = new TComponent[history];
			public readonly uint[] Ticks = new uint[history];
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
