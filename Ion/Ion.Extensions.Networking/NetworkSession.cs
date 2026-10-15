using System.Security.Cryptography;
using System.Text;

using Arch.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Ion.Extensions.Metrics;
using Ion.Extensions.Ecs;

namespace Ion.Extensions.Networking;

/// <summary>
/// A network session: the transport, the peers and their handshakes, the security checks, the typed messages
/// (<see cref="INetworkMessages"/>), the tick, and the owner of the replication (<see cref="Worlds"/>: the root
/// <see cref="World"/> and the named scene worlds) and prediction (<see cref="Prediction"/>). Driven by the networking
/// systems from the game thread; nothing in it is thread safe.
/// </summary>
public sealed class NetworkSession : INetworkSession, INetworkMessages, IDisposable
{
	private readonly NetworkConfig _config;
	private readonly INetworkTransport? _transport;
	private readonly IEvents _events;
	private readonly IClock _clock;
	private readonly ILogger _logger;
	private readonly NetworkMetrics? _metrics;
	private readonly ulong _gameHash;
	private readonly byte[]? _secret;

	private readonly byte[] _receive = new byte[Protocol.MaxPacketSize];
	private readonly byte[] _scratch = new byte[Protocol.MaxPacketSize];
	private readonly byte[] _control = new byte[256];
	private PeerState?[] _byConnection = new PeerState?[8];
	private readonly PeerState?[] _byPeer = new PeerState?[256];
	private readonly List<NetworkPeer> _peers = [];
	private readonly List<PeerState> _states = [];
	private NetworkStats _stats;
	private NetworkState _state;
	private NetworkPeer _localPeer = NetworkPeer.None;
	private DisconnectReason _reason;
	private TimeSpan _handshakeSentAt;
	private bool _started;
	private int _mtu;
	private double _syncError;

	/// <summary>Creates a session. Normally created by <c>AddNetworking</c>.</summary>
	/// <param name="config">The settings.</param>
	/// <param name="transport">The transport (null only in <see cref="NetworkMode.Offline"/>).</param>
	/// <param name="world">The root ECS world (always replicated; scene worlds join with <see cref="NetworkWorlds.Attach"/>).</param>
	/// <param name="events">The frame event bus inbound messages are emitted on.</param>
	/// <param name="clock">The clock timeouts, pings and rate limits are measured with.</param>
	/// <param name="tickRate">The fixed-step rate, used when <see cref="NetworkConfig.TickRate"/> is 0.</param>
	/// <param name="gameTitle">The game id used when <see cref="NetworkConfig.GameId"/> is empty.</param>
	/// <param name="logger">Optional logger.</param>
	/// <param name="metrics">Optional metrics module, which gets the network counters.</param>
	/// <param name="table">The type table (the process registry's, <see cref="NetworkRegistry.Table"/>, when null).</param>
	public NetworkSession(NetworkConfig config, INetworkTransport? transport, World world, IEvents events, IClock clock, double tickRate, string gameTitle, ILogger? logger = null, IMetrics? metrics = null, NetworkTypeTable? table = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(events);
		ArgumentNullException.ThrowIfNull(clock);

		_config = config;
		_transport = transport;
		_events = events;
		_clock = clock;
		_logger = logger ?? NullLogger.Instance;
		_metrics = metrics is null ? null : new NetworkMetrics(metrics);

		TickRate = config.TickRate > 0 ? config.TickRate : tickRate > 0 ? tickRate : GameConfig.DefaultFixedUpdateRate;
		Mode = config.Mode;
		Table = table ?? NetworkRegistry.Table;
		var gameId = string.IsNullOrEmpty(config.GameId) ? gameTitle ?? "" : config.GameId;
		_gameHash = NetworkTypeTable.Fnv1a64(gameId);
		_secret = string.IsNullOrEmpty(config.JoinSecret) ? null : Encoding.UTF8.GetBytes(config.JoinSecret);
		_mtu = Math.Clamp(config.MtuBytes, 256, Protocol.MaxPacketSize);

		Worlds = new NetworkWorlds(this, new NetworkWorld(this, world, config, Table));
		Prediction = new NetworkPrediction(this, events);
	}

	/// <summary>The settings.</summary>
	public NetworkConfig Config => _config;

	/// <summary>The transport, or null when offline.</summary>
	public INetworkTransport? Transport => _transport;

	/// <summary>The type table in use (ordinals and the registry hash).</summary>
	public NetworkTypeTable Table { get; }

	/// <summary>The simulation rate in ticks per second.</summary>
	public double TickRate { get; }

	/// <summary>The root world's replication.</summary>
	public NetworkWorld World => Worlds.Root;

	internal NetworkWorld Root => Worlds.Root;

	/// <summary>The networked worlds: the root and the named worlds of the loaded scenes.</summary>
	public NetworkWorlds Worlds { get; }

	/// <summary>The current tick, shared by every world (incremented at the start of every fixed step; on a client, ahead of the server).</summary>
	public uint Tick { get; private set; }

	/// <summary>Prediction and reconciliation.</summary>
	public NetworkPrediction Prediction { get; }

	/// <inheritdoc/>
	public NetworkMode Mode { get; }

	/// <inheritdoc/>
	public bool IsServer => Mode is NetworkMode.Server or NetworkMode.ListenServer;

	/// <inheritdoc/>
	public bool IsClient => Mode == NetworkMode.Client;

