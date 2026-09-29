using Arch.Core;

namespace Ion.Extensions.Networking.Tests;

/// <summary>
/// One process of a network test driven by hand, without a game loop: a world, an event bus and a session whose steps are
/// called in the order the <see cref="NetworkSystem"/> steps run in a frame (one fixed step per frame).
/// </summary>
internal sealed class NetPeer : IDisposable
{
	public NetPeer(NetHarness harness, NetworkConfig config, NetworkTypeTable? table = null)
	{
		Harness = harness;
		World = World.Create();
		Events = new EventBus();
		Transport = new LoopbackTransport(harness.Network, harness.Clock, config.Simulate);
		Session = new NetworkSession(config, Transport, World, Events, harness.Clock, harness.TickRate, "test", table: table);
	}

	public NetHarness Harness { get; }

	public World World { get; }

	public EventBus Events { get; }

	public LoopbackTransport Transport { get; }

	public NetworkSession Session { get; }

	public NetworkWorld Net => Session.World;

	/// <summary>Game code of the fixed step (between the prediction step and the capture).</summary>
	public Action<NetPeer>? FixedStep { get; set; }

	public void First()
	{
		Session.Poll();
		Session.Prediction.Reconcile();
	}

	public void Fixed()
	{
		Events.BeginFixedStep();
		Net.BeginTick();
		try
		{
			Session.Prediction.FixedStep((float)(1.0 / Harness.TickRate));
			FixedStep?.Invoke(this);
		}
		finally
		{
			Net.Capture();
			Events.EndFixedSteps();
		}
	}

	public void Last()
	{
		Session.Send();
		Events.Step();
	}

	public void Dispose()
	{
		Session.Dispose();
		World.Dispose();
	}
}

/// <summary>A server and clients on one loopback network, stepped in lockstep on one manual clock.</summary>
internal sealed class NetHarness : IDisposable
{
	private readonly List<NetPeer> _peers = [];

	public NetHarness(double tickRate = 60)
	{
		TickRate = tickRate;
		Clock = new ManualClock();
	}

	public double TickRate { get; }

	public ManualClock Clock { get; }

	public LoopbackNetwork Network { get; } = new();

	public TimeSpan FrameTime => TimeSpan.FromSeconds(1.0 / TickRate);

	public static NetworkConfig Config(NetworkMode mode, Action<NetworkConfig>? configure = null)
	{
		var config = new NetworkConfig { Mode = mode, Port = 0, TickRate = 60 };
		configure?.Invoke(config);
		return config;
	}

	public NetPeer Server(Action<NetworkConfig>? configure = null, NetworkTypeTable? table = null)
	{
		var peer = new NetPeer(this, Config(NetworkMode.Server, configure), table);
		_peers.Add(peer);
		peer.Session.Start();
		return peer;
	}

	public NetPeer Client(NetPeer server, Action<NetworkConfig>? configure = null, NetworkTypeTable? table = null)
	{
		var peer = new NetPeer(this, Config(NetworkMode.Client, c =>
		{
			c.Port = server.Transport.LocalPort;
			configure?.Invoke(c);
		}), table);
		_peers.Add(peer);
		peer.Session.Start();
		return peer;
	}

	/// <summary>Runs <paramref name="frames"/> frames of every peer: each peer's First, fixed step and Last, then the clock advances.</summary>
	public void Step(int frames = 1)
	{
		for (var i = 0; i < frames; i++)
		{
			foreach (var peer in _peers) peer.First();
			foreach (var peer in _peers) peer.Fixed();
			foreach (var peer in _peers) peer.Last();
			Clock.Advance(FrameTime);
		}
	}

	/// <summary>Steps until <paramref name="condition"/> holds; false after <paramref name="maxFrames"/>.</summary>
	public bool StepUntil(Func<bool> condition, int maxFrames = 600)
	{
		for (var i = 0; i < maxFrames; i++)
		{
			if (condition()) return true;
			Step();
		}

		return condition();
	}

	public void Dispose()
	{
		foreach (var peer in _peers) peer.Dispose();
	}
}
