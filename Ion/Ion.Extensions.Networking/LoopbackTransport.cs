using System.Buffers;

namespace Ion.Extensions.Networking;

/// <summary>
/// An in-process network that <see cref="LoopbackTransport"/>s connect through: servers listen on a port number, clients
/// connect to it. Several <c>IonTestHost</c>s (a server and any number of clients) share one instance and are stepped in
/// the same thread; nothing touches a socket.
/// </summary>
public sealed class LoopbackNetwork
{
	private readonly Lock _gate = new();
	private readonly Dictionary<int, LoopbackTransport> _servers = [];
	private int _nextPort = 40000;
	private int _endpoints;

	// Packet buffers of up to SmallPacket bytes (nearly all packets), shared by the transports of the network (a sender
	// takes one, the receiver gives it back), so steady traffic does not allocate; larger ones are rented.
	private const int SmallPacket = 2048;
	private readonly Stack<byte[]> _buffers = new();

	internal byte[] Rent(int length)
	{
		if (length > SmallPacket) return ArrayPool<byte>.Shared.Rent(length);
		lock (_gate) return _buffers.TryPop(out var buffer) ? buffer : new byte[SmallPacket];
	}

	internal void Return(byte[] buffer)
	{
		if (buffer.Length != SmallPacket)
		{
			ArrayPool<byte>.Shared.Return(buffer);
			return;
		}

		lock (_gate) _buffers.Push(buffer);
	}

	/// <summary>The network used by <c>AddLoopbackTransport()</c> without an explicit one.</summary>
	public static LoopbackNetwork Shared { get; } = new();

	internal int NextEndpoint()
	{
		lock (_gate) return ++_endpoints;
	}

	internal int Listen(LoopbackTransport server, int port)
	{
		lock (_gate)
		{
			if (port == 0)
			{
				while (_servers.ContainsKey(_nextPort)) _nextPort++;
				port = _nextPort++;
			}

			if (!_servers.TryAdd(port, server)) throw new InvalidOperationException($"Loopback port {port} is already in use.");
			return port;
		}
	}

	internal void Unlisten(LoopbackTransport server, int port)
	{
		lock (_gate)
		{
			if (_servers.TryGetValue(port, out var existing) && existing == server) _servers.Remove(port);
		}
	}

	internal LoopbackTransport? Find(int port)
	{
		lock (_gate) return _servers.GetValueOrDefault(port);
	}
}

/// <summary>
/// A deterministic in-process <see cref="INetworkTransport"/> with a simulated network (<see cref="NetworkSimulation"/>:
/// latency, jitter, loss and reordering, from a seeded random generator). Packets are stamped with the sender's
/// <see cref="IClock"/> time and delivered when the receiver's clock reaches the stamp plus the simulated delay, so hosts
/// stepped in lockstep with fixed-step clocks exchange packets reproducibly.
/// </summary>
/// <remarks>
/// Reliable deliveries are never dropped, and <see cref="Delivery.ReliableOrdered"/> packets never overtake each other;
/// <see cref="Delivery.Sequenced"/> packets older than one already delivered are dropped; only
/// <see cref="Delivery.Unreliable"/> packets are reordered. Buffers are pooled, so the send and receive paths do not
/// allocate in steady state. Single-threaded: every transport of a network must be driven from the same thread.
/// </remarks>
public sealed class LoopbackTransport : INetworkTransport
{
	/// <summary>The largest packet the loopback carries.</summary>
	public const int MaxPacket = 16 * 1024;

	private readonly LoopbackNetwork _network;
	private readonly IClock _clock;
	private readonly NetworkSimulation _simulation;
	private readonly Random _random;
	private readonly List<InFlight> _inbox = [];
	private Link?[] _links = new Link?[8];
	private long _sequence;
	private int _port;
	private bool _server;
	private bool _running;

