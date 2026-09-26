using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, INTEGRATION)]
public class MessageTests
{
	private static (NetHarness Net, NetPeer Server, NetPeer A, NetPeer B) Connect()
	{
		var net = new NetHarness();
		var server = net.Server();
		var a = net.Client(server);
		var b = net.Client(server);
		Assert.True(net.StepUntil(() => a.Session.IsActive && b.Session.IsActive, 30));
		return (net, server, a, b);
	}

	[Fact]
	public void EachReaderSeesEachMessageOnceWithItsSenderAndTick()
	{
		var (net, server, a, b) = Connect();
		using var owner = net;
		var first = server.Session.Reader<ChatRequest>();
		var second = server.Session.Reader<ChatRequest>();

		a.Session.SendToServer(new ChatRequest("from a"));
		b.Session.SendToServer(new ChatRequest("from b"));
		var sentAt = a.Net.CurrentTick;
		net.Step(2);

		var seen = new List<string>();
		while (first.TryRead(out var from, out var request))
		{
			Assert.True(from == a.Session.LocalPeer || from == b.Session.LocalPeer);
			seen.Add($"{from.Id}:{request.Text}");
		}

		Assert.Equal([$"{a.Session.LocalPeer.Id}:from a", $"{b.Session.LocalPeer.Id}:from b"], seen.Order());
		Assert.False(first.TryRead(out _, out _));

		// A second reader has its own cursor, and gets the sender's tick too.
		var all = second.Read();
		Assert.Equal(2, all.Length);
		Assert.Contains(all.ToArray(), m => m.Tick == sentAt);
		Assert.Equal(0, second.Count);
	}

	[Fact]
	public void MessagesStayVisibleForTheFrameAfterTheirs()
	{
		var (net, server, a, _) = Connect();
		using var owner = net;
		a.Session.SendToServer(new ChatRequest("x"));
		net.Step(2);
		var late = server.Session.Reader<ChatRequest>();
		Assert.Equal(1, late.Count);
		net.Step(2);
		var later = server.Session.Reader<ChatRequest>();
		Assert.Equal(0, later.Count);
	}

	[Fact]
	public void SendBroadcastAndBothDirections()
	{
		var (net, server, a, b) = Connect();
		using var owner = net;
		var aChats = a.Session.Reader<Chat>();
		var bChats = b.Session.Reader<Chat>();
		var aNotes = a.Session.Reader<Note>();
		var serverNotes = server.Session.Reader<Note>();
		var events = a.Events.Reader<NetworkMessageReceived<Chat>>();

		server.Session.Broadcast(new Chat(0, "everyone"));
		server.Session.Send(b.Session.LocalPeer, new Chat(0, "only b"));
		server.Session.Send(a.Session.LocalPeer, new Note(1), Delivery.Unreliable);
		a.Session.Broadcast(new Note(2));
		net.Step(2);

		Assert.Equal(1, aChats.Count);
		Assert.Equal(2, bChats.Count);
		Assert.True(aNotes.TryRead(out var fromServer, out var note));
		Assert.True(fromServer.IsServer);
		Assert.Equal(1, note.Value);
		Assert.True(serverNotes.TryRead(out var fromA, out var noteA));
		Assert.Equal(a.Session.LocalPeer, fromA);
		Assert.Equal(2, noteA.Value);

		// Also on the frame event bus, for coroutines and test collectors.
		Assert.Equal(1, events.Count);

		// A server cannot send a client-to-server message; a message to itself is delivered locally.
		Assert.Throws<InvalidOperationException>(() => server.Session.Broadcast(new MoveInput(Vector2.One)));
		var local = server.Session.Reader<Note>();
		local.Skip();
		server.Session.Send(NetworkPeer.Server, new Note(3));
		Assert.True(local.TryRead(out _, out var self));
		Assert.Equal(3, self.Value);
	}

	[Fact]
	public void ReliableOrderedMessagesArriveInOrderUnderJitter()
	{
		using var net = new NetHarness();
		void Simulate(NetworkConfig c)
		{
			c.Simulate.Latency = TimeSpan.FromMilliseconds(40);
			c.Simulate.Jitter = TimeSpan.FromMilliseconds(60);
			c.Simulate.Loss = 0.3;
			c.Simulate.Reorder = 0.5;
		}

		var server = net.Server(Simulate);
		var client = net.Client(server, Simulate);
		Assert.True(net.StepUntil(() => client.Session.IsActive, 60));
		var reader = server.Session.Reader<ChatRequest>();
		var received = new List<string>();
		for (var i = 0; i < 100; i++)
		{
			client.Session.SendToServer(new ChatRequest(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			net.Step();
			while (reader.TryRead(out _, out var m)) received.Add(m.Text.ToString());
		}

		for (var i = 0; i < 30; i++)
		{
			net.Step();
			while (reader.TryRead(out _, out var m)) received.Add(m.Text.ToString());
		}

		Assert.Equal(Enumerable.Range(0, 100).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), received);
	}

	[Fact]
	public void SendingWhileOfflineDoesNothing()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var client = net.Client(server);

		// Not connected yet: dropped silently.
		client.Session.SendToServer(new ChatRequest("early"));
		Assert.Equal(0, client.Session.Stats.MessagesOut);
		_ = server;
	}
}
