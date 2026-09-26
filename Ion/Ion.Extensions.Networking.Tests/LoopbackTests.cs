using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, UNIT)]
public class LoopbackTests
{
	private sealed class Pair : IDisposable
	{
		public readonly ManualClock Clock = new();
		public readonly LoopbackNetwork Network = new();
		public readonly LoopbackTransport Server;
		public readonly LoopbackTransport Client;
		public readonly byte[] Buffer = new byte[LoopbackTransport.MaxPacket];

		public Pair(NetworkSimulation simulation)
		{
			Server = new LoopbackTransport(Network, Clock, new NetworkSimulation());
			Client = new LoopbackTransport(Network, Clock, simulation);
			Server.StartServer("127.0.0.1", 0, 4);
			Client.StartClient("127.0.0.1", Server.LocalPort);
			Assert.True(Client.TryReceive(Buffer, out var e) || Advance(simulation.Latency + simulation.Jitter, out e));
			Assert.Equal(TransportEventKind.Connected, e.Kind);
			Assert.True(Server.TryReceive(Buffer, out var s));
			Assert.Equal(TransportEventKind.Connected, s.Kind);
			Assert.Equal(1, s.Connection);
		}

		private bool Advance(TimeSpan time, out TransportEvent e)
		{
			Clock.Advance(time);
			return Client.TryReceive(Buffer, out e);
		}

		public List<int> Drain()
		{
			var values = new List<int>();
			while (Server.TryReceive(Buffer, out var e))
			{
				if (e.Kind == TransportEventKind.Data) values.Add(BitConverter.ToInt32(Buffer, 0));
			}

			return values;
		}

		public void Send(int value, Delivery delivery) => Client.Send(0, BitConverter.GetBytes(value), delivery);

		public void Dispose()
		{
			Client.Dispose();
			Server.Dispose();
		}
	}

	[Fact]
	public void PacketsArriveAfterTheLatency()
	{
		using var pair = new Pair(new NetworkSimulation { Latency = TimeSpan.FromMilliseconds(100) });
		pair.Send(7, Delivery.Unreliable);
		pair.Clock.Advance(TimeSpan.FromMilliseconds(99));
		Assert.Empty(pair.Drain());
		pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
		Assert.Equal([7], pair.Drain());
	}

	[Fact]
	public void LossDropsOnlyUnreliableAndSequencedPackets()
	{
		using var pair = new Pair(new NetworkSimulation { Loss = 0.5, Seed = 3 });
		for (var i = 0; i < 200; i++) pair.Send(i, Delivery.Unreliable);
		for (var i = 0; i < 200; i++) pair.Send(1000 + i, Delivery.ReliableUnordered);
		var received = pair.Drain();
		var unreliable = received.Count(v => v < 1000);
		Assert.InRange(unreliable, 70, 130);
		Assert.Equal(200, received.Count(v => v >= 1000));
		Assert.Equal(200 - unreliable, pair.Client.Dropped);
	}

	[Fact]
	public void ReorderingOnlyAffectsUnreliablePacketsAndOrderedOnesStayInOrder()
	{
		using var pair = new Pair(new NetworkSimulation { Latency = TimeSpan.FromMilliseconds(20), Jitter = TimeSpan.FromMilliseconds(30), Reorder = 0.3, Seed = 5 });
		for (var i = 0; i < 100; i++)
		{
			pair.Send(i, Delivery.Unreliable);
			pair.Send(1000 + i, Delivery.ReliableOrdered);
			pair.Clock.Advance(TimeSpan.FromMilliseconds(2));
		}

		pair.Clock.Advance(TimeSpan.FromSeconds(1));
		var received = pair.Drain();
		var ordered = received.Where(v => v >= 1000).ToList();
		var unordered = received.Where(v => v < 1000).ToList();
		Assert.Equal(Enumerable.Range(1000, 100), ordered);
		Assert.Equal(100, unordered.Count);
		Assert.NotEqual(Enumerable.Range(0, 100), unordered);
		Assert.True(pair.Client.Reordered > 0);
	}

	[Fact]
	public void SequencedPacketsOlderThanADeliveredOneAreDropped()
	{
		using var pair = new Pair(new NetworkSimulation { Jitter = TimeSpan.FromMilliseconds(50), Seed = 8 });
		for (var i = 0; i < 100; i++)
		{
			pair.Send(i, Delivery.Sequenced);
			pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
		}

		pair.Clock.Advance(TimeSpan.FromSeconds(1));
		var received = pair.Drain();
		Assert.True(received.Count < 100);
		for (var i = 1; i < received.Count; i++) Assert.True(received[i] > received[i - 1]);
	}

	[Fact]
	public void TheSameSeedGivesTheSameRun()
	{
		static List<int> Run()
		{
			using var pair = new Pair(new NetworkSimulation { Latency = TimeSpan.FromMilliseconds(10), Jitter = TimeSpan.FromMilliseconds(20), Loss = 0.2, Reorder = 0.2, Seed = 42 });
			var all = new List<int>();
			for (var i = 0; i < 200; i++)
			{
				pair.Send(i, Delivery.Unreliable);
				pair.Clock.Advance(TimeSpan.FromMilliseconds(3));
				all.AddRange(pair.Drain());
			}

			return all;
		}

		Assert.Equal(Run(), Run());
	}

	[Fact]
	public void DisconnectsReachThePeerAndAClientWithoutServerFails()
	{
		using var pair = new Pair(new NetworkSimulation());
		pair.Client.Disconnect(0);
		Assert.True(pair.Server.TryReceive(pair.Buffer, out var e));
		Assert.Equal(TransportEventKind.Disconnected, e.Kind);

		var lonely = new LoopbackTransport(pair.Network, pair.Clock);
		lonely.StartClient("127.0.0.1", 1);
		Assert.True(lonely.TryReceive(pair.Buffer, out var failed));
		Assert.Equal(TransportEventKind.Disconnected, failed.Kind);
		lonely.Dispose();
	}
}