	/// <summary>Creates a transport on <paramref name="network"/> timed by <paramref name="clock"/>, simulating <paramref name="simulation"/> on the packets it sends.</summary>
	public LoopbackTransport(LoopbackNetwork network, IClock clock, NetworkSimulation? simulation = null)
	{
		ArgumentNullException.ThrowIfNull(network);
		ArgumentNullException.ThrowIfNull(clock);
		_network = network;
		_clock = clock;
		_simulation = simulation ?? new NetworkSimulation();
		_random = new Random(unchecked(_simulation.Seed * 7919 + network.NextEndpoint() * 104729));
	}

	/// <inheritdoc/>
	public string Name => "loopback";

	/// <inheritdoc/>
	public bool IsRunning => _running;

	/// <inheritdoc/>
	public int LocalPort => _running ? _port : 0;

	/// <summary>Packets dropped by the simulated loss (sent by this transport).</summary>
	public long Dropped { get; private set; }

	/// <summary>Packets held back by the simulated reordering (sent by this transport).</summary>
	public long Reordered { get; private set; }

	/// <inheritdoc/>
	public void StartServer(string bindAddress, int port, int maxConnections)
	{
		ThrowIfRunning();
		_port = _network.Listen(this, port);
		_server = true;
		_running = true;
	}

	/// <inheritdoc/>
	public void StartClient(string address, int port)
	{
		ThrowIfRunning();
		_server = false;
		_running = true;
		_port = port;
		var server = _network.Find(port);
		var now = _clock.Elapsed;
		if (server is null)
		{
			// Nobody listens: the connection attempt fails after one latency.
			Enqueue(new InFlight(now + Delay(), NextSequence(), 0, TransportEventKind.Disconnected, null, 0, 0));
			return;
		}

		var remote = server.Accept(this);
		SetLink(0, new Link(server, remote));
		var at = now + Delay();
		Enqueue(new InFlight(at, NextSequence(), 0, TransportEventKind.Connected, null, 0, 0));
		server.Enqueue(new InFlight(at, server.NextSequence(), remote, TransportEventKind.Connected, null, 0, 0));
	}

	private int Accept(LoopbackTransport client)
	{
		var connection = 1;
		while (connection < _links.Length && _links[connection] is not null) connection++;
		SetLink(connection, new Link(client, 0));
		return connection;
	}

	/// <inheritdoc/>
	public int MaxPacketSize(Delivery delivery) => MaxPacket;

	/// <inheritdoc/>
	public void Send(int connection, ReadOnlySpan<byte> packet, Delivery delivery)
	{
		if (!_running || packet.Length > MaxPacket) return;
		var link = GetLink(connection);
		if (link is null || !link.Open) return;

		var at = _clock.Elapsed + Delay();
		if (delivery is Delivery.Unreliable or Delivery.Sequenced && _simulation.Loss > 0 && _random.NextDouble() < _simulation.Loss)
		{
			Dropped++;
			return;
		}

		if (delivery == Delivery.Unreliable && _simulation.Reorder > 0 && _random.NextDouble() < _simulation.Reorder)
		{
			at += _simulation.Latency > TimeSpan.Zero ? _simulation.Latency : TimeSpan.FromMilliseconds(1);
			Reordered++;
		}

		if (delivery == Delivery.ReliableOrdered)
		{
			// Never before an earlier ordered packet of the same link.
			if (at < link.LastOrdered) at = link.LastOrdered;
			link.LastOrdered = at;
		}

		var data = _network.Rent(packet.Length);
		packet.CopyTo(data);
		var order = delivery == Delivery.Sequenced ? ++link.SequencedSent : 0;
		link.Remote.Enqueue(new InFlight(at, link.Remote.NextSequence(), link.RemoteConnection, TransportEventKind.Data, data, packet.Length, order));
	}

	/// <inheritdoc/>
	public void Poll()
	{
	}

