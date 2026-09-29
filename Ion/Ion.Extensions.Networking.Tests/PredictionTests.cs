using Arch.Core;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, INTEGRATION)]
public class PredictionTests
{
	private static void Move(ref Avatar avatar, in MoveInput input, float delta) => avatar.Position += input.Move * (delta * 60f);

	private static void Lag(NetworkConfig config)
	{
		config.Simulate.Latency = TimeSpan.FromMilliseconds(50);
		config.Simulate.Jitter = TimeSpan.FromMilliseconds(10);
		config.Simulate.Seed = 4;
	}

	[Fact]
	public void TheOwnerPredictsImmediatelyAndMatchesTheServerExactly()
	{
		using var net = new NetHarness();
		var server = net.Server(Lag);
		var client = net.Client(server, Lag);
		Assert.True(net.StepUntil(() => client.Session.IsActive, 60));

		// Scripted input: right for 60 ticks, then down for 60, then nothing.
		var input = Vector2.Zero;
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		client.Session.Prediction.Register<Avatar, MoveInput>(Move, () => new MoveInput(input));

		var entity = TestWorlds.CreateAvatar(server.World, server.Net.Allocate(client.Session.LocalPeer));
		var id = default(NetworkId);
		net.StepUntil(() => server.World.TryGet(entity, out id) && client.Net.TryGetEntity(id, out _), 60);
		Assert.True(client.Net.TryGetEntity(id, out var mirror));

		input = new Vector2(1, 0);
		var before = client.World.Get<Avatar>(mirror).Position;
		net.Step(1);

		// Applied on the client in the same frame, long before the server has seen the input (100 ms round trip).
		Assert.Equal(before + new Vector2(1, 0), client.World.Get<Avatar>(mirror).Position);
		Assert.Equal(Vector2.Zero, server.World.Get<Avatar>(entity).Position);

		net.Step(59);
		input = new Vector2(0, 1);
		net.Step(60);
		input = Vector2.Zero;
		net.Step(30);

		// Every input reached the server and the client's state is the server's, bit for bit.
		var serverValue = server.World.Get<Avatar>(entity).Position;
		Assert.Equal(new Vector2(60, 60), serverValue);
		Assert.Equal(serverValue, client.World.Get<Avatar>(mirror).Position);
		Assert.True(client.Session.Stats.Corrections <= 1, $"{client.Session.Stats.Corrections} corrections");
	}

	[Fact]
	public void AServerOverrideIsReconciledByReplayingTheStoredInputs()
	{
		using var net = new NetHarness();
		var server = net.Server(Lag);
		var client = net.Client(server, Lag);
		net.StepUntil(() => client.Session.IsActive, 60);
		var input = new Vector2(1, 0);
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		client.Session.Prediction.Register<Avatar, MoveInput>(Move, () => new MoveInput(input));
		var entity = TestWorlds.CreateAvatar(server.World, server.Net.Allocate(client.Session.LocalPeer));
		net.Step(60);
		var corrections = client.Session.Stats.Corrections;

		// The server teleports the avatar (a respawn): the client's prediction for that tick is wrong.
		server.FixedStep = peer =>
		{
			peer.World.Get<Avatar>(entity).Position = new Vector2(500, 500);
			peer.FixedStep = null;
		};
		net.Step(30);
		input = Vector2.Zero;
		net.Step(30);

		Assert.True(client.Session.Stats.Corrections > corrections);
		var id = server.World.Get<NetworkId>(entity);
		Assert.True(client.Net.TryGetEntity(id, out var mirror));
		var serverValue = server.World.Get<Avatar>(entity).Position;
		Assert.True(serverValue.X > 500);
		Assert.Equal(serverValue, client.World.Get<Avatar>(mirror).Position);
	}

	[Fact]
	public void LostInputsAreCoveredByTheRedundantCopies()
	{
		using var net = new NetHarness();
		void Lossy(NetworkConfig config)
		{
			Lag(config);
			config.Simulate.Loss = 0.3;
		}

		var server = net.Server(Lossy);
		var client = net.Client(server, Lossy);
		Assert.True(net.StepUntil(() => client.Session.IsActive, 120));
		var input = new Vector2(1, 0);
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		client.Session.Prediction.Register<Avatar, MoveInput>(Move, () => new MoveInput(input));
		var entity = TestWorlds.CreateAvatar(server.World, server.Net.Allocate(client.Session.LocalPeer));
		net.Step(120);
		input = Vector2.Zero;
		net.Step(60);

		var id = server.World.Get<NetworkId>(entity);
		Assert.True(client.Net.TryGetEntity(id, out var mirror));
		Assert.Equal(server.World.Get<Avatar>(entity).Position, client.World.Get<Avatar>(mirror).Position);
		Assert.True(server.Session.Prediction.TryGetInput<MoveInput>(client.Session.LocalPeer, out var last));
		Assert.Equal(Vector2.Zero, last.Move);
	}

