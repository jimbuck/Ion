using System.Globalization;

using Arch.Core;

using Ion.Extensions.Ecs;
using Ion.Extensions.Networking;
using Ion.Testing;

using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.Net.Tests;

/// <summary>
/// A headless dedicated server and a headless client (played by <see cref="NetAutopilotSystem"/>) of the real game in one
/// process, stepped frame by frame: the client's replicated state must equal the server's at the tick of the client's
/// newest snapshot, within a few ticks of the server, all along the game.
/// </summary>
public class ConvergenceTests(ITestOutputHelper output)
{
	private static readonly QueryDescription Networked = new QueryDescription().WithAll<NetworkId>();
	private static readonly QueryDescription Balls = new QueryDescription().WithAll<Ball>();
	private static readonly QueryDescription Blocks = new QueryDescription().WithAll<Block>();

	/// <summary>How far (in ticks) the client's newest snapshot may lag the server once running.</summary>
	private const int MaxLagTicks = 12;

	internal sealed class Session : IDisposable
	{
		public required IonTestHost Server { get; init; }
		public required IonTestHost Client { get; init; }
		public Action? AfterFrame { get; init; }

		public void Step(int frames = 1)
		{
			for (var i = 0; i < frames; i++)
			{
				Server.Step();
				Client.Step();
				AfterFrame?.Invoke();
			}
		}

		public void Dispose()
		{
			Client.Dispose();
			Server.Dispose();
		}
	}

	private static IonTestHost Host(LoopbackNetwork? loopback, params string[] args) =>
		new IonTestHost().WithArgs(args).UseGame(b => BreakoutNetGame.Configure(b, loopback), a => BreakoutNetGame.Use(a));

	internal static Session Loopback(params string[] clientSimulation)
	{
		var network = new LoopbackNetwork();
		var server = Host(network, ["--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0", .. clientSimulation]).Start();
		var port = server.Get<NetworkSession>().Transport!.LocalPort.ToString(CultureInfo.InvariantCulture);
		var client = Host(network, ["--Ion:Headless=true", "--Ion:Network:Mode=Client", "--Ion:Network:Port=" + port, .. clientSimulation]).Start();
		return new Session { Server = server, Client = client };
	}

	internal static Session Udp()
	{
		var server = Host(null, "--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Bind=127.0.0.1", "--Ion:Network:Port=0", "--Ion:Network:JoinSecret=breakout").Start();
		var port = server.Get<NetworkSession>().Transport!.LocalPort.ToString(CultureInfo.InvariantCulture);
		var client = Host(null, "--Ion:Headless=true", "--Ion:Network:Mode=Client", "--Ion:Network:Connect=127.0.0.1", "--Ion:Network:Port=" + port, "--Ion:Network:JoinSecret=breakout").Start();

		// Real sockets: give LiteNetLib's threads a moment per frame, like a real frame would.
		return new Session { Server = server, Client = client, AfterFrame = () => Thread.Sleep(1) };
	}

	/// <summary>
	/// Whether the client's replicated state equals the server's at the client's newest snapshot tick: the same number of
	/// networked entities, and for each server entity alive then, the same values of every replicated component.
	/// </summary>
	internal static bool Converged(Session session, out string why)
	{
		var serverNet = session.Server.Get<NetworkWorld>();
		var clientNet = session.Client.Get<NetworkWorld>();
		var tick = clientNet.LastReceivedServerTick;
		why = "";
		if (tick == 0)
		{
			why = "no snapshot yet";
			return false;
		}

		if (serverNet.CurrentTick - tick > MaxLagTicks)
		{
			why = $"client at server tick {tick}, server at {serverNet.CurrentTick}";
			return false;
		}

		var count = serverNet.CountAtTick(tick);
		if (count < 0 || count != clientNet.CountAtTick(tick))
		{
			why = $"tick {tick}: server has {count} entities, client {clientNet.CountAtTick(tick)}";
			return false;
		}

		var serverWorld = session.Server.Get<World>();
		var mismatches = 0;
		var compared = 0;
		foreach (ref var chunk in serverWorld.Query(in Networked))
		{
			var ids = chunk.GetSpan<NetworkId>();
			for (var i = 0; i < chunk.Count; i++)
			{
				var entity = chunk.Entity(i);
				if (!serverNet.TryGetAtTick<Transform2D>(entity, tick, out var transform) && !serverNet.TryGetAtTick<Scoreboard>(entity, tick, out _)) continue;
				compared++;
				if (!clientNet.TryGetEntity(ids[i], out var mirror)
					|| !Same<Transform2D>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Ball>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Block>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Paddle>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<PaddleControl>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Scoreboard>(serverNet, clientNet, entity, mirror, tick))
				{
					mismatches++;
				}

				_ = transform;
			}
		}

		why = $"tick {tick}: {mismatches} of {compared} entities differ";
		return mismatches == 0;
	}

	private static bool Same<T>(NetworkWorld server, NetworkWorld client, Entity serverEntity, Entity clientEntity, uint tick) where T : unmanaged
	{
		var onServer = server.TryGetAtTick<T>(serverEntity, tick, out var a);
		var onClient = client.TryGetAtTick<T>(clientEntity, tick, out var b);
		if (onServer != onClient) return false;
		return !onServer || NetworkTypes<T>.Component!.Serializer.Equal(a, b);
	}

