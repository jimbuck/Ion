using Arch.Core;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

/// <summary>
/// Multi-world replication over the loopback: named worlds besides the root, opened and closed by each client on its own
/// (as its scenes load and unload), never tracked by the server.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public class MultiWorldTests
{
	private static readonly string[] Names = ["level", "menu"];
	// Health, not the interpolated Position: these peers run no Render step, where interpolated values are written.
	private static readonly QueryDescription Healths = new QueryDescription().WithAll<NetworkId, Health>();

	private static (NetHarness Net, NetPeer Server) Start()
	{
		var net = new NetHarness();
		return (net, net.Server(worlds: Names));
	}

	private static NetPeer Join(NetHarness net, NetPeer server)
	{
		var client = net.Client(server, worlds: Names);
		Assert.True(net.StepUntil(() => client.Session.IsActive, 60));
		return client;
	}

	private static void Populate(World world, int count, int max)
	{
		for (var i = 0; i < count; i++) world.Create(new Health(i, max));
	}

	private static void Hurt(World world, int amount) => world.Query(in Healths, (ref NetworkId _, ref Health health) => health.Current += amount);

	/// <summary>The healths of the replicated entities of <paramref name="world"/>, sorted.</summary>
	private static List<Health> Snapshot(World world)
	{
		var values = new List<Health>();
		world.Query(in Healths, (ref NetworkId _, ref Health health) => values.Add(health));
		values.Sort((a, b) => a.Max != b.Max ? a.Max.CompareTo(b.Max) : a.Current.CompareTo(b.Current));
		return values;
	}

	[Fact]
	public void NamesAreNumberedInOrderAndPartOfTheHandshake()
	{
		using var net = new NetHarness();
		var server = net.Server(worlds: ["menu", "level"]);
		Assert.Equal(["level", "menu"], server.Session.Worlds.Names);
		Assert.Equal(1, server.Session.Worlds.IndexOf("level"));
		Assert.Equal(2, server.Session.Worlds.IndexOf("menu"));

		// A client with another set of worlds is refused like one with other types.
		var other = net.Client(server, worlds: ["level"]);
		net.Step(30);
		Assert.Equal(NetworkState.Disconnected, other.Session.State);
		Assert.Equal(DisconnectReason.RegistryMismatch, other.Session.DisconnectReason);

		var same = net.Client(server, worlds: ["level", "menu"]);
		Assert.True(net.StepUntil(() => same.Session.IsActive, 60));

		// Started sessions cannot learn new names: the handshake already used the set.
		Assert.Throws<InvalidOperationException>(() => server.Session.Worlds.Register("lobby"));
		server.Session.Worlds.Register("level");
	}

	[Fact]
	public void ANamedWorldGoesOnlyToTheClientsThatOpenIt()
	{
		var (net, server) = Start();
		using var harness = net;
		var inLevel = Join(net, server);
		var inMenu = Join(net, server);

		Populate(server.World, 3, 0);
		using var serverLevelWorld = World.Create();
		var serverLevel = server.Session.Worlds.Attach("level", serverLevelWorld);
		Populate(serverLevelWorld, 5, 100);

		using var levelWorld = World.Create();
		var level = inLevel.Session.Worlds.Attach("level", levelWorld);
		net.Step(20);

		// Both clients have the root world; only the one that opened the level has the level, in its own world.
		Assert.Equal(Snapshot(server.World), Snapshot(inLevel.World));
		Assert.Equal(Snapshot(server.World), Snapshot(inMenu.World));
		Assert.Equal(Snapshot(serverLevelWorld), Snapshot(levelWorld));
		Assert.True(serverLevel.HasPeer(inLevel.Session.LocalPeer));
		Assert.False(serverLevel.HasPeer(inMenu.Session.LocalPeer));

		// Ids carry the world: unique across worlds, and resolved by INetworkWorlds in the right one.
		levelWorld.Query(in Healths, (Entity entity, ref NetworkId id, ref Health health) =>
		{
			Assert.Equal(1, id.World);
			Assert.True(inLevel.Session.Worlds.TryGetEntity(id, out var found, out var same));
			Assert.Same(levelWorld, found);
			Assert.Equal(entity, same);
			Assert.False(inLevel.Net.TryGetEntity(id, out _));
		});
		Assert.Equal(1, level.Index);
		Assert.Equal("level", level.Name);

		// Changes keep flowing as deltas against the world's own acknowledgments.
		var full = server.Session.Stats.FullSnapshots;
		Hurt(serverLevelWorld, 1000);
		net.Step(10);
		Assert.Equal(Snapshot(serverLevelWorld), Snapshot(levelWorld));
		Assert.Equal(full, server.Session.Stats.FullSnapshots);
		Assert.True(serverLevel.LastAckedTick(inLevel.Session.LocalPeer) > 0);
	}

	[Fact]
	public void AClientMovesBetweenWorldsAcrossASceneChange()
	{
		var (net, server) = Start();
		using var harness = net;
		var client = Join(net, server);

		using var serverMenuWorld = World.Create();
		using var serverLevelWorld = World.Create();
		var serverMenu = server.Session.Worlds.Attach("menu", serverMenuWorld);
		var serverLevel = server.Session.Worlds.Attach("level", serverLevelWorld);
		Populate(serverMenuWorld, 2, 10);
		Populate(serverLevelWorld, 4, 20);

		var menuWorld = World.Create();
		var menu = client.Session.Worlds.Attach("menu", menuWorld);
		net.Step(15);
		Assert.Equal(Snapshot(serverMenuWorld), Snapshot(menuWorld));

		// A fade: the new scene's world is open while the old one is still live, and both replicate.
		using var levelWorld = World.Create();
		client.Session.Worlds.Attach("level", levelWorld);
		net.Step(15);
		Assert.Equal(Snapshot(serverMenuWorld), Snapshot(menuWorld));
		Assert.Equal(Snapshot(serverLevelWorld), Snapshot(levelWorld));

		// The old scene unloads: its world goes, and the server stops sending it to this client (and only that).
		client.Session.Worlds.Detach(menu);
		menuWorld.Dispose();
		var bytes = client.Session.Stats.BytesIn;
		net.Step(15);
		Assert.False(serverMenu.HasPeer(client.Session.LocalPeer));
		Assert.True(serverLevel.HasPeer(client.Session.LocalPeer));
		Assert.False(client.Session.Worlds.TryGet("menu", out INetworkWorld? _));

		// The level keeps replicating.
		Hurt(serverLevelWorld, 1);
		net.Step(10);
		Assert.Equal(Snapshot(serverLevelWorld), Snapshot(levelWorld));
		Assert.True(client.Session.Stats.BytesIn > bytes);
		Assert.Equal(0, client.Session.Stats.Malformed);
		Assert.Equal(0, server.Session.Stats.Malformed);
	}

	[Fact]
	public void AWorldTheServerDoesNotHaveStaysEmptyUntilItDoes()
	{
		var (net, server) = Start();
		using var harness = net;
		var client = Join(net, server);
		var unavailable = client.Events.Reader<NetworkWorldUnavailable>();

		using var levelWorld = World.Create();
		var level = client.Session.Worlds.Attach("level", levelWorld);
		var missing = default(NetworkWorldUnavailable);
		Assert.True(net.StepUntil(() => unavailable.TryRead(out missing), 30));
		Assert.Equal(level.Index, missing.World);
		Assert.Equal(0, levelWorld.Size);

		// The server loads the level later: the open was remembered, so the client gets it without asking again.
		var serverLevelWorld = World.Create();
		var serverLevel = server.Session.Worlds.Attach("level", serverLevelWorld);
		Populate(serverLevelWorld, 3, 5);
		net.Step(15);
		Assert.Equal(Snapshot(serverLevelWorld), Snapshot(levelWorld));

		// The server unloads it: the client is told, and its replicated entities go.
		server.Session.Worlds.Detach(serverLevel);
		serverLevelWorld.Dispose();
		Assert.True(net.StepUntil(() => unavailable.TryRead(out _), 30));
		Assert.Empty(Snapshot(levelWorld));
		// The client's world is still live (its scene is loaded): it fills again when the server has the level again.
		Assert.True(client.Session.Worlds.TryGet("level", out var live) && ReferenceEquals(live, level));
	}

	[Fact]
	public void IdsReusedAfterAWorldCloseAndReopenDoNotMixUp()
	{
		var (net, server) = Start();
		using var harness = net;
		var client = Join(net, server);

		var first = World.Create();
		var serverLevel = server.Session.Worlds.Attach("level", first);
		Populate(first, 6, 1);
		var levelWorld = World.Create();
		var level = client.Session.Worlds.Attach("level", levelWorld);
		net.Step(15);
		Assert.Equal(6, Snapshot(levelWorld).Count);

		// The server reloads the level: a new world whose entities get the same slots and generations as before.
		server.Session.Worlds.Detach(serverLevel);
		first.Dispose();
		using var second = World.Create();
		server.Session.Worlds.Attach("level", second);
		Populate(second, 4, 2);
		net.Step(20);
		Assert.Equal(Snapshot(second), Snapshot(levelWorld));

		// The client reloads its level scene: a new open (a new epoch), whatever of the old one is still in flight.
		client.Session.Worlds.Detach(level);
		levelWorld.Dispose();
		using var reopened = World.Create();
		client.Session.Worlds.Attach("level", reopened);
		Hurt(second, 50);
		net.Step(20);
		Assert.Equal(Snapshot(second), Snapshot(reopened));
		Assert.Equal(0, client.Session.Stats.Malformed);
	}

	[Fact]
	public void PredictionRunsOnTheOwnedEntitiesOfEveryLiveWorld()
	{
		var (net, server) = Start();
		using var harness = net;
		var client = Join(net, server);
		var input = new Vector2(1, 0);
		static void Move(ref Avatar avatar, in MoveInput input, float delta) => avatar.Position += input.Move;
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		client.Session.Prediction.Register<Avatar, MoveInput>(Move, () => new MoveInput(input));

		using var serverLevelWorld = World.Create();
		var serverLevel = server.Session.Worlds.Attach("level", serverLevelWorld);
		var avatar = TestWorlds.CreateAvatar(serverLevelWorld, serverLevel.Allocate(client.Session.LocalPeer));
		using var levelWorld = World.Create();
		var level = client.Session.Worlds.Attach("level", levelWorld);
		var id = serverLevelWorld.Get<NetworkId>(avatar);
		Assert.Equal(serverLevel.Index, id.World);
		Assert.True(net.StepUntil(() => level.TryGetEntity(id, out var _), 60));
		net.Step(30);
		input = Vector2.Zero;
		net.Step(30);

		Assert.True(level.TryGetEntity(id, out var mirror));
		var authoritative = serverLevelWorld.Get<Avatar>(avatar).Position;
		Assert.True(authoritative.X > 20, $"{authoritative}");
		Assert.Equal(authoritative, levelWorld.Get<Avatar>(mirror).Position);
		Assert.True(client.Session.Stats.Corrections <= 1, $"{client.Session.Stats.Corrections} corrections");
	}
}