	/// <inheritdoc/>
	public bool TryReceive(Span<byte> buffer, out TransportEvent e)
	{
		var now = _clock.Elapsed;
		while (_inbox.Count > 0)
		{
			var next = _inbox[0];
			if (next.DeliverAt > now) break;
			_inbox.RemoveAt(0);

			var link = GetLink(next.Connection);
			switch (next.Kind)
			{
				case TransportEventKind.Data:
				{
					var data = next.Data!;
					var deliver = link is not null && link.Open && next.Length <= buffer.Length;
					if (deliver && next.Order != 0)
					{
						// Sequenced: an older packet than one already delivered is dropped.
						if (next.Order <= link!.SequencedReceived) deliver = false;
						else link.SequencedReceived = next.Order;
					}

					if (deliver) data.AsSpan(0, next.Length).CopyTo(buffer);
					_network.Return(data);
					if (!deliver) continue;
					e = new TransportEvent(TransportEventKind.Data, next.Connection, next.Length);
					return true;
				}

				case TransportEventKind.Connected:
					if (link is null || !link.Open) continue;
					e = new TransportEvent(TransportEventKind.Connected, next.Connection, 0);
					return true;

				case TransportEventKind.Disconnected:
					if (link is not null) SetLink(next.Connection, null);
					else if (_server) continue;
					e = new TransportEvent(TransportEventKind.Disconnected, next.Connection, 0);
					return true;
			}
		}

		e = default;
		return false;
	}

	/// <inheritdoc/>
	public void Flush()
	{
	}

	/// <inheritdoc/>
	public void Disconnect(int connection)
	{
		var link = GetLink(connection);
		if (link is null || !link.Open) return;
		link.Open = false;
		var at = _clock.Elapsed + Delay();
		if (at < link.LastOrdered) at = link.LastOrdered;
		link.Remote.Enqueue(new InFlight(at, link.Remote.NextSequence(), link.RemoteConnection, TransportEventKind.Disconnected, null, 0, 0));
		SetLink(connection, null);
	}

	/// <inheritdoc/>
	public void Stop()
	{
		if (!_running) return;
		for (var i = 0; i < _links.Length; i++)
		{
			if (_links[i] is not null) Disconnect(i);
		}

		foreach (var packet in _inbox)
		{
			if (packet.Data is not null) _network.Return(packet.Data);
		}

		_inbox.Clear();
		if (_server) _network.Unlisten(this, _port);
		_running = false;
	}

	/// <inheritdoc/>
	public void Dispose() => Stop();

	private void ThrowIfRunning()
	{
		if (_running) throw new InvalidOperationException("The transport is already started.");
	}

	private TimeSpan Delay()
	{
		var delay = _simulation.Latency;
		if (_simulation.Jitter > TimeSpan.Zero) delay += TimeSpan.FromTicks((long)(_random.NextDouble() * _simulation.Jitter.Ticks));
		return delay;
	}

	private long NextSequence() => ++_sequence;

	private void Enqueue(in InFlight packet)
	{
		// Sorted by delivery time, then by arrival order (the list is short: what is in flight).
		var index = _inbox.Count;
		while (index > 0 && Compare(_inbox[index - 1], packet) > 0) index--;
		_inbox.Insert(index, packet);
	}

	private static int Compare(in InFlight a, in InFlight b)
	{
		var time = a.DeliverAt.CompareTo(b.DeliverAt);
		return time != 0 ? time : a.Sequence.CompareTo(b.Sequence);
	}

	private Link? GetLink(int connection) => (uint)connection < (uint)_links.Length ? _links[connection] : null;

	private void SetLink(int connection, Link? link)
	{
		if (connection >= _links.Length) Array.Resize(ref _links, Math.Max(_links.Length * 2, connection + 1));
		_links[connection] = link;
	}

	private readonly record struct InFlight(TimeSpan DeliverAt, long Sequence, int Connection, TransportEventKind Kind, byte[]? Data, int Length, long Order);

	private sealed class Link(LoopbackTransport remote, int remoteConnection)
	{
		public readonly LoopbackTransport Remote = remote;
		public readonly int RemoteConnection = remoteConnection;
		public bool Open = true;
		public TimeSpan LastOrdered;
		public long SequencedSent;
		public long SequencedReceived;
	}
}