	private void Play(Session session, int frames, int convergeWithin)
	{
		var client = session.Client;
		var server = session.Server;
		Assert.True(RunUntil(session, () => client.Get<INetworkSession>().State == NetworkState.Connected, 300), "the client did not connect");

		// Play: the autopilot launches balls and follows them; the server breaks blocks. At every point the client must be
		// converged within a few ticks.
		var converged = 0;
		var longest = 0;
		var streak = 0;
		for (var frame = 0; frame < frames; frame++)
		{
			session.Step();
			if (Converged(session, out _))
			{
				converged++;
				streak = 0;
			}
			else
			{
				streak++;
				longest = Math.Max(longest, streak);
			}
		}

		var serverWorld = server.Get<World>();
		var clientWorld = client.Get<World>();
		var scoreboard = QueryScore(serverWorld);
		output.WriteLine($"{frames} frames: converged in {converged}, longest divergence {longest} frames; server tick {server.Get<INetworkWorld>().CurrentTick}, client snapshot {client.Get<INetworkWorld>().LastReceivedServerTick}; score {scoreboard.Score}, {serverWorld.CountEntities(in Balls)} balls, {serverWorld.CountEntities(in Blocks)} blocks; {client.Get<NetAutopilotSystem>().Launches} launches; corrections {client.Get<INetworkSession>().Stats.Corrections}, snapshots {client.Get<INetworkSession>().Stats.Snapshots}, {client.Get<INetworkSession>().Stats.BytesIn} bytes in");

		Assert.True(client.Get<NetAutopilotSystem>().Launches > 3);
		Assert.True(scoreboard.Score > 0, "no block broke");
		Assert.True(serverWorld.CountEntities(in Balls) > 0);
		Assert.True(longest <= convergeWithin, $"the client diverged for {longest} frames in a row");

		// The replicated world, not only the ring: same number of balls and blocks on both sides at the end.
		Assert.True(RunUntil(session, () => Converged(session, out _), convergeWithin), "not converged at the end");
		Assert.True(clientWorld.CountEntities(in Blocks) > 0);

		// The predicted paddle: with a fixed target the client's own paddle and the server's copy end at the same place.
		client.Get<NetAutopilotSystem>().Enabled = false;
		client.Get<PaddleInputSource>().TargetX = 400;
		session.Step(90);
		var id = OwnPaddle(clientWorld, client.Get<INetworkWorld>());
		Assert.True(server.Get<INetworkWorld>().TryGetEntity(id, out var serverPaddle));
		Assert.True(client.Get<INetworkWorld>().TryGetEntity(id, out var clientPaddle));
		Assert.Equal(400f, serverWorld.Get<PaddleControl>(serverPaddle).X);
		Assert.Equal(400f, clientWorld.Get<PaddleControl>(clientPaddle).X);
		Assert.Equal(client.Get<INetworkSession>().LocalPeer.Id, serverWorld.Get<Paddle>(serverPaddle).Player);
	}

	private static bool RunUntil(Session session, Func<bool> condition, int maxFrames)
	{
		for (var i = 0; i < maxFrames; i++)
		{
			if (condition()) return true;
			session.Step();
		}

		return condition();
	}

	private static Scoreboard QueryScore(World world)
	{
		var result = default(Scoreboard);
		world.Query(new QueryDescription().WithAll<Scoreboard>(), (ref Scoreboard scoreboard) => result = scoreboard);
		return result;
	}

	private static NetworkId OwnPaddle(World world, INetworkWorld network)
	{
		var result = default(NetworkId);
		world.Query(new QueryDescription().WithAll<Paddle, NetworkId>(), (ref NetworkId id) =>
		{
			if (network.IsOwned(id)) result = id;
		});
		Assert.True(result.IsValid, "the client has no paddle of its own");
		return result;
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ServerAndClientConvergeOverLoopback()
	{
		using var session = Loopback();
		Play(session, frames: 900, convergeWithin: 10);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ServerAndClientConvergeOverALossyLaggyLoopback()
	{
		using var session = Loopback(
			"--Ion:Network:Simulate:Latency=00:00:00.040",
			"--Ion:Network:Simulate:Jitter=00:00:00.010",
			"--Ion:Network:Simulate:Loss=0.05",
			"--Ion:Network:Simulate:Reorder=0.05",
			"--Ion:Network:Simulate:Seed=7");
		Play(session, frames: 900, convergeWithin: 30);
	}

	[Fact, Trait(CATEGORY, E2E)]
	public void ServerAndClientConvergeOverUdpOnLocalhost()
	{
		using var session = Udp();
		Play(session, frames: 900, convergeWithin: 30);
		Assert.Equal("litenetlib", session.Server.Get<NetworkSession>().Transport!.Name);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AListenServerPlaysOnItsOwn()
	{
		using var host = new IonTestHost().UseGame(b => BreakoutNetGame.Configure(b, new LoopbackNetwork()), a => BreakoutNetGame.Use(a));
		host.WithArgs("--Ion:Network:Port=0");
		host.Step(400);
		var session = host.Get<INetworkSession>();
		Assert.Equal(NetworkMode.ListenServer, session.Mode);
		Assert.True(host.Get<NetAutopilotSystem>().Launches > 3);
		Assert.True(QueryScore(host.Get<World>()).Score > 0);
		Assert.True(host.SpriteBatch.LastFrame.Sprites > 0);
	}
}