	[Fact]
	public void RegistrationIsChecked()
	{
		using var net = new NetHarness();
		var server = net.Server();
		Assert.Throws<InvalidOperationException>(() => server.Session.Prediction.Register<Avatar, Chat>((ref Avatar a, in Chat c, float d) => { }));
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		Assert.Throws<InvalidOperationException>(() => server.Session.Prediction.Register<Avatar, MoveInput>(Move));
	}
}

[Trait(CATEGORY, INTEGRATION)]
public class InterpolationAndLagCompensationTests
{
	[Fact]
	public void RemoteEntitiesAreDrawnBetweenSnapshots()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.SendRate = 20);
		var client = net.Client(server, c => c.InterpolationDelay = 3);
		net.StepUntil(() => client.Session.IsActive, 30);
		var entity = server.World.Create(new Position(Vector2.Zero));
		server.FixedStep = peer => peer.World.Get<Position>(entity).Value += new Vector2(1, 0);
		net.Step(60);

		var id = server.World.Get<NetworkId>(entity);
		Assert.True(client.Net.TryGetEntity(id, out var mirror));

		// Snapshots arrive every third tick; the drawn value moves every frame, smoothly, behind the newest snapshot.
		var xs = new List<float>();
		for (var i = 0; i < 12; i++)
		{
			net.Step();
			client.Net.Interpolate();
			xs.Add(client.World.Get<Position>(mirror).Value.X);
		}

		for (var i = 1; i < xs.Count; i++) Assert.True(xs[i] > xs[i - 1], string.Join(", ", xs));
		var newest = client.Net.Frame(client.Net.LastReceivedServerTick)!;
		Assert.True(xs[^1] < server.World.Get<Position>(entity).Value.X);
		Assert.True(client.Net.InterpolationTick < client.Net.LastReceivedServerTick);
		_ = newest;
	}

	[Fact]
	public void LagCompensationRewindsWithinTheLimitAndRestores()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.MaxRewindTicks = 12);
		var client = net.Client(server);
		net.StepUntil(() => client.Session.IsActive, 30);
		var entity = server.World.Create(new Position(Vector2.Zero));
		server.FixedStep = peer => peer.World.Get<Position>(entity).Value += new Vector2(1, 0);
		net.Step(30);
		server.FixedStep = null;

		var now = server.Net.CurrentTick;
		var peer = client.Session.LocalPeer;
		var current = server.World.Get<Position>(entity).Value;

		// Five ticks back is plausible and in the ring: the rewound world has the old position, then it is restored.
		Assert.True(server.Net.TryGetRewindTick(peer, now - 5, out var tick));
		Assert.Equal(current.X - 5, server.Net.GetAtTick<Position>(entity, tick).Value.X);
		var seen = Vector2.Zero;
		server.Net.WithWorldAtTick(tick, world => seen = world.Get<Position>(entity).Value);
		Assert.Equal(current - new Vector2(5, 0), seen);
		Assert.Equal(current, server.World.Get<Position>(entity).Value);
		Assert.Equal(1, server.Session.Stats.Rewinds);

		// Beyond MaxRewindTicks, or in the future, it is refused; WithWorldAtTick clamps.
		Assert.False(server.Net.TryGetRewindTick(peer, now - 13, out _));
		Assert.False(server.Net.TryGetRewindTick(peer, now + 1, out _));
		Assert.Equal(2, server.Session.Stats.RewindsRejected);
		server.Net.WithWorldAtTick(now - 25, world => seen = world.Get<Position>(entity).Value);
		Assert.Equal(current - new Vector2(12, 0), seen);
		Assert.True(server.Net.TryGetAtTick<Position>(entity, now - 3, out var old));
		Assert.Equal(current.X - 3, old.Value.X);
	}

	private sealed class HalfPolicy : IInterestPolicy
	{
		public bool ShowOdd;

		public void BeginPeer(NetworkPeer peer, World world)
		{
		}

		public bool IsRelevant(NetworkPeer peer, Entity entity, in NetworkId id) => ShowOdd || id.Slot % 2 == 0;
	}

	[Fact]
	public void TheInterestPolicyDecidesWhatEachPeerReceives()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var policy = new HalfPolicy();
		server.Net.InterestPolicy = policy;
		var client = net.Client(server);
		net.StepUntil(() => client.Session.IsActive, 30);
		for (var i = 0; i < 10; i++) TestWorlds.CreateAll(server.World, i);
		net.Step(5);

		var networked = new QueryDescription().WithAll<NetworkId>();
		Assert.Equal(5, client.World.CountEntities(in networked));

		// Relevance changes: the others appear, and disappear again.
		policy.ShowOdd = true;
		net.Step(5);
		Assert.Equal(10, client.World.CountEntities(in networked));
		policy.ShowOdd = false;
		net.Step(5);
		Assert.Equal(5, client.World.CountEntities(in networked));
	}
}
