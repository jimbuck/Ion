using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;

using Ion.Extensions.Web;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Companion.Tests;

/// <summary>
/// The companion sample headless, driven the way a phone drives it: the test opens the game's WebSocket, sends stick
/// positions, and reads the score over HTTP, while the game runs on its own thread.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public sealed class CompanionTests : IDisposable
{
	private readonly IonTestHost _host;
	private readonly Thread _loop;
	private volatile bool _stop;
	private long _frames;

	public CompanionTests()
	{
		_host = new IonTestHost()
			.WithConfiguration(new Dictionary<string, string?> { ["Ion:Web:Port"] = "0", ["Ion:Web:PrintUrl"] = "false" })
			.UseGame(b => CompanionApp.Configure(b), a => CompanionApp.Use(a))
			.Start();
		Server = _host.Get<IWebServer>();
		Game = _host.Get<PaddleGame>();
		Http = new HttpClient { BaseAddress = new Uri(Server.BaseUrl!), Timeout = TimeSpan.FromSeconds(30) };
		_loop = new Thread(() =>
		{
			while (!_stop)
			{
				_host.Step();
				Interlocked.Increment(ref _frames);
				Thread.Sleep(1);
			}
		}) { IsBackground = true, Name = "Companion game loop" };
		_loop.Start();
	}

	private IWebServer Server { get; }

	private PaddleGame Game { get; }

	private HttpClient Http { get; }

	private void WaitFrames(int count)
	{
		var target = Interlocked.Read(ref _frames) + count;
		var deadline = DateTime.UtcNow.AddSeconds(20);
		while (Interlocked.Read(ref _frames) < target && DateTime.UtcNow < deadline) Thread.Sleep(1);
	}

	private static async Task<string> Receive(WebSocket socket)
	{
		using var cts = new CancellationTokenSource(10_000);
		var buffer = new byte[4096];
		var result = await socket.ReceiveAsync(buffer, cts.Token);
		return Encoding.UTF8.GetString(buffer, 0, result.Count);
	}

	[Fact]
	public async Task APhoneOnTheWebSocketMovesThePaddleAndScoreAnswers()
	{
		using var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol("ion");
		await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Server.Port}/paddle"), CancellationToken.None);
		Assert.Equal("ion", socket.SubProtocol);

		// The game greets the phone with the score and counts it.
		var greeting = await Receive(socket);
		Assert.Contains("\"controllers\":1", greeting, StringComparison.Ordinal);

		var start = Game.PaddleX;
		await socket.SendAsync("{\"x\":1}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
		WaitFrames(20);
		var right = Game.PaddleX;
		Assert.True(right > start + 50, $"The paddle should move right: {start} -> {right}.");

		await socket.SendAsync("{\"x\":-1}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
		WaitFrames(20);
		Assert.True(Game.PaddleX < right - 50, $"The paddle should move left: {right} -> {Game.PaddleX}.");

		// Releasing the stick stops it.
		await socket.SendAsync("{\"x\":0}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
		WaitFrames(5);
		var stopped = Game.PaddleX;
		WaitFrames(10);
		Assert.Equal(stopped, Game.PaddleX);

		var score = await Http.GetFromJsonAsync("/score", CompanionJson.Default.ScoreInfo);
		Assert.Equal(1, score.Controllers);
		Assert.Equal(Game.PaddleX, score.Paddle);
		Assert.Equal(Game.Score, score.Score);

		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (Game.Controllers != 0 && DateTime.UtcNow < deadline) await Task.Delay(5);
		Assert.Equal(0, Game.Controllers);
	}

	[Fact]
	public async Task TheControllerPageIsServed()
	{
		using var page = await Http.GetAsync("/");
		Assert.Equal(HttpStatusCode.OK, page.StatusCode);
		Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
		Assert.Contains("controller.js", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
		var script = await Http.GetStringAsync("/controller.js");
		Assert.Contains("/paddle", script, StringComparison.Ordinal);
		Assert.Contains("bearer.", script, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AtMostThreePhonesPlay()
	{
		var sockets = new List<ClientWebSocket>();
		try
		{
			for (var i = 0; i < 4; i++)
			{
				var socket = new ClientWebSocket();
				await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Server.Port}/paddle"), CancellationToken.None);
				sockets.Add(socket);
			}

			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (sockets[3].State == WebSocketState.Open && DateTime.UtcNow < deadline)
			{
				try
				{
					using var wait = new CancellationTokenSource(200);
					await sockets[3].ReceiveAsync(new byte[256], wait.Token);
				}
				catch (OperationCanceledException)
				{
				}
			}

			Assert.Equal((WebSocketCloseStatus)1013, sockets[3].CloseStatus);
			Assert.Equal(3, Game.Controllers);
		}
		finally
		{
			foreach (var socket in sockets) socket.Dispose();
		}
	}

	[Fact]
	public void TheStickParserReadsOnlyX()
	{
		Assert.True(CompanionEndpoints.TryReadStick("{\"y\":2,\"x\":0.25}"u8, out var x));
		Assert.Equal(0.25f, x);
		Assert.True(CompanionEndpoints.TryReadStick("{\"x\":7}"u8, out x));
		Assert.Equal(1f, x);
		Assert.False(CompanionEndpoints.TryReadStick("{\"x\":\"left\"}"u8, out _));
		Assert.False(CompanionEndpoints.TryReadStick("not json"u8, out _));
	}

	public void Dispose()
	{
		_stop = true;
		_loop.Join(TimeSpan.FromSeconds(10));
		Http.Dispose();
		_host.Dispose();
	}
}
