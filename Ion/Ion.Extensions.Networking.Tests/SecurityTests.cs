using Arch.Core;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, INTEGRATION)]
public class SecurityTests
{
	private static bool Connected(NetPeer client) => client.Session.State == NetworkState.Connected;

	private static bool Refused(NetPeer client) => client.Session.State == NetworkState.Disconnected;

	/// <summary>A packet as a (malicious) client would send it: header, then <paramref name="body"/>.</summary>
	internal static void SendRaw(NetPeer client, PacketKind kind, Action<NetWriterBox> body, Delivery delivery = Delivery.ReliableOrdered)
	{
		var box = new NetWriterBox();
		box.Write((ref NetWriter w) => Protocol.WriteHeader(ref w, kind, client.Net.CurrentTick, 0));
		body(box);
		client.Transport.Send(0, box.Bytes, delivery);
	}

	/// <summary>Writes a Messages packet body with one message.</summary>
	internal static Action<NetWriterBox> Message<T>(in T message) where T : unmanaged
	{
		var info = NetworkTypes<T>.Message!;
		var payload = new byte[256];
		var writer = new NetWriter(payload);
		info.Serializer.Write(ref writer, message);
		var bytes = writer.Written.ToArray();
		return box => box.Write((ref NetWriter w) =>
		{
			w.WriteVarUInt32((uint)info.Ordinal);
			w.WriteUInt32(1);
			w.WriteUInt16((ushort)bytes.Length);
			w.WriteBytes(bytes);
		});
	}

