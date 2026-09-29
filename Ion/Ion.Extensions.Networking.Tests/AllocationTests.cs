using Arch.Core;

using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, INTEGRATION)]
public class AllocationTests(ITestOutputHelper output)
{
	[Fact]
	public void TheReceivePathAllocatesNothingInSteadyState()
	{
		using var net = new NetHarness();
		void Lossy(NetworkConfig c)
		{
			c.Simulate.Latency = TimeSpan.FromMilliseconds(30);
			c.Simulate.Jitter = TimeSpan.FromMilliseconds(10);
			c.Simulate.Loss = 0.1;
			c.Simulate.Reorder = 0.1;
		}

		var server = net.Server(Lossy);
		var client = net.Client(server, Lossy);
		Assert.True(net.StepUntil(() => client.Session.IsActive, 60));

		var input = new Vector2(1, 0);
		static void Move(ref Avatar avatar, in MoveInput move, float delta) => avatar.Position += move.Move * delta;
		server.Session.Prediction.Register<Avatar, MoveInput>(Move);
		client.Session.Prediction.Register<Avatar, MoveInput>(Move, () => new MoveInput(input));
		TestWorlds.CreateAvatar(server.World, server.Net.Allocate(client.Session.LocalPeer));

		// 200 moving entities, messages both ways every frame, interpolation on the client.
		var entities = new List<Entity>();
		for (var i = 0; i < 200; i++) entities.Add(TestWorlds.CreateAll(server.World, i));
		var requests = server.Session.Reader<ChatRequest>();
		var chats = client.Session.Reader<Chat>();
		var frame = 0;
		server.FixedStep = peer =>
		{
			foreach (var entity in entities) peer.World.Get<Position>(entity).Value += new Vector2(0.5f, 0);
			peer.World.Get<Health>(entities[frame % entities.Count]).Current++;
		};

		void Frame(ref long receive)
		{
			frame++;
			input = new Vector2(frame % 3, 1);
			client.Session.SendToServer(new ChatRequest("hello"));
			server.Session.Broadcast(new Chat(0, "hi"));

			var before = GC.GetAllocatedBytesForCurrentThread();
			server.First();
			client.First();
			receive += GC.GetAllocatedBytesForCurrentThread() - before;

			server.Fixed();
			client.Fixed();
			client.Net.Interpolate();
			while (requests.TryRead(out _, out _)) { }
			while (chats.TryRead(out _, out _)) { }
			server.Last();
			client.Last();
			net.Clock.Advance(net.FrameTime);
		}

		long warmup = 0;
		for (var i = 0; i < 300; i++) Frame(ref warmup);

		long receive = 0;
		var total = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 300; i++) Frame(ref receive);
		total = GC.GetAllocatedBytesForCurrentThread() - total;

		output.WriteLine($"receive path: {receive} B over 300 frames; whole frame (both peers): {total} B; client got {client.Session.Stats.Snapshots} snapshots, {client.Session.Stats.MessagesIn} messages");
		Assert.True(client.Session.Stats.Snapshots > 200);
		Assert.Equal(0, receive);
		Assert.Equal(0, total);
	}
}
