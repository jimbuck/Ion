using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

using Arch.Core;

using Ion.Extensions.Networking.WebSockets;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

/// <summary>The WebSocket transport on 127.0.0.1 (real sockets, real time): packets, connection events and a whole session.</summary>
[Trait(CATEGORY, E2E)]
public class WebSocketTransportTests
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
	public void PacketsAndConnectionEventsCrossTheWebSocket()
	{
		using var server = new WebSocketTransport();
		using var client = new WebSocketTransport();
		server.StartServer("127.0.0.1", 0, 4);
		Assert.NotEqual(0, server.LocalPort);
		Assert.Equal("websocket", server.Name);
		client.StartClient("127.0.0.1", server.LocalPort);

		var buffer = new byte[WebSocketTransport.MaxPacket];
		var serverConnection = -1;
		var clientConnected = false;
		var received = new List<int>();
		var fromServer = new List<int>();
		void Pump()
		{
			server.Poll();
			client.Poll();
			while (server.TryReceive(buffer, out var e))
			{
				if (e.Kind == TransportEventKind.Connected) serverConnection = e.Connection;
				if (e.Kind == TransportEventKind.Data) received.Add(BitConverter.ToInt32(buffer, 0));
			}

			while (client.TryReceive(buffer, out var e))
			{
				clientConnected |= e.Kind == TransportEventKind.Connected;
				if (e.Kind == TransportEventKind.Data) fromServer.Add(BitConverter.ToInt32(buffer, 0));
			}
		}

		Assert.True(Until(() => serverConnection > 0 && clientConnected, Pump));

		// Every delivery arrives, in order (TCP), both ways.
		for (var i = 0; i < 100; i++) client.Send(0, BitConverter.GetBytes(i), (Delivery)(i % 4));
		for (var i = 0; i < 50; i++) server.Send(serverConnection, BitConverter.GetBytes(1000 + i), Delivery.Unreliable);
		client.Flush();
		server.Flush();
		Assert.True(Until(() => received.Count == 100 && fromServer.Count == 50, Pump));
		Assert.Equal(Enumerable.Range(0, 100), received);
		Assert.Equal(Enumerable.Range(1000, 50), fromServer);

		// A large packet arrives whole; one over the limit is not sent.
		var big = new byte[40_000];
		big[0] = 42;
		big[^1] = 7;
		var bigReceived = false;
		client.Send(0, big, Delivery.ReliableOrdered);
		client.Send(0, new byte[WebSocketTransport.MaxPacket + 1], Delivery.ReliableOrdered);
		Assert.True(Until(() => bigReceived, () =>
		{
			while (server.TryReceive(buffer, out var e)) bigReceived |= e.Kind == TransportEventKind.Data && e.Length == 40_000 && buffer[0] == 42 && buffer[39_999] == 7;
		}));

		var disconnected = false;
		client.Disconnect(0);
		Assert.True(Until(() => disconnected, () =>
		{
			while (server.TryReceive(buffer, out var e)) disconnected |= e.Kind == TransportEventKind.Disconnected && e.Connection == serverConnection;
		}));
	}

	[Fact]
	public void AFullServerTurnsAClientAwayAndAPlainRequestGetsA404()
	{
		using var server = new WebSocketTransport();
		server.StartServer("127.0.0.1", 0, 1);
		using var first = new WebSocketTransport();
		using var second = new WebSocketTransport();
		var buffer = new byte[WebSocketTransport.MaxPacket];
		first.StartClient("127.0.0.1", server.LocalPort);
		var firstConnected = false;
		Assert.True(Until(() => firstConnected, () =>
		{
			while (first.TryReceive(buffer, out var e)) firstConnected |= e.Kind == TransportEventKind.Connected;
			while (server.TryReceive(buffer, out _)) { }
		}));

		second.StartClient("127.0.0.1", server.LocalPort);
		var secondEvents = new List<TransportEventKind>();
		Assert.True(Until(() => secondEvents.Contains(TransportEventKind.Disconnected), () =>
		{
			while (second.TryReceive(buffer, out var e)) secondEvents.Add(e.Kind);
		}));
		Assert.DoesNotContain(TransportEventKind.Connected, secondEvents);

		// An ordinary HTTP request is not an upgrade.
		using var tcp = new TcpClient("127.0.0.1", server.LocalPort);
		var stream = tcp.GetStream();
		stream.Write("GET /ion-net HTTP/1.1\r\nHost: localhost\r\n\r\n"u8);
		var reply = new byte[256];
		var read = stream.Read(reply);
		Assert.StartsWith("HTTP/1.1 404", Encoding.ASCII.GetString(reply, 0, read));
	}

	private sealed class WebSocketPeer : IDisposable
	{
		public readonly World World = World.Create();
		public readonly EventBus Events = new();
		public readonly WebSocketTransport Transport = new();
		public readonly NetworkSession Session;

		public WebSocketPeer(IClock clock, NetworkConfig config) => Session = new NetworkSession(config, Transport, World, Events, clock, 60, "ws-test");

		public void Frame()
		{
			Session.Poll();
			Events.BeginFixedStep();
			Session.BeginTick();
			Session.Capture();
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
	public void TheHandshakeAndReplicationWorkOverWebSockets(string serverSecret, string clientSecret, DisconnectReason expected)
	{
		var clock = new StopwatchClock();
		using var server = new WebSocketPeer(clock, new NetworkConfig { Mode = NetworkMode.Server, Port = 0, TickRate = 60, JoinSecret = serverSecret });
		server.Session.Start();
		using var client = new WebSocketPeer(clock, new NetworkConfig { Mode = NetworkMode.Client, Port = server.Transport.LocalPort, TickRate = 60, JoinSecret = clientSecret });
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
	}

	private sealed class StopwatchClock : IClock
	{
		private readonly Stopwatch _watch = Stopwatch.StartNew();

		public TimeSpan Elapsed => _watch.Elapsed;

		public TimeSpan NextFrame() => _watch.Elapsed;

		public void Sleep(TimeSpan duration)
		{
		}
	}
}