	[Fact]
	public void ARegistryMismatchIsRefusedWithItsReason()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var other = NetworkTypeTable.Create(NetworkRegistry.Table.Components.Cast<NetworkTypeInfo>().Skip(1));
		var client = net.Client(server, table: other);
		Assert.True(net.StepUntil(() => Refused(client), 30));
		Assert.Equal(DisconnectReason.RegistryMismatch, client.Session.DisconnectReason);
		Assert.Equal(1, server.Session.Stats.HandshakesRejected);
		Assert.Empty(server.Session.Peers);
	}

	[Theory]
	[InlineData("secret", "secret", DisconnectReason.None)]
	[InlineData("secret", "wrong", DisconnectReason.WrongSecret)]
	[InlineData("secret", null, DisconnectReason.WrongSecret)]
	[InlineData(null, null, DisconnectReason.None)]
	public void TheJoinSecretIsChecked(string? serverSecret, string? clientSecret, DisconnectReason expected)
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.JoinSecret = serverSecret);
		var client = net.Client(server, c => c.JoinSecret = clientSecret);
		Assert.True(net.StepUntil(() => Connected(client) || Refused(client), 30));
		Assert.Equal(expected, client.Session.DisconnectReason);
		Assert.Equal(expected == DisconnectReason.None, Connected(client));
	}

	[Fact]
	public void GameTickRateAndCapacityMismatchesAreRefused()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.MaxPeers = 1);
		var game = net.Client(server, c => c.GameId = "another game");
		var rate = net.Client(server, c => c.TickRate = 30);
		var first = net.Client(server);
		net.Step(10);
		var second = net.Client(server);
		net.Step(10);

		Assert.Equal(DisconnectReason.GameMismatch, game.Session.DisconnectReason);
		Assert.Equal(DisconnectReason.TickRateMismatch, rate.Session.DisconnectReason);
		Assert.True(Connected(first));
		Assert.Equal(DisconnectReason.ServerFull, second.Session.DisconnectReason);
		Assert.Equal(3, server.Session.Stats.HandshakesRejected);
	}

	[Fact]
	public void NothingButTheHandshakeIsParsedBeforeItIsAccepted()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var raw = new LoopbackTransport(net.Network, net.Clock);
		raw.StartClient("", server.Transport.LocalPort);
		var chats = server.Session.Reader<ChatRequest>();
		net.Step(2);

		var box = new NetWriterBox();
		box.Write((ref NetWriter w) => Protocol.WriteHeader(ref w, PacketKind.Messages, 1, 0));
		Message(new ChatRequest("sneaky"))(box);
		raw.Send(1 - 1, box.Bytes, Delivery.ReliableOrdered);
		net.Step(2);

		Assert.False(chats.Any());
		Assert.Equal(1, server.Session.Stats.Rejected);

		// And a connection that never completes its handshake is closed.
		net.Step((int)(server.Session.Config.HandshakeTimeout.TotalSeconds * 60) + 30);
		Span<byte> buffer = stackalloc byte[LoopbackTransport.MaxPacket];
		var disconnected = false;
		while (raw.TryReceive(buffer, out var e)) disconnected |= e.Kind == TransportEventKind.Disconnected;
		Assert.True(disconnected);
		raw.Dispose();
	}

	[Fact]
	public void AMessageTheClientMayNotSendIsDroppedAndCounted()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var client = net.Client(server);
		net.StepUntil(() => Connected(client), 30);
		var chats = server.Session.Reader<Chat>();

		// A client cannot send a server-to-client message through the API...
		Assert.Throws<InvalidOperationException>(() => client.Session.SendToServer(new Chat(1, "fake")));

		// ...and one crafted by hand is dropped by the server.
		SendRaw(client, PacketKind.Messages, Message(new Chat(1, "fake")));
		net.Step(2);
		Assert.False(chats.Any());
		Assert.Equal(1, server.Session.Stats.Rejected);
		Assert.True(Connected(client));
	}

	[Fact]
	public void OwnerAuthorityUpdatesAreAcceptedOnlyFromTheOwner()
	{
		using var net = new NetHarness();
		var server = net.Server();
		var owner = net.Client(server);
		var other = net.Client(server);
		net.StepUntil(() => Connected(owner) && Connected(other), 30);

		// The server gives one entity to each client.
		var mine = TestWorlds.CreateAvatar(server.World, server.Net.Allocate(owner.Session.LocalPeer));
		var theirs = TestWorlds.CreateAvatar(server.World, server.Net.Allocate(other.Session.LocalPeer));
		net.Step(5);
		var mineId = server.World.Get<NetworkId>(mine);
		var theirsId = server.World.Get<NetworkId>(theirs);
		Assert.True(owner.Net.TryGetEntity(mineId, out var ownerMine));
		Assert.True(owner.Net.TryGetEntity(theirsId, out var ownerTheirs));
		Assert.True(owner.Net.IsOwned(mineId));
		Assert.False(owner.Net.IsOwned(theirsId));

		// The owner writes its own entity's Aim: the server takes it and the other client sees it.
		owner.World.Get<Aim>(ownerMine).Angle = 1.5f;
		net.Step(5);
		Assert.Equal(1.5f, server.World.Get<Aim>(mine).Angle);
		Assert.True(other.Net.TryGetEntity(mineId, out var otherMine));
		Assert.Equal(1.5f, other.World.Get<Aim>(otherMine).Angle);
		Assert.Equal(0, server.Session.Stats.Rejected);

		// A forged update of someone else's entity is rejected and changes nothing.
		SendRaw(owner, PacketKind.OwnerUpdate, box => box.Write((ref NetWriter w) =>
		{
			w.WriteVarUInt32((uint)theirsId.Slot);
			w.WriteUInt32(theirsId.Id);
			w.WriteVarUInt32((uint)NetworkTypes<Aim>.Component!.Ordinal);
			w.WriteSingle(9f);
		}), Delivery.Unreliable);

		// So is an update of a server-authority component on its own entity.
		SendRaw(owner, PacketKind.OwnerUpdate, box => box.Write((ref NetWriter w) =>
		{
			w.WriteVarUInt32((uint)mineId.Slot);
			w.WriteUInt32(mineId.Id);
			w.WriteVarUInt32((uint)NetworkTypes<Health>.Component!.Ordinal);
			w.WriteInt32(1000);
			w.WriteInt32(1000);
		}), Delivery.Unreliable);
		net.Step(3);

		Assert.Equal(0f, server.World.Get<Aim>(theirs).Angle);
		Assert.False(server.World.Has<Health>(mine));
		Assert.Equal(2, server.Session.Stats.Rejected);
		_ = ownerTheirs;
	}

	[Fact]
	public void RateLimitsDropExcessMessagesAndRepeatedViolationsDisconnect()
	{
		using var net = new NetHarness();
		var server = net.Server(c =>
		{
			c.MaxMessagesPerSecond = 30;
			c.MaxViolations = 40;
		});
		var client = net.Client(server);
		net.StepUntil(() => Connected(client), 30);
		var requests = server.Session.Reader<ChatRequest>();
		net.Step(60);

		// 60 messages at once: the bucket (30 per second, full after a second) lets 30 through.
		for (var i = 0; i < 60; i++) client.Session.SendToServer(new ChatRequest("m" + i));
		net.Step(2);
		var received = requests.Count;
		Assert.InRange(received, 28, 32);
		Assert.True(server.Session.Stats.RateLimited > 0);
		Assert.True(Connected(client));

		// Keeping it up: more than MaxViolations drops, and the client is disconnected for it.
		for (var frame = 0; frame < 10; frame++)
		{
			for (var i = 0; i < 20; i++) client.Session.SendToServer(new ChatRequest("spam"));
			net.Step(1);
		}

		Assert.True(net.StepUntil(() => Refused(client), 60));
		Assert.Equal(DisconnectReason.Violations, client.Session.DisconnectReason);
		Assert.Empty(server.Session.Peers);
	}

	[Fact]
	public void TheByteBudgetIsEnforced()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.MaxBytesPerSecond = 2000);
		var client = net.Client(server);
		net.StepUntil(() => Connected(client), 30);
		net.Step(60);
		var before = server.Session.Stats.RateLimited;
		for (var i = 0; i < 10; i++) SendRaw(client, PacketKind.Ping, box => box.Write((ref NetWriter w) => w.WriteBytes(new byte[1000])), Delivery.Unreliable);
		net.Step(1);
		Assert.True(server.Session.Stats.RateLimited - before >= 7);
	}

	[Fact]
	public void MalformedPacketsNeverThrowOutOfPoll()
	{
		using var net = new NetHarness();
		var server = net.Server(c => c.MaxViolations = int.MaxValue);
		var client = net.Client(server);
		net.StepUntil(() => Connected(client), 30);
		TestWorlds.CreateAll(server.World, 1);
		net.Step(3);

		var random = new Random(9);
		var buffer = new byte[300];
		for (var i = 0; i < 2000; i++)
		{
			var length = random.Next(buffer.Length);
			random.NextBytes(buffer);
			buffer[0] = (byte)random.Next(12);
			if (buffer[0] is (byte)PacketKind.Reject or (byte)PacketKind.Disconnect) buffer[0] = (byte)PacketKind.Snapshot;
			client.Transport.Send(0, buffer.AsSpan(0, length), Delivery.Unreliable);

			// And the same garbage from the server to the client.
			server.Transport.Send(1, buffer.AsSpan(0, length), Delivery.Unreliable);
			if (i % 50 == 0) net.Step(1);
		}

		net.Step(2);
		Assert.True(server.Session.Stats.Malformed > 0);
		Assert.True(client.Session.Stats.Malformed > 0);
	}
}

/// <summary>Builds a packet with a <see cref="NetWriter"/> across lambdas (a ref struct cannot be captured).</summary>
internal sealed class NetWriterBox
{
	private readonly byte[] _buffer = new byte[4096];
	private int _length;

	public delegate void WriteAction(ref NetWriter writer);

	public ReadOnlySpan<byte> Bytes => _buffer.AsSpan(0, _length);

	public void Write(WriteAction action)
	{
		var writer = new NetWriter(_buffer.AsSpan(_length));
		action(ref writer);
		_length += writer.Position;
	}
}
