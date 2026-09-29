using System.IO.Pipes;
using System.Net.WebSockets;
using System.Text;

using Ion.Tests;

namespace Ion.Extensions.Remote.Tests;

[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class TransportTests
{
	/// <summary>A client end of the stdio transport over two anonymous pipes.</summary>
	private sealed class StdioClient : IDisposable
	{
		private readonly AnonymousPipeServerStream _toGame = new(PipeDirection.Out);
		private readonly AnonymousPipeServerStream _fromGame = new(PipeDirection.In);
		private readonly StreamReader _reader;

		public StdioClient(RemoteServer server)
		{
			var gameInput = new AnonymousPipeClientStream(PipeDirection.In, _toGame.ClientSafePipeHandle);
			var gameOutput = new AnonymousPipeClientStream(PipeDirection.Out, _fromGame.ClientSafePipeHandle);
			server.AttachStream(gameInput, gameOutput, "test");
			_reader = new StreamReader(_fromGame, Encoding.UTF8);
		}

		public void Send(string line)
		{
			var bytes = Encoding.UTF8.GetBytes(line + "\n");
			_toGame.Write(bytes);
			_toGame.Flush();
		}

		public Task<string?> ReadLineAsync() => _reader.ReadLineAsync();

		public void Dispose()
		{
			_toGame.Dispose();
			_fromGame.Dispose();
		}
	}

	private static JsonObject Await(RemoteGame game, Task<string?> line)
	{
		var text = game.Pump(() => line.Wait(TimeSpan.FromSeconds(20)) ? line.Result : null);
		Assert.NotNull(text);
		return JsonNode.Parse(text!)!.AsObject();
	}

	[Fact]
	public void StdioTransportServesNewlineDelimitedJsonWithoutAToken()
	{
		using var game = new RemoteGame(mutations: true);
		using var client = new StdioClient(game.Server);

		client.Send("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"game.info\"}");
		var info = Await(game, client.ReadLineAsync());
		Assert.Equal(1, info["id"]!.GetValue<int>());
		Assert.Equal("mutate", info["result"]!["access"]!.GetValue<string>());

		client.Send("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"world.spawn\",\"params\":{\"name\":\"FromStdio\"}}");
		var spawned = Await(game, client.ReadLineAsync());
		Assert.Null(spawned["error"]);
		Assert.Equal(3, game.World.Size);

		client.Send("not json");
		Assert.Equal(RemoteErrorCodes.ParseError, RemoteGame.ErrorCode(Await(game, client.ReadLineAsync())));
	}

	[Fact]
	public void StdioSessionsAreReadOnlyWithoutAllowMutations()
	{
		using var game = new RemoteGame(mutations: false);
		using var client = new StdioClient(game.Server);
		client.Send("{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"method\":\"world.despawn\",\"params\":{\"entity\":\"Ball\"}}");
		var response = Await(game, client.ReadLineAsync());
		Assert.Equal(RemoteErrorCodes.Forbidden, RemoteGame.ErrorCode(response));
		Assert.Equal(2, game.World.Size);
	}

	[Fact]
	public void StdioWatchStreamsChangesUntilUnwatched()
	{
		using var game = new RemoteGame();
		using var client = new StdioClient(game.Server);
		client.Send("{\"jsonrpc\":\"2.0\",\"id\":\"w\",\"method\":\"world.get_components+watch\",\"params\":{\"entity\":\"Ball\",\"components\":[\"Transform2D\"]}}");
		var first = Await(game, client.ReadLineAsync());
		Assert.Equal("w", first["id"]!.GetValue<string>());
		Assert.Equal(10, first["result"]!["components"]!["Transform2D"]!["Position"]![0]!.GetValue<float>());

		// No change, no message; a change sends the new value with the watch's id.
		game.Host.Step(3);
		game.World.Set(EcsEntitiesForTests.Find(game.World, "Ball"), new Ion.Extensions.Ecs.Transform2D(new Vector2(33, 20)));
		var update = Await(game, client.ReadLineAsync());
		Assert.Equal("w", update["id"]!.GetValue<string>());
		Assert.Equal(33, update["result"]!["components"]!["Transform2D"]!["Position"]![0]!.GetValue<float>());

		client.Send("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"rpc.unwatch\",\"params\":{\"id\":\"w\"}}");
		var unwatched = Await(game, client.ReadLineAsync());
		Assert.Equal(1, unwatched["result"]!["removed"]!.GetValue<int>());
	}

	[Fact]
	public void WatchOverPlainHttpIsUnsupported()
	{
		using var game = new RemoteGame();
		Assert.Equal(RemoteErrorCodes.Unsupported, RemoteGame.ErrorCode(game.Call("metrics.get+watch")));
		Assert.Equal(RemoteErrorCodes.InvalidRequest, RemoteGame.ErrorCode(game.Call("game.pause+watch")));
	}

	[Fact]
	public async Task WebSocketNeedsATokenAndStreamsWatches()
	{
		using var game = new RemoteGame().RunInBackground();
		var ws = new Uri($"ws://127.0.0.1:{game.Server.Port}/ws");

		using (var anonymous = new ClientWebSocket())
		{
			var refused = await Assert.ThrowsAnyAsync<WebSocketException>(() => anonymous.ConnectAsync(ws, CancellationToken.None));
			Assert.NotNull(refused);
		}

		using var socket = new ClientWebSocket();
		socket.Options.SetRequestHeader("Authorization", $"Bearer {game.Server.ReadToken}");
		await socket.ConnectAsync(ws, CancellationToken.None);

		await Send(socket, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"world.query\",\"params\":{\"name\":\"Paddle\"}}");
		var query = await Receive(socket);
		Assert.Equal("Paddle", query["result"]!["entities"]![0]!["name"]!.GetValue<string>());

		// Read session: mutations are forbidden over the socket too.
		await Send(socket, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"world.despawn\",\"params\":{\"entity\":\"Paddle\"}}");
		Assert.Equal(RemoteErrorCodes.Forbidden, RemoteGame.ErrorCode(await Receive(socket)));

		await Send(socket, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"game.info+watch\"}");
		var first = await Receive(socket);
		var second = await Receive(socket); // the frame number changes every frame
		Assert.Equal(3, first["id"]!.GetValue<int>());
		Assert.Equal(3, second["id"]!.GetValue<int>());
		Assert.NotEqual(first["result"]!["frame"]!.GetValue<uint>(), second["result"]!["frame"]!.GetValue<uint>());

		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
	}

	private static Task Send(ClientWebSocket socket, string text) =>
		socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

	private static async Task<JsonObject> Receive(ClientWebSocket socket)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var buffer = new byte[1 << 20];
		var total = 0;
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer.AsMemory(total), timeout.Token);
			total += result.Count;
			if (result.EndOfMessage) break;
		}

		return JsonNode.Parse(buffer.AsSpan(0, total))!.AsObject();
	}
}
