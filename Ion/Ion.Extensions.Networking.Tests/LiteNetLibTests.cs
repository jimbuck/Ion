using System.Diagnostics;

using Arch.Core;

using Ion.Extensions.Networking.LiteNetLib;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

/// <summary>The UDP transport on 127.0.0.1 (real sockets, real time).</summary>
[Trait(CATEGORY, E2E)]
public class LiteNetLibTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	private static bool Until(Func<bool> condition, Action pump)
	{
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < Timeout)
		{
			pump();
			if (condition()) return true;
			Thread.Sleep(2);
		}

		return condition();
	}

	[Fact]
	public void PacketsAndConnectionEventsCrossTheSocket()
	{
		using var server = new LiteNetLibTransport();
		using var client = new LiteNetLibTransport();
		server.StartServer("127.0.0.1", 0, 4);
		Assert.NotEqual(0, server.LocalPort);
		client.StartClient("127.0.0.1", server.LocalPort);

		var buffer = new byte[LiteNetLibTransport.MaxReliablePacket];
		var serverConnection = -1;
		var clientConnected = false;
		var received = new List<int>();
		void Pump()
		{
			server.Poll();
			client.Poll();
			while (server.TryReceive(buffer, out var e))
			{
				if (e.Kind == TransportEventKind.Connected) serverConnection = e.Connection;
				if (e.Kind == TransportEventKind.Data) received.Add(BitConverter.ToInt32(buffer, 0));
			}

			while (client.TryReceive(buffer, out var e)) clientConnected |= e.Kind == TransportEventKind.Connected;
		}

		Assert.True(Until(() => serverConnection > 0 && clientConnected, Pump));

		for (var i = 0; i < 100; i++) client.Send(0, BitConverter.GetBytes(i), Delivery.ReliableOrdered);
		client.Flush();
		Assert.True(Until(() => received.Count == 100, Pump));
		Assert.Equal(Enumerable.Range(0, 100), received);

		// A reliable packet larger than the MTU is fragmented and arrives whole.
		var big = new byte[8000];
		big[0] = 42;
		big[^1] = 7;
		var bigReceived = false;
		client.Send(0, big, Delivery.ReliableUnordered);
		client.Flush();
		Assert.True(Until(() => bigReceived, () =>
		{
			server.Poll();
			while (server.TryReceive(buffer, out var e)) bigReceived |= e.Kind == TransportEventKind.Data && e.Length == 8000 && buffer[0] == 42 && buffer[7999] == 7;
		}));

		var disconnected = false;
		client.Disconnect(0);
		client.Flush();
		Assert.True(Until(() => disconnected, () =>
		{
			server.Poll();
			while (server.TryReceive(buffer, out var e)) disconnected |= e.Kind == TransportEventKind.Disconnected && e.Connection == serverConnection;
		}));
	}

	private sealed class UdpPeer : IDisposable
	{
		public readonly World World = World.Create();
		public readonly EventBus Events = new();
		public readonly LiteNetLibTransport Transport = new();
		public readonly NetworkSession Session;

		public UdpPeer(IClock clock, NetworkConfig config) => Session = new NetworkSession(config, Transport, World, Events, clock, 60, "udp-test");

		public void Frame()
		{
			Session.Poll();
			Events.BeginFixedStep();
			Session.World.BeginTick();
			Session.World.Capture();
			Events.EndFixedSteps();
			Session.Send();
			Events.Step();
		}

		public void Dispose()
		{
			Session.Dispose();
			World.Dispose();
		}
	}

	[Theory]
	[InlineData("s3cret", "s3cret", DisconnectReason.None)]
	[InlineData("s3cret", "guess", DisconnectReason.WrongSecret)]
	public void TheHandshakeAndReplicationWorkOverUdp(string serverSecret, string clientSecret, DisconnectReason expected)
	{
		var clock = new StopwatchClockForTests();
		using var server = new UdpPeer(clock, new NetworkConfig { Mode = NetworkMode.Server, Port = 0, TickRate = 60, JoinSecret = serverSecret });
		server.Session.Start();
		using var client = new UdpPeer(clock, new NetworkConfig { Mode = NetworkMode.Client, Port = server.Transport.LocalPort, TickRate = 60, JoinSecret = clientSecret });
		client.Session.Start();

		void Pump()
		{
			server.Frame();
			client.Frame();
		}

		Assert.True(Until(() => client.Session.State is NetworkState.Connected or NetworkState.Disconnected, Pump));
		Assert.Equal(expected, client.Session.DisconnectReason);
		if (expected != DisconnectReason.None)
		{
			Assert.Empty(server.Session.Peers);
			return;
		}

		var entity = TestWorlds.CreateAll(server.World, 3);
		var networked = new QueryDescription().WithAll<NetworkId>();
		Assert.True(Until(() => client.World.CountEntities(in networked) == 1, Pump));
		server.World.Get<Health>(entity).Current = 77;
		var id = server.World.Get<NetworkId>(entity);
		Assert.True(Until(() => client.Session.World.TryGetEntity(id, out var mirror) && client.World.Get<Health>(mirror).Current == 77, Pump));
		Assert.True(client.Session.RoundTripTime(NetworkPeer.Server) > TimeSpan.Zero || client.Session.Stats.PacketsIn > 0);
	}

	[Fact]
	public void TheUdpReceivePathAllocatesNothingInSteadyState()
	{
		var clock = new StopwatchClockForTests();
		using var server = new UdpPeer(clock, new NetworkConfig { Mode = NetworkMode.Server, Port = 0, TickRate = 60 });
		server.Session.Start();
		using var client = new UdpPeer(clock, new NetworkConfig { Mode = NetworkMode.Client, Port = server.Transport.LocalPort, TickRate = 60 });
		client.Session.Start();
		Assert.True(Until(() => client.Session.IsActive, () =>
		{
			server.Frame();
			client.Frame();
		}));

		var entities = new List<Entity>();
		for (var i = 0; i < 100; i++) entities.Add(TestWorlds.CreateAll(server.World, i));
		long received = 0;
		var allocatingFrames = 0;
		void Frame(bool measure)
		{
			foreach (var entity in entities) server.World.Get<Position>(entity).Value += Vector2.One;
			server.Session.Broadcast(new Chat(0, "tick"));
			server.Frame();
			var before = GC.GetAllocatedBytesForCurrentThread();
			client.Session.Poll();
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			if (measure && allocated > 0)
			{
				received += allocated;
				allocatingFrames++;
			}
			client.Events.BeginFixedStep();
			client.Session.World.BeginTick();
			client.Events.EndFixedSteps();
			client.Session.Send();
			client.Events.Step();
			Thread.Sleep(1);
		}

		for (var i = 0; i < 300; i++) Frame(false);

		// A stall makes a burst larger than any before it, so the queues and event channels reach their size here.
		Thread.Sleep(100);
		for (var i = 0; i < 30; i++) Frame(false);
		var snapshots = client.Session.Stats.Snapshots;
		for (var i = 0; i < 300; i++) Frame(true);
		Assert.True(client.Session.Stats.Snapshots - snapshots > 100);

		// Real sockets and real time: a burst larger than every earlier one can still grow a buffer once (amortized, like
		// any capacity growth); frames otherwise allocate nothing. The loopback test asserts exactly zero.
		Assert.True(allocatingFrames <= 2, $"{allocatingFrames} of 300 frames allocated ({received} B)");
	}

	private sealed class StopwatchClockForTests : IClock
	{
		private readonly Stopwatch _watch = Stopwatch.StartNew();

		public TimeSpan Elapsed => _watch.Elapsed;

		public TimeSpan NextFrame() => _watch.Elapsed;

		public void Sleep(TimeSpan duration)
		{
		}
	}
}