	/// <inheritdoc/>
	public NetworkState State => _state;

	/// <inheritdoc/>
	public NetworkPeer LocalPeer => _localPeer;

	/// <inheritdoc/>
	public DisconnectReason DisconnectReason => _reason;

	/// <inheritdoc/>
	public IReadOnlyList<NetworkPeer> Peers => _peers;

	/// <inheritdoc/>
	public ref readonly NetworkStats Stats => ref _stats;

	internal ref NetworkStats MutableStats => ref _stats;

	internal IClock Clock => _clock;

	internal IEvents Events => _events;

	internal int Mtu => _mtu;

	/// <summary>Whether packets flow: a running server, or a client whose handshake was accepted.</summary>
	public bool IsActive => _state == NetworkState.Connected;

	/// <inheritdoc/>
	public TimeSpan RoundTripTime(NetworkPeer peer) => _byPeer[peer.Id]?.Rtt ?? TimeSpan.Zero;

	internal List<PeerState> States => _states;

	// Lifecycle ----------------------------------------------------------------------------------------------------------

	/// <summary>Starts the transport in the configured role (Init).</summary>
	public void Start()
	{
		if (_started || Mode == NetworkMode.Offline) return;
		if (_transport is null) throw new InvalidOperationException($"Networking mode {Mode} needs a transport: register one (AddLoopbackTransport, AddLiteNetLibTransport).");
		_started = true;
		Worlds.Freeze();
		_mtu = Math.Min(_mtu, _transport.MaxPacketSize(Delivery.Unreliable));

		if (IsServer)
		{
			if (!IsLoopback(_config.Bind)) _logger.LogWarning("Network server bound to the public address {Bind}:{Port}: it accepts connections from other machines.", _config.Bind, _config.Port);
			_transport.StartServer(_config.Bind, _config.Port, Math.Clamp(_config.MaxPeers, 1, NetworkPeer.MaxClientId));
			_localPeer = NetworkPeer.Server;
			_state = NetworkState.Connected;
			_logger.LogInformation("Network server started on {Bind}:{Port} ({Transport}, {Rate} ticks per second, registry {Hash:X16}, {Worlds} named worlds).", _config.Bind, _transport.LocalPort, _transport.Name, TickRate, Worlds.Hash(Table.Hash), Worlds.Names.Count);
		}
		else
		{
			_transport.StartClient(_config.Connect, _config.Port);
			_state = NetworkState.Connecting;
			_logger.LogInformation("Network client connecting to {Address}:{Port} ({Transport}).", _config.Connect, _config.Port, _transport.Name);
		}
	}

	/// <summary>Disconnects every peer and stops the transport (Destroy).</summary>
	public void Stop()
	{
		if (!_started || _transport is null) return;
		foreach (var state in _states.ToArray())
		{
			if (state.Stage != PeerStage.Closing) SendControl(state, PacketKind.Disconnect, (byte)DisconnectReason.Shutdown);
		}

		_transport.Flush();
		_transport.Stop();
		_started = false;
		_state = NetworkState.Offline;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Stop();
		_transport?.Dispose();
	}

	private static bool IsLoopback(string address) =>
		address is "127.0.0.1" or "localhost" or "::1" || address.StartsWith("127.", StringComparison.Ordinal);

	// Receive -------------------------------------------------------------------------------------------------------------

	/// <summary>Drains the transport: handshakes, messages, snapshots, owner updates; then timeouts, and on a client applies the newest snapshot (First).</summary>
	public void Poll()
	{
		if (!_started || _transport is null) return;
		_transport.Poll();

		while (_transport.TryReceive(_receive, out var e))
		{
			switch (e.Kind)
			{
				case TransportEventKind.Connected:
					OnConnected(e.Connection);
					break;
				case TransportEventKind.Disconnected:
					OnDisconnected(e.Connection);
					break;
				case TransportEventKind.Data:
					if (e.Length <= _receive.Length) OnData(e.Connection, new ReadOnlySpan<byte>(_receive, 0, e.Length));
					break;
			}
		}

		CheckTimeouts();

		if (IsClient && IsActive)
		{
			foreach (var world in Worlds.LiveWorlds) world.ApplyLatest();
		}
	}

	/// <summary>Where the services that follow lag compensation rewinds come from (the module wires the DI ones).</summary>
	internal Func<IEnumerable<IWorldRewindListener>>? RewindListenerSource { get; set; }

	private IWorldRewindListener[]? _rewindListeners;

	/// <summary>The services told when a world is rewound for lag compensation and restored (physics bodies follow).</summary>
	internal IWorldRewindListener[] RewindListeners => _rewindListeners ??= RewindListenerSource is { } source ? [.. source()] : [];

	/// <summary>Adds a rewind listener (tests and hosts without DI).</summary>
	internal void AddRewindListener(IWorldRewindListener listener) => _rewindListeners = [.. RewindListeners, listener];

	// Ticks -------------------------------------------------------------------------------------------------------------------

	/// <summary>Opens a tick: increments <see cref="Tick"/> (FixedUpdate begin).</summary>
	internal void BeginTick()
	{
		// A resimulated tick already has its number (Prediction.ResimulatedTick).
		if (!IsActive || Prediction.IsResimulating) return;
		Tick++;
	}

