using Arch.Core;

using Ion.Extensions.Networking;

namespace Ion.Benchmarks;

[Replicated, Interpolated]
public record struct NetBenchPosition(Vector2 Value, float Rotation);

[Replicated]
public record struct NetBenchHealth(int Current, int Max);

[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct NetBenchInput(Vector2 Move, int Buttons);

/// <summary>A server and a client session on the loopback transport, driven by hand (no game loop).</summary>
internal sealed class NetBenchPair : IDisposable
{
	public readonly ManualClock Clock = new();
	public readonly World ServerWorld = World.Create();
	public readonly World ClientWorld = World.Create();
	public readonly EventBus ServerEvents = new();
	public readonly EventBus ClientEvents = new();
	public readonly NetworkSession Server;
	public readonly NetworkSession Client;
	public readonly List<Entity> Entities = [];

	public NetBenchPair(int entities, bool withClient = true)
	{
		var network = new LoopbackNetwork();
		Server = new NetworkSession(new NetworkConfig { Mode = NetworkMode.Server, Port = 0, TickRate = 60, MaxMessagesPerSecond = int.MaxValue, MaxBytesPerSecond = int.MaxValue },
			new LoopbackTransport(network, Clock), ServerWorld, ServerEvents, Clock, 60, "bench");
		Server.Start();
		var port = Server.Transport!.LocalPort;
		Client = new NetworkSession(new NetworkConfig { Mode = NetworkMode.Client, Port = port, TickRate = 60 },
			new LoopbackTransport(network, Clock), ClientWorld, ClientEvents, Clock, 60, "bench");
		if (withClient) Client.Start();

		for (var i = 0; i < entities; i++)
		{
			Entities.Add(ServerWorld.Create(new NetBenchPosition(new Vector2(i, i), 0), new NetBenchHealth(100, 100)));
		}

		for (var i = 0; i < 30; i++) Frame();
	}

	public void ServerTick()
	{
		Server.World.BeginTick();
		Server.World.Capture();
	}

	public void Frame()
	{
		Server.Poll();
		Client.Poll();
		ServerTick();
		Client.World.BeginTick();
		Server.Send();
		Client.Send();
		ServerEvents.Step();
		ClientEvents.Step();
		Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
	}

	/// <summary>Moves every <paramref name="stride"/>-th entity.</summary>
	public void Move(int stride)
	{
		for (var i = 0; i < Entities.Count; i += stride) ServerWorld.Get<NetBenchPosition>(Entities[i]).Value += Vector2.One;
	}

	public void Dispose()
	{
		Client.Dispose();
		Server.Dispose();
		World.Destroy(ClientWorld);
		World.Destroy(ServerWorld);
	}
}

/// <summary>
/// Stage 6b: the server's per-tick snapshot capture (the FixedUpdate End scope): every replicated component of every
/// networked entity copied into the ring by dense slot, one chunk loop per replicated type.
/// </summary>
[MemoryDiagnoser]
public class NetworkSnapshotCaptureBenchmarks
{
	private NetBenchPair _pair = null!;

	[Params(1_000, 10_000)]
	public int Entities { get; set; }

	[GlobalSetup]
	public void Setup() => _pair = new NetBenchPair(Entities, withClient: false);

	[GlobalCleanup]
	public void Cleanup() => _pair.Dispose();

	/// <summary>One tick: the tick scope's begin and end (the capture), nothing moving.</summary>
	[Benchmark]
	public void Capture() => _pair.ServerTick();
}

/// <summary>
/// Stage 6b: delta encoding. The generated field-wise delta serializer on 10,000 component values (one member changed
/// each), and a whole snapshot round trip of 10,000 networked entities between a server and a client on the loopback
/// transport with 1 percent and 10 percent of them moving: capture, delta encoding against the acknowledged tick,
/// packetizing, decoding and applying to the client's world.
/// </summary>
[MemoryDiagnoser]
public class NetworkDeltaBenchmarks
{
	private const int Values = 10_000;
	private NetSerializer<NetBenchPosition> _serializer = null!;
	private NetBenchPosition[] _before = null!;
	private NetBenchPosition[] _after = null!;
	private byte[] _buffer = null!;
	private int _written;
	private NetBenchPair _pair = null!;

	[Params(1, 10)]
	public int PercentMoving { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_serializer = NetworkTypes<NetBenchPosition>.Component!.Serializer;
		_before = new NetBenchPosition[Values];
		_after = new NetBenchPosition[Values];
		for (var i = 0; i < Values; i++)
		{
			_before[i] = new NetBenchPosition(new Vector2(i, i), 1);
			_after[i] = _before[i] with { Value = _before[i].Value + Vector2.One };
		}

		_buffer = new byte[Values * 16];
		EncodeDelta10k();
		_pair = new NetBenchPair(Values);
	}

	[GlobalCleanup]
	public void Cleanup() => _pair.Dispose();

	[Benchmark]
	public int EncodeDelta10k()
	{
		var writer = new NetWriter(_buffer);
		for (var i = 0; i < Values; i++) _serializer.WriteDelta(ref writer, _before[i], _after[i]);
		_written = writer.Position;
		return _written;
	}

	[Benchmark]
	public float DecodeDelta10k()
	{
		var reader = new NetReader(_buffer.AsSpan(0, _written));
		var sum = 0f;
		for (var i = 0; i < Values; i++) sum += _serializer.ReadDelta(ref reader, _before[i]).Value.X;
		return sum;
	}

	/// <summary>One frame of a server and a client with 10,000 networked entities, some moving.</summary>
	[Benchmark]
	public void SnapshotRoundTrip10k()
	{
		_pair.Move(100 / PercentMoving);
		_pair.Frame();
	}
}

/// <summary>
/// Stage 6b: typed messages. 100 client-to-server messages per frame: serialized and packed on the client, sent through
/// the loopback transport, decoded on the server with the authority and rate checks, dispatched onto the event bus, and
/// read with a <see cref="NetworkReader{T}"/>.
/// </summary>
[MemoryDiagnoser]
public class NetworkMessageBenchmarks
{
	private NetBenchPair _pair = null!;
	private NetworkReader<NetBenchInput> _reader;

	[GlobalSetup]
	public void Setup()
	{
		_pair = new NetBenchPair(0);
		_reader = _pair.Server.Reader<NetBenchInput>();
		for (var i = 0; i < 100; i++) Dispatch100();
	}

	[GlobalCleanup]
	public void Cleanup() => _pair.Dispose();

	[Benchmark]
	public int Dispatch100()
	{
		for (var i = 0; i < 100; i++) _pair.Client.SendToServer(new NetBenchInput(new Vector2(i, 1), i));
		_pair.Frame();
		var read = 0;
		while (_reader.TryRead(out _, out _)) read++;
		return read;
	}
}
