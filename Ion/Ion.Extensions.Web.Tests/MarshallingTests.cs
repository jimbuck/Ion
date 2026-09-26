using System.Net;
using System.Net.Sockets;
using System.Text;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>Requests cross to the game thread and run inside the web step, in arrival order, at a bounded rate per frame.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class MarshallingTests
{
	[Fact]
	public async Task HandlersRunOnTheGameThreadInsideTheWebStepInArrivalOrder()
	{
		using var game = new WebGame();
		using var socket = await WebSocketTests.Connect(game, "/echo");
		for (var i = 0; i < 30; i++)
		{
			using var response = await game.Http.PostAsync($"/order?n={i}", null);
			Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
			await WebSocketTests.Send(socket, "w" + i);
			Assert.Equal("w" + i, await WebSocketTests.Receive(socket));
		}

		var log = game.System.Snapshot();
		Assert.Equal(60, log.Length);
		Assert.All(log, h => Assert.Equal(game.System.GameThread, h.Thread));
		Assert.All(log, static h => Assert.True(h.InWebStep, $"{h.What} ran outside the web step."));
		// Each request completed before the next was sent, so the log is the send order.
		Assert.Equal(Enumerable.Range(0, 30).SelectMany(static i => new[] { "http " + i, "ws w" + i }), log.Select(static h => h.What));
		Assert.True(log.Zip(log.Skip(1)).All(static p => p.First.Frame <= p.Second.Frame));
	}

	[Fact]
	public void ConcurrentClientsAreEachServedInOrder()
	{
		using var game = new WebGame();
		const int Clients = 4, PerClient = 25;
		var threads = Enumerable.Range(0, Clients).Select(c => new Thread(() =>
		{
			using var client = new TcpClient("127.0.0.1", game.Port);
			var stream = client.GetStream();
			var buffer = new byte[1024];
			for (var i = 0; i < PerClient; i++)
			{
				stream.Write(Encoding.ASCII.GetBytes($"POST /order?n={c * 1000 + i} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\n\r\n"));
				var read = stream.Read(buffer);
				Assert.StartsWith("HTTP/1.1 204", Encoding.ASCII.GetString(buffer, 0, read), StringComparison.Ordinal);
			}
		})).ToList();
		threads.ForEach(static t => t.Start());
		threads.ForEach(static t => t.Join());

		var log = game.System.Snapshot();
		Assert.Equal(Clients * PerClient, log.Length);
		for (var c = 0; c < Clients; c++)
		{
			var mine = log.Select(static h => int.Parse(h.What[5..], System.Globalization.CultureInfo.InvariantCulture)).Where(n => n / 1000 == c).ToList();
			Assert.Equal(Enumerable.Range(c * 1000, PerClient), mine);
		}
	}

	[Fact]
	public void AtMostTheConfiguredNumberOfRequestsRunPerFrame()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:MaxRequestsPerFrame"] = "1" }, background: false);
		var server = game.Server;
		// Three clients queue a request each while no frame runs.
		var clients = Enumerable.Range(0, 3).Select(i => Task.Run(() => game.Raw($"POST /order?n={i} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"))).ToArray();
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (server.RequestCount < 3 && DateTime.UtcNow < deadline) Thread.Sleep(5);
		Thread.Sleep(50);

		game.Host.Step();
		Assert.Single(game.System.Snapshot());
		game.Host.Step();
		Assert.Equal(2, game.System.Snapshot().Length);
		game.Host.Step();
		Assert.Equal(3, game.System.Snapshot().Length);
		deadline = DateTime.UtcNow.AddSeconds(10);
		while (!clients.All(static c => c.IsCompleted) && DateTime.UtcNow < deadline) Thread.Sleep(5);
		Assert.All(clients, static c => Assert.True(c.IsCompletedSuccessfully));
		var frames = game.System.Snapshot().Select(static h => h.Frame).ToArray();
		Assert.Equal(3, frames.Distinct().Count());
	}

	[Fact]
	public void ARequestTheGameNeverAnswersTimesOut()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:RequestTimeoutMs"] = "200" }, background: false);
		var response = game.Raw("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 504", response, StringComparison.Ordinal);
		// The abandoned request is skipped when the game catches up.
		game.Host.Step();
		Assert.Empty(game.System.Snapshot());
	}
}
