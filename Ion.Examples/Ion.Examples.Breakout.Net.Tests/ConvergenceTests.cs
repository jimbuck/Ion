using System.Globalization;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Ecs;
using Ion.Extensions.Networking;
using Ion.Testing;

using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.Net.Tests;

/// <summary>
/// A headless dedicated server and a headless client (played by <see cref="NetAutopilotSystem"/>) of the real game in one
/// process, stepped frame by frame: in every networked world both have live (the root, and the level while both are in
/// it), the client's replicated state must equal the server's at the tick of the client's newest snapshot, within a few
/// ticks of the server, all along the game.
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

	/// <summary>The game's own Program.cs, on <paramref name="loopback"/> instead of UDP when given, with a short menu.</summary>
	private static IonTestHost Host(LoopbackNetwork? loopback, params string[] args)
	{
		var host = new IonTestHost().UseEntryPoint<Program>(["--Breakout:MenuSeconds=0.25", .. args]);
		if (loopback is not null) host.Configure(services => services.RemoveAll<INetworkTransport>().AddLoopbackTransport(loopback));
		return host;
	}

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
	/// Whether the client's replicated state equals the server's in the root world and, when both have it live, the level:
	/// at the client's newest snapshot tick of each, the same number of networked entities, and for each server entity
	/// alive then, the same values of every replicated component. False while only one of them is in the level.
	/// </summary>
	internal static bool Converged(Session session, out string why)
	{
		var serverWorlds = session.Server.Get<NetworkSession>().Worlds;
		var clientWorlds = session.Client.Get<NetworkSession>().Worlds;
		if (!Converged(serverWorlds.Root, clientWorlds.Root, out why)) return false;

		var serverHas = serverWorlds.TryGet("level", out var serverLevel);
		var clientHas = clientWorlds.TryGet("level", out var clientLevel);
		if (serverHas != clientHas)
		{
			why = serverHas ? "the server is in the level, the client is not" : "the client is in the level, the server is not";
			return false;
		}

		if (!serverHas) return true;
		if (!Converged((NetworkWorld)serverLevel!, (NetworkWorld)clientLevel!, out var level))
		{
			why = "level: " + level;
			return false;
		}

		why += "; level: " + level;
		return true;
	}

	private static bool Converged(NetworkWorld serverNet, NetworkWorld clientNet, out string why)
	{
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

		var serverWorld = serverNet.World;
		var mismatches = 0;
		var compared = 0;
		foreach (ref var chunk in serverWorld.Query(in Networked))
		{
			var ids = chunk.GetSpan<NetworkId>();
			for (var i = 0; i < chunk.Count; i++)
			{
				var entity = chunk.Entity(i);
				if (!serverNet.TryGetAtTick<Transform2D>(entity, tick, out _) && !serverNet.TryGetAtTick<Scoreboard>(entity, tick, out _) && !serverNet.TryGetAtTick<Stage>(entity, tick, out _)) continue;
				compared++;
				if (!clientNet.TryGetEntity(ids[i], out var mirror)
					|| !Same<Transform2D>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Ball>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Block>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Paddle>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<PaddleControl>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Scoreboard>(serverNet, clientNet, entity, mirror, tick)
					|| !Same<Stage>(serverNet, clientNet, entity, mirror, tick))
				{
					mismatches++;
				}
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

		// The menu counts down once the client joined; then both load the level (each on its own) and converge in it.
		Assert.True(RunUntil(session, () => Level(client) is not null && Level(server) is not null && Converged(session, out _), 300), "the client never got the level");

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

		var serverWorld = Level(server)!.World;
		var clientWorld = Level(client)!.World;
		var scoreboard = QueryScore(server.Get<World>());
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
		var id = OwnPaddle(clientWorld, Level(client)!);
		Assert.Equal(Level(client)!.Index, id.World);
		Assert.True(Level(server)!.TryGetEntity(id, out var serverPaddle));
		Assert.True(Level(client)!.TryGetEntity(id, out var clientPaddle));
		Assert.Equal(400f, serverWorld.Get<PaddleControl>(serverPaddle).X);
		Assert.Equal(400f, clientWorld.Get<PaddleControl>(clientPaddle).X);
		Assert.Equal(client.Get<INetworkSession>().LocalPeer.Id, serverWorld.Get<Paddle>(serverPaddle).Player);
	}

	/// <summary>The level's networked world, while the level scene is loaded.</summary>
	private static INetworkWorld? Level(IonTestHost host) => host.Get<INetworkWorlds>().TryGet("level", out var level) ? level : null;

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
	public void TheGameGoesFromTheMenuToTheLevelAndBack()
	{
		// Timed levels: two seconds each, a quarter of a second of menu in between.
		using var session = Loopback("--Breakout:LevelSeconds=2");
		var client = session.Client;
		var server = session.Server;
		var blocks = new QueryDescription().WithAll<Block>();
		var scenes = new List<GameScene>();
		var levelsWithBlocks = 0;
		var counted = false;
		for (var frame = 0; frame < 720; frame++)
		{
			session.Step();
			var scene = (GameScene)client.Get<Ion.Extensions.Scenes.SceneSystem>().CurrentSceneId;
			if (scenes.Count == 0 || scenes[^1] != scene) scenes.Add(scene);
			if (scene == GameScene.Menu) counted = false;

			// Each visit of the level gets the server's new level, converged with the server's.
			if (!counted && Level(client) is { } level && level.World.CountEntities(in blocks) > 0 && Converged(session, out _))
			{
				levelsWithBlocks++;
				counted = true;
			}
		}

		output.WriteLine(string.Join(" > ", scenes));
		Assert.Equal([GameScene.Menu, GameScene.Level, GameScene.Menu, GameScene.Level], scenes.Take(4));
		Assert.True(levelsWithBlocks >= 2, $"{levelsWithBlocks} levels seen with blocks");
		Assert.True(QueryScore(server.Get<World>()).Round >= 3);
		Assert.True(RunUntil(session, () => QueryScore(server.Get<World>()).Round == QueryScore(client.Get<World>()).Round, 30));
		Assert.Equal(0, client.Get<INetworkSession>().Stats.Malformed);
		Assert.Equal(0, server.Get<INetworkSession>().Stats.Malformed);
		Assert.Equal(0, server.Get<INetworkSession>().Stats.Rejected);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AListenServerPlaysOnItsOwn()
	{
		using var host = Host(new LoopbackNetwork(), "--Ion:Network:Port=0");
		host.Step(400);
		var session = host.Get<INetworkSession>();
		Assert.Equal(NetworkMode.ListenServer, session.Mode);
		Assert.Equal((int)GameScene.Level, host.Get<Ion.Extensions.Scenes.SceneSystem>().CurrentSceneId);
		Assert.True(host.Get<NetAutopilotSystem>().Launches > 3);
		Assert.True(QueryScore(host.Get<World>()).Score > 0);
		Assert.True(host.SpriteBatch.LastFrame.Sprites > 0);
	}
}