	/// <summary>Closes a tick: the server captures every live world (FixedUpdate end, after every fixed step of the root and the scenes).</summary>
	internal void Capture()
	{
		// A client records its predictions as the tick ends, as the server captures it, so every fixed step's effect is in
		// the value compared with the snapshot.
		if (IsClient) Prediction.RecordTick();
		if (Prediction.IsResimulating) return;
		foreach (var world in Worlds.LiveWorlds) world.Capture();
	}

	/// <summary>Client: writes the interpolated state of the remote entities of every live world (Render).</summary>
	internal void Interpolate()
	{
		foreach (var world in Worlds.LiveWorlds) world.Interpolate();
	}

	// The client runs ahead of the server's snapshot by a full round trip (the snapshot took half of it to arrive, and an
	// input takes the other half to reach the server) plus the input lead, so its inputs for tick N arrive before the
	// server simulates N. The error is smoothed; beyond 3 ticks the tick is reset (and predictions cleared). Driven by the
	// root world's snapshots, which every client gets.
	internal void SyncTick(uint serverTick)
	{
		var rtt = RoundTripTime(NetworkPeer.Server).TotalSeconds;
		var target = serverTick + rtt * TickRate + Math.Max(0, _config.InputLeadTicks);
		var error = Tick - target;
		_syncError = _syncError * 0.8 + error * 0.2;
		if (Math.Abs(_syncError) <= 3 && Math.Abs(error) <= 12) return;
		Tick = (uint)Math.Max(1, Math.Round(target));
		_syncError = 0;
		_stats.Resyncs++;
		Prediction.OnResync();
	}

	private void OnConnected(int connection)
	{
		var now = _clock.Elapsed;
		var state = new PeerState(connection, _mtu, now)
		{
			TokensAt = now,
			ByteTokens = _config.MaxBytesPerSecond,
			MessageTokens = _config.MaxMessagesPerSecond,
		};
		SetConnection(connection, state);
		_states.Add(state);

		if (IsServer)
		{
			// A random nonce the client must sign with the join secret; the handshake must complete in time.
			RandomNumberGenerator.Fill(state.Nonce);
			var w = new NetWriter(_control);
			Protocol.WriteHeader(ref w, PacketKind.Challenge, Tick, 0);
			w.WriteUInt16(NetworkTypeTable.ProtocolVersion);
			w.WriteBytes(state.Nonce);
			SendRaw(state, w.Written, Delivery.ReliableOrdered);
		}
		else
		{
			state.Peer = NetworkPeer.Server;
		}
	}

	private void OnDisconnected(int connection)
	{
		var state = GetConnection(connection);
		if (state is null) return;
		RemoveState(state, state.Reason);

		if (IsClient)
		{
			if (_state != NetworkState.Disconnected)
			{
				_state = NetworkState.Disconnected;
				_reason = state.Reason;
				_events.Emit(new PeerDisconnected(NetworkPeer.Server, _reason));
			}
		}
	}

	private void OnData(int connection, ReadOnlySpan<byte> packet)
	{
		var state = GetConnection(connection);
		if (state is null) return;

		_stats.BytesIn += packet.Length;
		_stats.PacketsIn++;
		state.LastReceived = _clock.Elapsed;

		if (packet.Length < Protocol.HeaderSize)
		{
			Malformed(state);
			return;
		}

		var reader = new NetReader(packet);
		var kind = (PacketKind)reader.ReadByte();
		var tick = reader.ReadUInt32();
		var ack = reader.ReadUInt32();

		if (IsServer) OnServerData(state, kind, tick, ack, ref reader, packet.Length);
		else OnClientData(state, kind, tick, ref reader);
	}

	private void OnServerData(PeerState state, PacketKind kind, uint tick, uint ack, ref NetReader reader, int length)
	{
		if (state.Stage == PeerStage.Closing) return;

		if (!TakeBytes(state, length))
		{
			_stats.RateLimited++;
			Violation(state);
			return;
		}

		if (kind == PacketKind.Handshake)
		{
			if (state.Stage == PeerStage.Handshaking) OnHandshake(state, ref reader);
			else Violation(state);
			return;
		}

		if (state.Stage != PeerStage.Connected)
		{
			// Nothing but the handshake is parsed before the handshake is accepted.
			_stats.Rejected++;
			Violation(state);
			return;
		}

		Root.OnAck(state, ack);

		switch (kind)
		{
			case PacketKind.Messages:
				OnMessages(state, ref reader);
				break;
			case PacketKind.OwnerUpdate:
				OnOwnerUpdate(state, ref reader);
				break;
			case PacketKind.OpenWorld:
				OnOpenWorld(state, ref reader);
				break;
			case PacketKind.CloseWorld:
				OnCloseWorld(state, ref reader);
				break;
			case PacketKind.WorldAck:
				OnWorldAck(state, ref reader);
				break;
			case PacketKind.Ping:
				OnPing(state, ref reader);
				break;
			case PacketKind.Pong:
				OnPong(state, ref reader);
				break;
			case PacketKind.Disconnect:
				var reason = (DisconnectReason)reader.ReadByte();
				BeginClose(state, reason);
				break;
			default:
				Malformed(state);
				break;
		}

		_ = tick;
	}

