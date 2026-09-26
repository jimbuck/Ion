using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Menu.Tests;

/// <summary>
/// The Stage 5b acceptance: the menu sample driven end to end by an agent through the remote protocol, without
/// screenshots. The game runs with <c>--remote-allow-mutations</c> on its own thread; the test is a plain JSON-RPC client
/// that reads the endpoint and token from the token file, finds widgets with <c>ui.tree</c> and acts with <c>ui.*</c>.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public sealed class MenuRemoteTests : IDisposable
{
	private readonly string _runDirectory = Path.Combine(Path.GetTempPath(), "ion-menu-remote", Guid.NewGuid().ToString("N"));
	private readonly IonTestHost _host;
	private readonly Thread _loop;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
	private volatile bool _stop;
	private int _id;

	public MenuRemoteTests()
	{
		_host = new IonTestHost()
			.WithArgs("--remote-allow-mutations")
			.WithConfiguration(new Dictionary<string, string?>
			{
				["Ion:Remote:Port"] = "0",
				["Ion:Remote:PrintToken"] = "false",
				["Ion:Remote:RunDirectory"] = _runDirectory,
			})
			.UseGame(b => MenuApp.Configure(b), a => MenuApp.Use(a))
			.Start();
		_loop = new Thread(() =>
		{
			while (!_stop && !_host.IsExitRequested)
			{
				_host.Step();
				Thread.Sleep(1);
			}
		}) { IsBackground = true, Name = "Menu game loop" };
		_loop.Start();

		// What an agent has: the token file.
		var info = JsonNode.Parse(File.ReadAllText(Path.Combine(_runDirectory, "remote.json")))!;
		Url = new Uri(info["url"]!.GetValue<string>());
		Token = info["mutateToken"]!.GetValue<string>();
	}

	private Uri Url { get; }

	private string Token { get; }

	private JsonNode? Call(string method, JsonObject? parameters = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = Interlocked.Increment(ref _id), ["method"] = method };
		if (parameters is not null) request["params"] = parameters;
		using var message = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json") };
		message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
		using var response = _http.Send(message);
		using var reader = new StreamReader(response.Content.ReadAsStream());
		var body = JsonNode.Parse(reader.ReadToEnd())!;
		Assert.True(body["error"] is null, $"{method} failed: {body["error"]?.ToJsonString()}");
		return body["result"];
	}

	private JsonObject[] Tree() => [.. Call("ui.tree")!["nodes"]!.AsArray().Select(static n => n!.AsObject())];

	private JsonObject Node(string path) => Tree().Single(n => n["path"]!.GetValue<string>() == path);

	/// <summary>Polls ui.tree until <paramref name="path"/> is there (a screen change shows from the frame after the click).</summary>
	private void WaitFor(string path)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (Tree().Any(n => n["path"]!.GetValue<string>() == path)) return;
			Thread.Sleep(5);
		}

		Assert.Fail($"'{path}' did not appear. Tree: {string.Join(", ", Tree().Select(static n => n["path"]))}");
	}

	private void WaitUntil(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!condition())
		{
			Assert.True(DateTime.UtcNow < deadline, what);
			Thread.Sleep(5);
		}
	}

	private static JsonObject At(string path) => new() { ["path"] = path };

	[Fact]
	public void AnAgentDrivesTheMenuThroughTheRemoteProtocol()
	{
		// The agent discovers the UI methods and reads the main menu.
		var methods = Call("rpc.discover")!["methods"]!.AsArray().Select(static m => m!["name"]!.GetValue<string>()).ToHashSet();
		Assert.Superset(new HashSet<string> { "ui.tree", "ui.click", "ui.set_value", "ui.focus", "ui.type", "ui.back" }, methods);
		WaitFor("main/Play");
		Assert.Equal(["main", "main/title", "main/greeting", "main/Play", "main/Options", "main/Quit"], Tree().Select(static n => n["path"]!.GetValue<string>()));
		Assert.Equal("Welcome, Player", Node("main/greeting")["text"]!.GetValue<string>());

		// Options: every widget changed by path.
		Call("ui.click", At("main/Options"));
		WaitFor("options/Back");
		Assert.Equal("options/Fullscreen", Call("ui.tree")!["focused"]!.GetValue<string>());
		Call("ui.click", At("options/Fullscreen"));
		Call("ui.set_value", new JsonObject { ["path"] = "options/Volume", ["value"] = 0.35 });
		Call("ui.click", At("options/Difficulty/Hard"));
		Call("ui.set_value", new JsonObject { ["path"] = "options/Name", ["value"] = "Ada" });
		WaitUntil(() => Node("options/Name")["value"]!.GetValue<string>() == "Ada", "The name was not set.");
		Call("ui.type", new JsonObject { ["path"] = "options/Name", ["text"] = " L." });
		WaitUntil(() => Node("options/Name")["value"]!.GetValue<string>() == "Ada L.", "The text was not typed.");
		Assert.Equal("true", Node("options/Fullscreen")["value"]!.GetValue<string>());
		Assert.Equal("0.35", Node("options/Volume")["value"]!.GetValue<string>());
		Assert.Equal("Hard", Node("options/Difficulty")["value"]!.GetValue<string>());
		Call("ui.focus", At("options/Back"));
		WaitUntil(() => Call("ui.tree")!["focused"]?.GetValue<string>() == "options/Back", "The focus did not move.");

		// The game state followed, as if a player had done it.
		var settings = _host.Get<MenuSettings>();
		Assert.True(settings.Fullscreen);
		Assert.Equal(0.35f, settings.Volume, 5);
		Assert.Equal(2, settings.Difficulty);
		Assert.Equal("Ada L.", settings.PlayerName);

		// Back to the main menu, into Play, and out with the back action.
		Call("ui.click", At("options/Back"));
		WaitFor("main/Play");
		Assert.Equal("Welcome, Ada L.", Node("main/greeting")["text"]!.GetValue<string>());
		Call("ui.click", At("main/Play"));
		WaitFor("play/Back");
		Assert.Equal("Ada L. on Hard", Node("play/status")["text"]!.GetValue<string>());
		Call("ui.back");
		WaitFor("main/Quit");

		// Quit ends the game.
		Call("ui.click", At("main/Quit"));
		WaitUntil(() => _host.IsExitRequested, "Quit did not end the game.");
	}

	public void Dispose()
	{
		_stop = true;
		_loop.Join(TimeSpan.FromSeconds(10));
		_http.Dispose();
		_host.Dispose();
		try
		{
			Directory.Delete(_runDirectory, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
