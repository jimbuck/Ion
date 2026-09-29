using System.Net;
using System.Net.WebSockets;
using System.Text;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>WebSocket endpoints: messages to the game thread, replies, and pushes through channels.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class WebSocketTests
{
	internal static async Task<ClientWebSocket> Connect(WebGame game, string path, Action<ClientWebSocketOptions>? options = null)
	{
		var socket = new ClientWebSocket();
		options?.Invoke(socket.Options);
		await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{game.Port}{path}"), CancellationToken.None);
		return socket;
	}

	internal static async Task<string> Receive(WebSocket socket, int timeoutMs = 10_000)
	{
		using var cts = new CancellationTokenSource(timeoutMs);
		var buffer = new byte[65536];
		var total = 0;
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer.AsMemory(total), cts.Token);
			if (result.MessageType == WebSocketMessageType.Close) return "<close>";
			total += result.Count;
			if (result.EndOfMessage) return Encoding.UTF8.GetString(buffer, 0, total);
		}
	}

	internal static Task Send(WebSocket socket, string text) => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

	[Fact]
	public async Task MessagesReachTheGameThreadAndRepliesComeBack()
	{
		using var game = new WebGame();
		using var socket = await Connect(game, "/echo");
		for (var i = 0; i < 20; i++)
		{
			await Send(socket, "m" + i);
			Assert.Equal("m" + i, await Receive(socket));
		}

		// A large message, fragmented by the client.
		var large = new string('x', 40_000);
		await Send(socket, large);
		Assert.Equal(large, await Receive(socket));

		var log = game.System.Snapshot();
		Assert.Equal(21, log.Length);
		Assert.All(log, h => Assert.Equal(game.System.GameThread, h.Thread));
		Assert.All(log, h => Assert.True(h.InWebStep));
		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
	}

	[Fact]
	public async Task ChannelsPushToEveryClient()
	{
		using var game = new WebGame();
		using var a = await Connect(game, "/events");
		using var b = await Connect(game, "/events/");
		var channel = game.Server.Channel("/events");
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (channel.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(5);
		Assert.Equal(2, channel.Count);
		Assert.Equal(2, game.System.Connections);

		Assert.Equal(2, channel.Broadcast("tick 1"));
		Assert.Equal(2, channel.BroadcastJson(new ScoreInfo(9, 3), TestJson.Default.ScoreInfo));
		Assert.Equal("tick 1", await Receive(a));
		Assert.Equal("tick 1", await Receive(b));
		Assert.Equal("""{"Score":9,"Frame":3}""", await Receive(a));
		Assert.Equal("""{"Score":9,"Frame":3}""", await Receive(b));

		await a.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
		deadline = DateTime.UtcNow.AddSeconds(10);
		while (channel.Count > 1 && DateTime.UtcNow < deadline) await Task.Delay(5);
		Assert.Equal(1, channel.Count);
		Assert.Equal(1, game.System.Disconnections);
		Assert.Throws<KeyNotFoundException>(() => game.Server.Channel("/nope"));
	}

	[Fact]
	public async Task UnknownPathsAndOversizedMessagesAreRefused()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:MaxWebSocketMessageBytes"] = "1024" });
		var error = await Assert.ThrowsAsync<WebSocketException>(() => Connect(game, "/nowhere"));
		Assert.Contains("404", error.Message, StringComparison.Ordinal);

		using var socket = await Connect(game, "/echo");
		await Send(socket, new string('y', 2048));
		Assert.Equal("<close>", await Receive(socket));
		Assert.Equal((WebSocketCloseStatus)1009, socket.CloseStatus);
	}

	[Fact]
	public async Task TheIonSubprotocolIsEchoed()
	{
		using var game = new WebGame();
		using var socket = await Connect(game, "/echo", o => o.AddSubProtocol("ion"));
		Assert.Equal("ion", socket.SubProtocol);
	}
}
