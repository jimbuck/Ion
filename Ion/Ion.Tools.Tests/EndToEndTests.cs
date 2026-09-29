using Ion.Tests;

namespace Ion.Tools.Tests;

/// <summary>
/// The ion tool against the Breakout sample: a headless run with summary and screenshot, the exit code of a failing run,
/// the schedule, and the MCP server driving a live game through the remote protocol. Uses the configuration the tests
/// were built with (the sample keeps the remote module in Release: IonRemote=true).
/// </summary>
[Collection(DotnetBuildCollection.Name)]
[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class EndToEndTests : IDisposable
{
	private readonly string _dir = Repo.TempDirectory("e2e");

	public void Dispose()
	{
		try
		{
			Directory.Delete(_dir, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	[Fact]
	public void RunBreakoutHeadlessWritesTheSummaryAndScreenshot()
	{
		var summary = Path.Combine(_dir, "run.json");
		var screenshot = Path.Combine(_dir, "frame.png");
		string[] args = ["run", Repo.Breakout, "-c", Repo.Configuration, "--headless", "--frames", "60", "--seed", "3", "--summary", summary];
		if (Repo.CanRender) args = [.. args, "--screenshot", screenshot];

		var (code, output) = Repo.Ion(_dir, args);
		Assert.True(code == 0, output);

		var json = JsonNode.Parse(File.ReadAllText(summary))!;
		Assert.Equal("ok", json["status"]!.GetValue<string>());
		Assert.Equal(0, json["exitCode"]!.GetValue<int>());
		Assert.Equal(60, json["frames"]!.GetValue<int>());
		Assert.Equal(3, json["seed"]!.GetValue<int>());
		Assert.True(json["fixedStep"]!.GetValue<bool>());
		Assert.Equal(60, json["frameStats"]!["count"]!.GetValue<int>());
		Assert.True(json["frameStats"]!["totalSprites"]!.GetValue<long>() > 0);
		Assert.Contains("BreakoutSystems.Update", json["schedule"]!.GetValue<string>(), StringComparison.Ordinal);
		Assert.Empty(json["errors"]!.AsArray());
		Assert.Null(json["exception"]);

		if (Repo.CanRender)
		{
			Assert.True(File.Exists(screenshot));
			Assert.Equal(screenshot, json["screenshot"]!.GetValue<string>());
			// The same seed and frame count give the same image.
			var again = Path.Combine(_dir, "again.png");
			Assert.Equal(0, Repo.Ion(_dir, "run", Repo.Breakout, "-c", Repo.Configuration, "--headless", "--frames", "60", "--seed", "3", "--screenshot", again).ExitCode);
			var (diffCode, diffOutput) = Repo.Ion(_dir, "diff", again, screenshot);
			Assert.True(diffCode == 0, diffOutput);
		}
	}

	[Fact]
	public void ExitCodeAndSummaryReflectAnException()
	{
		var summary = Path.Combine(_dir, "failed.json");
		// A reserved graphics backend throws NotSupportedException while the game is being set up.
		var (code, output) = Repo.Ion(_dir, "run", Repo.Breakout, "-c", Repo.Configuration, "--headless", "--render", "--frames", "5", "--summary", summary,
			"--", "--Ion:Graphics:PreferredBackend=Metal");
		Assert.NotEqual(0, code);
		Assert.Contains("NotSupportedException", output, StringComparison.Ordinal);

		var json = JsonNode.Parse(File.ReadAllText(summary))!;
		Assert.NotEqual("ok", json["status"]!.GetValue<string>());
		Assert.Equal(code, json["exitCode"]!.GetValue<int>());
	}

	[Fact]
	public void SchedulePrintsTheStages()
	{
		var (code, output) = Repo.Ion(_dir, "schedule", Repo.Breakout, "-c", Repo.Configuration);
		Assert.True(code == 0, output);
		Assert.Contains("Schedule root", output, StringComparison.Ordinal);
		Assert.Contains("BreakoutSystems.Render", output, StringComparison.Ordinal);
	}

	[Fact]
	public void McpDrivesALiveGame()
	{
		using var server = new McpServer(TextReader.Null, TextWriter.Null, _dir);
		var id = 0;
		JsonObject Tool(string name, JsonObject? arguments = null)
		{
			var request = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["id"] = ++id,
				["method"] = "tools/call",
				["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments ?? [] },
			};
			var result = server.Handle(request.ToJsonString())!["result"]!.AsObject();
			Assert.False(result["isError"]?.GetValue<bool>() ?? false, $"{name}: {result["content"]![0]!["text"]}");
			return result;
		}

		static JsonNode TextOf(JsonObject result) => JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;

		var started = TextOf(Tool("ion_run", new JsonObject
		{
			["project"] = Repo.Breakout,
			["configuration"] = Repo.Configuration,
			["live"] = true,
			["frames"] = 30,
			["render"] = Repo.CanRender,
			["args"] = new JsonArray("--Ion:Remote:PrintToken=false"),
		}));
		try
		{
			Assert.True(started["info"]!["paused"]!.GetValue<bool>());
			Assert.Equal(29, started["info"]!["frame"]!.GetValue<int>());
			Assert.Equal("mutate", started["info"]!["access"]!.GetValue<string>());

			var discover = TextOf(Tool("ion_call", new JsonObject { ["method"] = "rpc.discover" }));
			Assert.Contains(discover["methods"]!.AsArray(), m => m!["name"]!.GetValue<string>() == "input.send");

			// A pointer click is a move, a press and a release.
			Assert.Equal(3, TextOf(Tool("ion_input", new JsonObject { ["events"] = new JsonArray(new JsonObject { ["type"] = "pointer", ["x"] = 100, ["y"] = 200, ["action"] = "click" }) }))["queued"]!.GetValue<int>());
			var stepped = TextOf(Tool("ion_step", new JsonObject { ["frames"] = 5 }));
			Assert.Equal(34, stepped["frame"]!.GetValue<int>());

			var metrics = TextOf(Tool("ion_metrics"));
			Assert.True(metrics["sprites"]!.GetValue<int>() > 0);
			Assert.Contains("BreakoutSystems", Tool("ion_schedule")["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);

			if (Repo.CanRender)
			{
				var path = Path.Combine(_dir, "live.png");
				var shot = Tool("ion_screenshot", new JsonObject { ["path"] = path });
				Assert.Equal("image", shot["content"]![1]!["type"]!.GetValue<string>());
				Assert.True(File.Exists(path));
			}
		}
		finally
		{
			var stopped = TextOf(Tool("ion_stop"));
			Assert.True(stopped["stopped"]!.GetValue<bool>());
		}
	}
}