	private void OnClientData(PeerState state, PacketKind kind, uint tick, ref NetReader reader)
	{
		switch (kind)
		{
			case PacketKind.Challenge:
				if (state.Stage == PeerStage.Handshaking) OnChallenge(state, ref reader);
				else _stats.Malformed++;
				return;
			case PacketKind.Accept:
				if (state.Stage == PeerStage.Handshaking) OnAccept(state, tick, ref reader);
				else _stats.Malformed++;
				return;
			case PacketKind.Reject:
			case PacketKind.Disconnect:
				var reason = (DisconnectReason)reader.ReadByte();
				if (kind == PacketKind.Reject) _stats.HandshakesRejected++;
				state.Reason = reason;
				_reason = reason;
				_logger.LogInformation("Disconnected by the server: {Reason}.", reason);
				_transport!.Disconnect(state.Connection);
				RemoveState(state, reason);
				OnClientLost(reason);
				return;
		}

		if (state.Stage != PeerStage.Connected) return;

		switch (kind)
		{
			case PacketKind.Snapshot:
				OnSnapshotPart(tick, ref reader);
				break;
			case PacketKind.WorldUnavailable:
				OnWorldUnavailable(ref reader);
				break;
			case PacketKind.Messages:
				OnMessages(state, ref reader);
				break;
			case PacketKind.Ping:
				OnPing(state, ref reader);
				break;
			case PacketKind.Pong:
				OnPong(state, ref reader);
				break;
			default:
				_stats.Malformed++;
				break;
		}
	}

	// Handshake -------------------------------------------------------------------------------------------------------------

