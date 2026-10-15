using Arch.Core;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, INTEGRATION)]
public class ReplicationTests
{
	private static readonly QueryDescription Networked = new QueryDescription().WithAll<NetworkId>();

	internal static bool SameState(NetPeer server, NetPeer client, uint tick)
	{
		var serverFrame = server.Net.Frame(tick);
		var clientFrame = client.Net.Frame(tick);
		if (serverFrame is null || clientFrame is null) return false;
		var slots = Math.Max(serverFrame.SlotCount, clientFrame.SlotCount);
		for (var slot = 0; slot < slots; slot++)
		{
			var alive = serverFrame.IsAlive(slot);
			if (alive != clientFrame.IsAlive(slot)) return false;
			if (!alive) continue;
			if (serverFrame.Ids[slot] != clientFrame.Ids[slot] || serverFrame.Owners[slot] != clientFrame.Owners[slot]) return false;
			for (var c = 0; c < serverFrame.Columns.Length; c++)
			{
				var a = serverFrame.Columns[c];
				var b = clientFrame.Columns[c];
				if (a.Has(slot) != b.Has(slot)) return false;
				if (a.Has(slot) && !a.SameAs(b, slot)) return false;
			}
		}

		return true;
	}

	private static int Count(NetPeer peer) => peer.World.CountEntities(in Networked);

	[Fact]
	public void SpawnsUpdatesAndDespawnsReachTheClient()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var client = net.Client(server);
		Assert.True(net.StepUntil(() => client.Session.State == NetworkState.Connected, 30));

		var entities = new List<Entity>();
		for (var i = 0; i < 20; i++) entities.Add(TestWorlds.CreateAll(server.World, i));
		var spawned = client.Events.Reader<NetworkEntitySpawned>();

		Assert.True(net.StepUntil(() => Count(client) == 20, 30));
		Assert.Equal(20, spawned.Count);
		Assert.True(SameState(server, client, client.Net.LastReceivedServerTick));

		// Every entity got a server-owned id; the client has the same values.
		foreach (var entity in entities)
		{
			var id = server.World.Get<NetworkId>(entity);
			Assert.Equal(0, id.OwnerPeer);
			Assert.True(client.Net.TryGetEntity(id, out var mirror));
			Assert.Equal(server.World.Get<Health>(entity), client.World.Get<Health>(mirror));
			Assert.Equal(server.World.Get<Profile>(entity) with { LocalOnly = 0 }, client.World.Get<Profile>(mirror));
		}

		// Updates: after a change the next snapshots carry only deltas.
		server.World.Get<Health>(entities[3]).Current = 1;
		net.Step(3);
		var id3 = server.World.Get<NetworkId>(entities[3]);
		Assert.True(client.Net.TryGetEntity(id3, out var mirror3));
		Assert.Equal(1, client.World.Get<Health>(mirror3).Current);

		// Removing a component removes it on the client; destroying the entity despawns it.
		server.World.Remove<Health>(entities[4]);
		server.World.Destroy(entities[5]);
		var despawned = 0;
		var despawnReader = client.Events.Reader<NetworkEntityDespawned>();
		for (var i = 0; i < 3; i++)
		{
			net.Step();
			despawned += despawnReader.Read().Length;
		}