	private void OnChallenge(PeerState state, ref NetReader reader)
	{
		var protocol = reader.ReadUInt16();
		var nonce = reader.ReadBytes(Protocol.NonceSize);
		if (reader.Failed)
		{
			_stats.Malformed++;
			return;
		}

		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, PacketKind.Handshake, Tick, 0);
		w.WriteUInt16(NetworkTypeTable.ProtocolVersion);
		w.WriteUInt64(_gameHash);
		w.WriteUInt64(Worlds.Hash(Table.Hash));
		w.WriteDouble(TickRate);
		Span<byte> proof = stackalloc byte[Protocol.ProofSize];
		if (_secret is not null) HMACSHA256.HashData(_secret, nonce, proof);
		else proof.Clear();
		w.WriteBytes(proof);
		_ = protocol;
		SendRaw(state, w.Written, Delivery.ReliableOrdered);
		_handshakeSentAt = _clock.Elapsed;
	}

	private void OnHandshake(PeerState state, ref NetReader reader)
	{
		var protocol = reader.ReadUInt16();
		var game = reader.ReadUInt64();
		var registry = reader.ReadUInt64();
		var rate = reader.ReadDouble();
		var proof = reader.ReadBytes(Protocol.ProofSize);

		DisconnectReason reason;
		if (reader.Failed) reason = DisconnectReason.ProtocolMismatch;
		else if (protocol != NetworkTypeTable.ProtocolVersion) reason = DisconnectReason.ProtocolMismatch;
		else if (game != _gameHash) reason = DisconnectReason.GameMismatch;
		else if (registry != Worlds.Hash(Table.Hash)) reason = DisconnectReason.RegistryMismatch;
		else if (Math.Abs(rate - TickRate) > 1e-6) reason = DisconnectReason.TickRateMismatch;
		else if (!SecretMatches(state, proof)) reason = DisconnectReason.WrongSecret;
		else if (_peers.Count >= Math.Clamp(_config.MaxPeers, 1, NetworkPeer.MaxClientId)) reason = DisconnectReason.ServerFull;
		else reason = DisconnectReason.None;

		if (reason != DisconnectReason.None)
		{
			_stats.HandshakesRejected++;
			_logger.LogWarning("Refused a client on connection {Connection}: {Reason}.", state.Connection, reason);
			SendControl(state, PacketKind.Reject, (byte)reason);
			BeginClose(state, reason);
			return;
		}

		byte id = 1;
		while (id <= NetworkPeer.MaxClientId && _byPeer[id] is not null) id++;
		state.Peer = new NetworkPeer(id);
		state.Stage = PeerStage.Connected;
		_byPeer[id] = state;
		_peers.Add(state.Peer);
		Root.AddPeer(state, 0);

		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, PacketKind.Accept, Tick, 0);
		w.WriteByte(id);
		w.WriteDouble(TickRate);
		SendRaw(state, w.Written, Delivery.ReliableOrdered);

		_logger.LogInformation("Client {Peer} joined (connection {Connection}).", state.Peer, state.Connection);
		_events.Emit(new PeerConnected(state.Peer));
	}

	private bool SecretMatches(PeerState state, ReadOnlySpan<byte> proof)
	{
		if (_secret is null) return true;
		Span<byte> expected = stackalloc byte[Protocol.ProofSize];
		HMACSHA256.HashData(_secret, state.Nonce, expected);
		return CryptographicOperations.FixedTimeEquals(expected, proof);
	}

	private void OnAccept(PeerState state, uint serverTick, ref NetReader reader)
	{
		var id = reader.ReadByte();
		var rate = reader.ReadDouble();
		if (reader.Failed || id == 0 || id > NetworkPeer.MaxClientId)
		{
			_stats.Malformed++;
			return;
		}

		_ = rate;
		state.Stage = PeerStage.Connected;
		state.Rtt = _clock.Elapsed - _handshakeSentAt;
		_localPeer = new NetworkPeer(id);
		_byPeer[0] = state;
		_peers.Add(NetworkPeer.Server);
		_state = NetworkState.Connected;
		_reason = DisconnectReason.None;

		// The server is half a round trip past the tick it stamped the accept with, and an input takes another half to
		// reach it: a full round trip ahead, plus the lead.
		Tick = serverTick + (uint)Math.Max(0, Math.Ceiling(state.Rtt.TotalSeconds * TickRate)) + (uint)Math.Max(0, _config.InputLeadTicks);
		_syncError = 0;
		Root.OnAccepted();

		// The named worlds created before the connection are asked for now.
		foreach (var world in Worlds.LiveWorlds)
		{
			if (world.Index == 0) continue;
			world.Reset();
			SendWorldControl(state, PacketKind.OpenWorld, world.Index, world.Epoch);
		}

		_logger.LogInformation("Joined the server as {Peer}.", _localPeer);
		_events.Emit(new PeerConnected(_localPeer));
	}

	// Worlds -------------------------------------------------------------------------------------------------------------------

	/// <summary>A named world became live: the server sends it to the clients that have it open; a client asks for it.</summary>
	internal void OnWorldAttached(NetworkWorld world)
	{
		if (IsServer)
		{
			foreach (var state in _states)
			{
				if (state.Stage == PeerStage.Connected && state.OpenWorlds.TryGetValue(world.Index, out var epoch)) world.AddPeer(state, epoch);
			}
		}
		else if (IsActive && _byPeer[0] is { } server)
		{
			SendWorldControl(server, PacketKind.OpenWorld, world.Index, world.Epoch);
		}
	}

	/// <summary>A named world went away: the server tells the clients that had it; a client tells the server it closed it.</summary>
	internal void OnWorldDetached(NetworkWorld world)
	{
		if (IsServer)
		{
			foreach (var view in world.Views.ToArray())
			{
				if (view.State.Stage == PeerStage.Connected) SendWorldControl(view.State, PacketKind.WorldUnavailable, world.Index, view.Epoch);
				world.RemovePeer(view.State);
			}
		}
		else if (IsActive && _byPeer[0] is { } server)
		{
			SendWorldControl(server, PacketKind.CloseWorld, world.Index, world.Epoch);
		}

		Prediction.OnWorldReset(world);
	}

	private void SendWorldControl(PeerState state, PacketKind kind, byte index, byte epoch)
	{
		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, kind, Tick, Root.AckTick);
		w.WriteByte(index);
		w.WriteByte(epoch);
		SendRaw(state, w.Written, Delivery.ReliableOrdered);
	}

	private void OnOpenWorld(PeerState state, ref NetReader reader)
	{
		var index = reader.ReadByte();
		var epoch = reader.ReadByte();
		if (reader.Failed || !Worlds.IsNamed(index))
		{
			Malformed(state);
			return;
		}

		state.OpenWorlds[index] = epoch;
		if (Worlds.Get(index) is { } world)
		{
			world.RemovePeer(state);
			world.AddPeer(state, epoch);
		}
		else
		{
			SendWorldControl(state, PacketKind.WorldUnavailable, index, epoch);
		}
	}

	private void OnCloseWorld(PeerState state, ref NetReader reader)
	{
		var index = reader.ReadByte();
		var epoch = reader.ReadByte();
		if (reader.Failed || !Worlds.IsNamed(index))
		{
			Malformed(state);
			return;
		}

		// A close of an earlier open (the client reopened since) leaves the newer one.
		if (!state.OpenWorlds.TryGetValue(index, out var open) || open != epoch) return;
		state.OpenWorlds.Remove(index);
		Worlds.Get(index)?.RemovePeer(state);
	}

	private void OnWorldAck(PeerState state, ref NetReader reader)
	{
		while (!reader.End)
		{
			var index = reader.ReadByte();
			var tick = reader.ReadUInt32();
			if (reader.Failed || !Worlds.IsNamed(index))
			{
				Malformed(state);
				return;
			}

			Worlds.Get(index)?.OnAck(state, tick);
		}
	}

	private void OnOwnerUpdate(PeerState state, ref NetReader reader)
	{
		var index = reader.ReadByte();
		if (reader.Failed || (index != 0 && !Worlds.IsNamed(index)))
		{
			Malformed(state);
			return;
		}

		Worlds.Get(index)?.OnOwnerUpdate(state, ref reader);
	}

	private void OnSnapshotPart(uint tick, ref NetReader reader)
	{
		var baseline = reader.ReadUInt32();
		var part = reader.ReadUInt16();
		var flags = reader.ReadByte();
		var index = reader.ReadByte();
		var epoch = reader.ReadByte();
		if (reader.Failed || tick == 0 || part >= Protocol.MaxParts)
		{
			_stats.Malformed++;
			return;
		}

		// A world this client does not have open (closed, or a snapshot of an earlier open still in flight) is skipped.
		if (Worlds.Get(index) is not { } world || world.Epoch != epoch) return;
		world.OnSnapshotPart(tick, baseline, part, flags, ref reader);
	}

	private void OnWorldUnavailable(ref NetReader reader)
	{
		var index = reader.ReadByte();
		var epoch = reader.ReadByte();
		if (reader.Failed)
		{
			_stats.Malformed++;
			return;
		}

		if (Worlds.Get(index) is not { } world || world.Epoch != epoch) return;
		_logger.LogInformation("The server has no networked world '{World}' (now): it stays empty until the server has it.", world.Name);
		world.Reset();
		Prediction.OnWorldReset(world);
		_events.Emit(new NetworkWorldUnavailable(world.Index));
	}

	// Messages ---------------------------------------------------------------------------------------------------------------

	private void OnMessages(PeerState state, ref NetReader reader)
	{
		while (!reader.End)
		{
			var ordinal = reader.ReadVarUInt32();
			var tick = reader.ReadUInt32();
			var length = reader.ReadUInt16();
			var payload = reader.ReadBytes(length);
			if (reader.Failed)
			{
				Malformed(state);
				return;
			}

			var info = Table.Message(ordinal);
			if (info is null)
			{
				Malformed(state);
				return;
			}

			if (IsServer)
			{
				if (!info.ClientMaySend)
				{
					_stats.Rejected++;
					Violation(state);
					if (state.Stage == PeerStage.Closing) return;
					continue;
				}

				if (!TakeMessage(state))
				{
					_stats.RateLimited++;
					Violation(state);
					if (state.Stage == PeerStage.Closing) return;
					continue;
				}
			}
			else if (!info.ServerMaySend)
			{
				_stats.Rejected++;
				continue;
			}

			var payloadReader = new NetReader(payload);
			if (!info.Dispatch(ref payloadReader, state.Peer, tick, _events) || !payloadReader.End)
			{
				Malformed(state);
				return;
			}

			_stats.MessagesIn++;
		}
	}

	/// <inheritdoc/>
	[SendsNetworkMessage]
	public void Send<T>(NetworkPeer peer, in T message) where T : unmanaged => Send(peer, message, Info<T>().Delivery);

	/// <inheritdoc/>
	[SendsNetworkMessage]
	public void Send<T>(NetworkPeer peer, in T message, Delivery delivery) where T : unmanaged
	{
		var info = Info<T>();
		if (IsServer && peer.IsServer)
		{
			// A listen server's own messages (of either direction) are delivered locally.
			_events.Emit(new NetworkMessageReceived<T>(NetworkPeer.Server, Tick, message));
			return;
		}

		CheckDirection(info);

		var state = _byPeer[peer.Id];
		if (state is null || state.Stage != PeerStage.Connected) return;
		var payload = Serialize(info, message);
		Append(state, delivery, (uint)info.Ordinal, Tick, payload);
	}

	/// <inheritdoc/>
	[SendsNetworkMessage]
	public void Broadcast<T>(in T message) where T : unmanaged
	{
		var info = Info<T>();
		CheckDirection(info);
		if (IsClient)
		{
			SendToServer(message);
			return;
		}

		if (!IsActive) return;
		var payload = Serialize(info, message);
		foreach (var state in _states)
		{
			if (state.Stage == PeerStage.Connected) Append(state, info.Delivery, (uint)info.Ordinal, Tick, payload);
		}
	}

	/// <inheritdoc/>
	[SendsNetworkMessage]
	public void SendToServer<T>(in T message) where T : unmanaged => Send(NetworkPeer.Server, message);

	/// <inheritdoc/>
	[ReadsNetworkMessage]
	public NetworkReader<T> Reader<T>() where T : unmanaged => new(_events.Reader<NetworkMessageReceived<T>>());

	/// <summary>Sends <paramref name="message"/> to the server stamped with <paramref name="tick"/> (prediction inputs).</summary>
	internal void SendTicked<T>(MessageTypeInfo<T> info, uint tick, in T message) where T : unmanaged
	{
		var state = _byPeer[0];
		if (!IsClient || state is null || state.Stage != PeerStage.Connected) return;
		var payload = Serialize(info, message);
		Append(state, info.Delivery, (uint)info.Ordinal, tick, payload);
	}

	private static MessageTypeInfo<T> Info<T>() where T : unmanaged =>
		NetworkTypes<T>.Message ?? throw new InvalidOperationException($"{typeof(T).Name} is not a registered network message: mark it [NetworkMessage] (the networking generator registers it).");

	private void CheckDirection(MessageTypeInfo info)
	{
		if (IsServer && !info.ServerMaySend) throw new InvalidOperationException($"{info.Name} is a client-to-server message; the server cannot send it.");
		if (IsClient && !info.ClientMaySend) throw new InvalidOperationException($"{info.Name} is a server-to-client message; a client cannot send it.");
	}

	private ReadOnlySpan<byte> Serialize<T>(MessageTypeInfo<T> info, in T message) where T : unmanaged
	{
		var w = new NetWriter(_scratch);
		info.Serializer.Write(ref w, message);
		if (w.Overflowed) throw new InvalidOperationException($"{info.Name} does not fit in a packet.");
		return new ReadOnlySpan<byte>(_scratch, 0, w.Position);
	}

	private void Append(PeerState state, Delivery delivery, uint ordinal, uint tick, ReadOnlySpan<byte> payload)
	{
		var size = VarSize(ordinal) + 4 + 2 + payload.Length;
		if (Protocol.HeaderSize + size > _mtu)
		{
			_logger.LogWarning("Dropped a network message of {Size} bytes: it does not fit in one packet of {Mtu} bytes.", size, _mtu);
			return;
		}

		var box = state.Outboxes[(int)delivery];
		if (box.Length + size > _mtu) FlushOutbox(state, delivery);
		var w = new NetWriter(box.Buffer.AsSpan(box.Length));
		w.WriteVarUInt32(ordinal);
		w.WriteUInt32(tick);
		w.WriteUInt16((ushort)payload.Length);
		w.WriteBytes(payload);
		box.Length += w.Position;
		box.Count++;
		_stats.MessagesOut++;
	}

	private static int VarSize(uint value)
	{
		var size = 1;
		while (value >= 0x80)
		{
			value >>= 7;
			size++;
		}

		return size;
	}

	private void FlushOutbox(PeerState state, Delivery delivery)
	{
		var box = state.Outboxes[(int)delivery];
		if (box.Count == 0) return;
		var w = new NetWriter(box.Buffer);
		Protocol.WriteHeader(ref w, PacketKind.Messages, Tick, Root.AckTick);
		SendRaw(state, new ReadOnlySpan<byte>(box.Buffer, 0, box.Length), delivery);
		box.Reset();
	}

	// Send -------------------------------------------------------------------------------------------------------------------

	/// <summary>Sends the snapshots, messages, owner updates and pings of the frame and flushes the transport (Last).</summary>
	public void Send()
	{
		if (!_started || _transport is null) return;
		var now = _clock.Elapsed;

		if (IsActive)
		{
			foreach (var world in Worlds.LiveWorlds)
			{
				if (IsServer) world.SendSnapshots();
				else world.SendOwnerUpdates(_byPeer[0]);
			}

			if (IsClient && _byPeer[0] is { } host) SendWorldAcks(host);

			foreach (var state in _states)
			{
				if (state.Stage != PeerStage.Connected) continue;
				if (now - state.LastPing >= _config.PingInterval)
				{
					state.LastPing = now;
					var w = new NetWriter(_control);
					Protocol.WriteHeader(ref w, PacketKind.Ping, Tick, Root.AckTick);
					w.WriteInt64(now.Ticks);
					SendRaw(state, w.Written, Delivery.Unreliable);
				}

				for (var d = 0; d < state.Outboxes.Length; d++) FlushOutbox(state, (Delivery)d);
			}

			// A client acknowledges every completed snapshot of the root world, even when it has nothing else to send.
			if (IsClient && Root.AckPending && _byPeer[0] is { } server)
			{
				var w = new NetWriter(_control);
				Protocol.WriteHeader(ref w, PacketKind.Messages, Tick, Root.AckTick);
				SendRaw(server, w.Written, Delivery.Unreliable);
			}

			Root.AckPending = false;
		}

		_transport.Flush();
		_metrics?.Update(this);
	}

	/// <summary>Client: acknowledges the named worlds that completed a snapshot since the last send (the root's ack is in every header).</summary>
	private void SendWorldAcks(PeerState server)
	{
		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, PacketKind.WorldAck, Tick, Root.AckTick);
		foreach (var world in Worlds.LiveWorlds)
		{
			if (world.Index == 0 || !world.AckPending) continue;
			w.WriteByte(world.Index);
			w.WriteUInt32(world.AckTick);
			world.AckPending = false;
		}

		if (w.Position > Protocol.HeaderSize) SendRaw(server, w.Written, Delivery.Unreliable);
	}

	/// <summary>Sends a raw packet (header included) to a connection and counts it.</summary>
	internal void SendRaw(PeerState state, ReadOnlySpan<byte> packet, Delivery delivery)
	{
		_transport!.Send(state.Connection, packet, delivery);
		_stats.BytesOut += packet.Length;
		_stats.PacketsOut++;
		if (IsClient && packet.Length >= Protocol.HeaderSize) Root.AckPending = false;
	}

	private void SendControl(PeerState state, PacketKind kind, byte value)
	{
		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, kind, Tick, Root.AckTick);
		w.WriteByte(value);
		SendRaw(state, w.Written, Delivery.ReliableOrdered);
	}

	private void OnPing(PeerState state, ref NetReader reader)
	{
		var stamp = reader.ReadInt64();
		if (reader.Failed)
		{
			Malformed(state);
			return;
		}

		var w = new NetWriter(_control);
		Protocol.WriteHeader(ref w, PacketKind.Pong, Tick, Root.AckTick);
		w.WriteInt64(stamp);
		SendRaw(state, w.Written, Delivery.Unreliable);
	}

	private void OnPong(PeerState state, ref NetReader reader)
	{
		var stamp = reader.ReadInt64();
		if (reader.Failed)
		{
			Malformed(state);
			return;
		}

		var sample = _clock.Elapsed - TimeSpan.FromTicks(stamp);
		if (sample < TimeSpan.Zero) return;
		state.Rtt = state.Rtt == TimeSpan.Zero ? sample : TimeSpan.FromTicks((state.Rtt.Ticks * 7 + sample.Ticks) / 8);
	}

	// Security --------------------------------------------------------------------------------------------------------------

	private bool TakeBytes(PeerState state, int length)
	{
		Refill(state);
		if (state.ByteTokens < length) return false;
		state.ByteTokens -= length;
		return true;
	}

	private bool TakeMessage(PeerState state)
	{
		Refill(state);
		if (state.MessageTokens < 1) return false;
		state.MessageTokens -= 1;
		return true;
	}

	private void Refill(PeerState state)
	{
		var now = _clock.Elapsed;
		var seconds = (now - state.TokensAt).TotalSeconds;
		if (seconds <= 0) return;
		state.TokensAt = now;
		state.ByteTokens = Math.Min(_config.MaxBytesPerSecond, state.ByteTokens + seconds * _config.MaxBytesPerSecond);
		state.MessageTokens = Math.Min(_config.MaxMessagesPerSecond, state.MessageTokens + seconds * _config.MaxMessagesPerSecond);
	}

	private bool _truncationLogged;

	/// <summary>A snapshot had more parts than <see cref="Protocol.MaxParts"/> (logged once).</summary>
	internal void ReportTruncated(int slots)
	{
		if (_truncationLogged) return;
		_truncationLogged = true;
		_logger.LogError("A snapshot of {Slots} networked entities does not fit in {Parts} packets of {Mtu} bytes and was not sent; replicate fewer entities, use an interest policy, or raise Ion:Network:MtuBytes.", slots, Protocol.MaxParts, _mtu);
	}

	internal void Malformed(PeerState state)
	{
		_stats.Malformed++;
		if (IsServer) Violation(state);
	}

	internal void Rejected(PeerState state)
	{
		_stats.Rejected++;
		Violation(state);
	}

	private void Violation(PeerState state)
	{
		if (!IsServer || state.Stage == PeerStage.Closing) return;
		state.Violations++;
		if (state.Violations > _config.MaxViolations)
		{
			_logger.LogWarning("Disconnecting {Peer}: {Count} protocol violations.", state.Peer, state.Violations);
			SendControl(state, PacketKind.Disconnect, (byte)DisconnectReason.Violations);
			BeginClose(state, DisconnectReason.Violations);
		}
	}

	// Connections ----------------------------------------------------------------------------------------------------------

	/// <inheritdoc/>
	public void Disconnect(NetworkPeer peer, DisconnectReason reason = DisconnectReason.Kicked)
	{
		if (IsClient)
		{
			var server = _byPeer[0];
			if (server is null) return;
			SendControl(server, PacketKind.Disconnect, (byte)reason);
			_transport?.Flush();
			server.Reason = reason;
			_reason = reason;
			_transport?.Disconnect(server.Connection);
			RemoveState(server, reason);
			OnClientLost(reason);
			return;
		}

		var state = _byPeer[peer.Id];
		if (state is null || peer.IsServer) return;
		SendControl(state, PacketKind.Disconnect, (byte)reason);
		BeginClose(state, reason);
	}

	/// <summary>Stops talking to a peer: it leaves the peer list now, and its connection closes shortly (so a queued reject or disconnect reaches it).</summary>
	private void BeginClose(PeerState state, DisconnectReason reason)
	{
		if (state.Stage == PeerStage.Closing) return;
		var wasConnected = state.Stage == PeerStage.Connected;
		state.Stage = PeerStage.Closing;
		state.Reason = reason;
		state.CloseAt = _clock.Elapsed + TimeSpan.FromMilliseconds(250);
		if (wasConnected) Forget(state, reason);
	}

	private void Forget(PeerState state, DisconnectReason reason)
	{
		if (!state.Peer.IsClient || _byPeer[state.Peer.Id] != state) return;
		_byPeer[state.Peer.Id] = null;
		_peers.Remove(state.Peer);
		foreach (var world in Worlds.LiveWorlds) world.RemovePeer(state);
		state.OpenWorlds.Clear();
		_events.Emit(new PeerDisconnected(state.Peer, reason));
		_logger.LogInformation("Client {Peer} left: {Reason}.", state.Peer, reason);
	}

	private void RemoveState(PeerState state, DisconnectReason reason)
	{
		if (IsServer && state.Stage == PeerStage.Connected) Forget(state, reason);
		if (IsClient && _byPeer[0] == state)
		{
			_byPeer[0] = null;
			_peers.Remove(NetworkPeer.Server);
		}

		_states.Remove(state);
		SetConnection(state.Connection, null);
	}

	private void CheckTimeouts()
	{
		var now = _clock.Elapsed;
		for (var i = _states.Count - 1; i >= 0; i--)
		{
			var state = _states[i];
			switch (state.Stage)
			{
				case PeerStage.Closing when now >= state.CloseAt:
					_transport!.Disconnect(state.Connection);
					RemoveState(state, state.Reason);
					if (IsClient) OnClientLost(state.Reason);
					break;
				case PeerStage.Handshaking when now - state.ConnectedAt > _config.HandshakeTimeout:
					if (IsServer) SendControl(state, PacketKind.Reject, (byte)DisconnectReason.Timeout);
					BeginClose(state, DisconnectReason.Timeout);
					break;
				case PeerStage.Connected when now - state.LastReceived > _config.IdleTimeout:
					SendControl(state, PacketKind.Disconnect, (byte)DisconnectReason.Timeout);
					BeginClose(state, DisconnectReason.Timeout);
					break;
			}
		}
	}

	private void OnClientLost(DisconnectReason reason)
	{
		if (_state == NetworkState.Disconnected) return;
		_state = NetworkState.Disconnected;
		if (_reason == DisconnectReason.None) _reason = reason;
		_events.Emit(new PeerDisconnected(NetworkPeer.Server, _reason));
	}

	private PeerState? GetConnection(int connection) => (uint)connection < (uint)_byConnection.Length ? _byConnection[connection] : null;

	private void SetConnection(int connection, PeerState? state)
	{
		if (connection < 0) return;
		if (connection >= _byConnection.Length) Array.Resize(ref _byConnection, Math.Max(_byConnection.Length * 2, connection + 1));
		_byConnection[connection] = state;
	}

	internal PeerState? StateOf(NetworkPeer peer) => _byPeer[peer.Id];
}