		Assert.True(client.Net.TryGetEntity(server.World.Get<NetworkId>(entities[4]), out var mirror4));
		Assert.False(client.World.Has<Health>(mirror4));
		Assert.Equal(19, Count(client));
		Assert.Equal(1, despawned);
		Assert.True(SameState(server, client, client.Net.LastReceivedServerTick));
	}

	[Fact]
	public void SteadyStateSnapshotsAreDeltasAgainstTheAcknowledgedTick()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var client = net.Client(server);
		net.StepUntil(() => client.Session.State == NetworkState.Connected, 30);
		for (var i = 0; i < 50; i++) TestWorlds.CreateAll(server.World, i);
		net.Step(10);

		var full = server.Session.Stats.FullSnapshots;
		var before = server.Session.Stats.BytesOut;
		net.Step(10);

		// Nothing changed: every snapshot is a delta (no full one) and nearly empty.
		Assert.Equal(full, server.Session.Stats.FullSnapshots);
		Assert.True(server.Session.Stats.LastSnapshotBytes < 32, $"{server.Session.Stats.LastSnapshotBytes} bytes");
		Assert.True(server.Session.Stats.BytesOut - before < 10 * 200, $"{server.Session.Stats.BytesOut - before} bytes in 10 frames");
	}

	[Theory]
	[InlineData(0.0, 0.0, 0, 0)]
	[InlineData(0.2, 0.2, 50, 20)]
	[InlineData(0.4, 0.5, 80, 40)]
	public void TheClientConvergesUnderLatencyLossAndReordering(double loss, double reorder, int latencyMs, int jitterMs)
	{
		using var net = new NetHarness();
		void Simulate(NetworkConfig c)
		{
			c.Simulate.Loss = loss;
			c.Simulate.Reorder = reorder;
			c.Simulate.Latency = TimeSpan.FromMilliseconds(latencyMs);
			c.Simulate.Jitter = TimeSpan.FromMilliseconds(jitterMs);
			c.Simulate.Seed = 11;
		}

		var server = net.Server(Simulate);
		var client = net.Client(server, Simulate);
		Assert.True(net.StepUntil(() => client.Session.State == NetworkState.Connected, 120));

		var random = new Random(5);
		var entities = new List<Entity>();
		server.FixedStep = peer =>
		{
			// Churn: moves, spawns and despawns every tick.
			foreach (var entity in entities) peer.World.Get<Position>(entity).Value += new Vector2(1, 0.5f);
			if (random.NextDouble() < 0.2) entities.Add(TestWorlds.CreateAll(peer.World, entities.Count));
			if (entities.Count > 5 && random.NextDouble() < 0.1)
			{
				var index = random.Next(entities.Count);
				peer.World.Destroy(entities[index]);
				entities.RemoveAt(index);
			}
		};

		net.Step(300);
		server.FixedStep = null;
		var stopped = server.Net.CurrentTick;

		// Once the churn stops the client catches up and holds exactly the server's state at the snapshot tick.
		Assert.True(net.StepUntil(() => client.Net.LastReceivedServerTick >= stopped && SameState(server, client, client.Net.LastReceivedServerTick), 120),
			$"not converged: server tick {server.Net.CurrentTick}, client received {client.Net.LastReceivedServerTick}");
		Assert.Equal(entities.Count, Count(server));
		var frame = client.Net.Frame(client.Net.LastReceivedServerTick)!;
		var alive = 0;
		for (var slot = 0; slot < frame.SlotCount; slot++) if (frame.IsAlive(slot)) alive++;
		Assert.True(entities.Count == alive, $"frame alive {alive}");
		Assert.True(client.Net.LastReceivedServerTick == client.Net.AppliedTick, $"received {client.Net.LastReceivedServerTick} applied {client.Net.AppliedTick}");
		Assert.Equal(entities.Count, Count(client));
		if (loss > 0) Assert.True(server.Transport.Dropped > 0);
	}

	[Fact]
	public void ALostBaselineFallsBackToAFullSnapshot()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.SnapshotHistory = 8);
		var client = net.Client(server);
		net.StepUntil(() => client.Session.State == NetworkState.Connected, 30);
		TestWorlds.CreateAll(server.World, 1);
		net.Step(5);
		var full = server.Session.Stats.FullSnapshots;

		// The client stops acknowledging (its packets are lost) for longer than the ring: the server's baseline is gone.
		client.Session.Config.Simulate.Loss = 1;
		net.Step(12);
		Assert.True(server.Session.Stats.FullSnapshots > full);
	}

	[Fact]
	public void LargeWorldsAreSentInManyPartsAndOversizedSnapshotsAreDropped()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var client = net.Client(server);
		net.StepUntil(() => client.Session.State == NetworkState.Connected, 30);

		// 12,000 entities: a full snapshot of several hundred parts.
		for (var i = 0; i < 12_000; i++) TestWorlds.CreateAll(server.World, i);
		Assert.True(net.StepUntil(() => Count(client) == 12_000, 30));
		Assert.True(SameState(server, client, client.Net.LastReceivedServerTick));

		// A second client with a tiny MTU: the snapshot needs more than 4096 parts, so it is flagged and dropped rather
		// than completed with part of the entities.
		var tinyServer = net.Server(c => c.MtuBytes = 256);
		var tinyClient = net.Client(tinyServer, c => c.MtuBytes = 256);
		for (var i = 0; i < 20_000; i++) TestWorlds.CreateAll(tinyServer.World, i);
		net.Step(10);
		Assert.True(tinyClient.Session.IsActive);
		Assert.Equal(0, Count(tinyClient));
		Assert.Equal(0u, tinyClient.Net.LastReceivedServerTick);
		Assert.True(tinyClient.Session.Stats.SnapshotsLost > 0);
	}

	[Fact]
	public void EntitiesMarkedLocalAreNotNetworked()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var entity = server.World.Create(new Health(1, 1), new NetworkLocal());
		net.Step(2);
		Assert.False(server.World.Has<NetworkId>(entity));
	}
}
