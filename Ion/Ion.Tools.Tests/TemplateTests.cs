using System.Diagnostics;
using System.Text;

using Ion.Tests;

namespace Ion.Tools.Tests;

/// <summary>
/// The templates compile and their own tests pass, built against this repository's sources (ion new --ion-source), and
/// the Stage 6 acceptance scenario (docs/agentic/acceptance.md) runs with only the CLI and the MCP server. Slow: each game
/// builds the engine from source (Debug), so these run only with ION_SLOW_TESTS=1.
/// </summary>
[Collection(DotnetBuildCollection.Name)]
[Trait(TestConstants.CATEGORY, "Slow")]
public sealed class TemplateTests : IDisposable
{
	private readonly string _dir = Repo.TempDirectory("templates");

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

	[SlowFact]
	public void EveryTemplateBuildsAndPassesItsTests()
	{
		foreach (var kind in Templates.Kinds)
		{
			var name = $"Game{kind.ToUpperInvariant()}";
			var (created, createOutput) = Repo.Ion(_dir, "new", kind, name, "--ion-source", Repo.Root);
			Assert.True(created == 0, createOutput);

			var root = Path.Combine(_dir, name);
			var (built, buildOutput) = Dotnet(root, "build", "-warnaserror");
			Assert.True(built == 0, $"{kind}: {buildOutput}");

			var (tested, testOutput) = Dotnet(root, "test", "--no-build");
			Assert.True(tested == 0, $"{kind}: {testOutput}");
		}
	}

	[SlowFact]
	public void AcceptanceScenarioWithOnlyTheCliAndMcp()
	{
		// 1. Create a game from the template.
		Assert.Equal(0, Repo.Ion(_dir, "new", "ecs", "Arena", "--ion-source", Repo.Root).ExitCode);
		var root = Path.Combine(_dir, "Arena");
		var game = Path.Combine(root, "Arena");

		// 2. Add a system: gravity on every ball, counted.
		File.WriteAllText(Path.Combine(game, "GravitySystem.cs"), """
			using Ion;
			using Ion.Extensions.Ecs;
			using Ion.Extensions.Metrics;

			namespace Arena;

			public sealed partial class GravitySystem(IMetrics metrics)
			{
				private readonly MetricsCounter _pulls = metrics.Counter("gravity");

				[FixedUpdate(Order = -10), Query, All<Ball>]
				private void Pull(ref Velocity velocity, [Data] in float dt)
				{
					velocity.Y += 200f * dt;
					_pulls.Increment();
				}
			}
			""");
		var gameFile = Path.Combine(game, "Game.cs");
		var source = File.ReadAllText(gameFile)
			.Replace("builder.Services.AddSingleton<DrawSystem>();", "builder.Services.AddSingleton<DrawSystem>();\n\t\tbuilder.Services.AddSingleton<GravitySystem>();", StringComparison.Ordinal)
			.Replace(".UseSystem<DrawSystem>();", ".UseSystem<DrawSystem>()\n\t\t\t.UseSystem<GravitySystem>();", StringComparison.Ordinal);
		File.WriteAllText(gameFile, source);

		// 3. Run 600 headless frames with a screenshot and a summary.
		var screenshot = Repo.CanRender ? new[] { "--screenshot", "out/frame600.png" } : [];
		var (runCode, runOutput) = Repo.Ion(root, ["run", "--headless", "--frames", "600", "--seed", "1", "--summary", "out/run.json", .. screenshot]);
		Assert.True(runCode == 0, runOutput);
		var summary = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "out", "run.json")))!;
		Assert.Equal("ok", summary["status"]!.GetValue<string>());
		Assert.Equal(600, summary["frames"]!.GetValue<int>());
		Assert.True(summary["counters"]!["gravity"]!.GetValue<long>() > 0, "The new system ran.");

		// 4. Diff the screenshot against a golden image (the first run's, committed by the agent; a second run must match).
		if (Repo.CanRender)
		{
			Directory.CreateDirectory(Path.Combine(root, "Golden"));
			File.Copy(Path.Combine(root, "out", "frame600.png"), Path.Combine(root, "Golden", "frame600.png"));
			Assert.Equal(0, Repo.Ion(root, "run", "--headless", "--frames", "600", "--seed", "1", "--screenshot", "out/again.png").ExitCode);
			var (diff, diffOutput) = Repo.Ion(root, "diff", "out/again.png", "Golden/frame600.png");
			Assert.True(diff == 0, diffOutput);
		}

		// 5. Inspect an entity and mutate a component through the MCP server.
		using var mcp = new McpServer(TextReader.Null, TextWriter.Null, root);
		var id = 0;
		JsonNode Tool(string name, JsonObject arguments)
		{
			var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++id, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments } };
			var result = mcp.Handle(request.ToJsonString())!["result"]!;
			Assert.False(result["isError"]?.GetValue<bool>() ?? false, $"{name}: {result["content"]![0]!["text"]}");
			return JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
		}

		var started = Tool("ion_run", new JsonObject { ["live"] = true, ["frames"] = 600, ["seed"] = 1, ["render"] = Repo.CanRender });
		try
		{
			Assert.Equal(599, started["info"]!["frame"]!.GetValue<int>());
			var balls = Tool("ion_query", new JsonObject { ["with"] = new JsonArray("Ball"), ["components"] = new JsonArray("Transform2D", "Velocity") });
			Assert.Equal(8, balls["total"]!.GetValue<int>());

			var ball = Tool("ion_get", new JsonObject { ["entity"] = "Ball0" });
			Assert.NotNull(ball["components"]!["Velocity"]);

			Tool("ion_mutate", new JsonObject { ["entity"] = "Ball0", ["component"] = "Transform2D", ["path"] = "Position", ["value"] = new JsonArray(100, 100) });
			Tool("ion_mutate", new JsonObject { ["entity"] = "Ball0", ["components"] = new JsonObject { ["Velocity"] = new JsonObject { ["X"] = 0, ["Y"] = 0 } } });
			var after = Tool("ion_get", new JsonObject { ["entity"] = "Ball0", ["components"] = new JsonArray("Transform2D") });
			Assert.Equal(100, after["components"]!["Transform2D"]!["Position"]![0]!.GetValue<float>());

			// One frame later gravity has pulled it down by 200 * (1/60)^2.
			Tool("ion_step", new JsonObject { ["frames"] = 1 });
			var moved = Tool("ion_get", new JsonObject { ["entity"] = "Ball0", ["components"] = new JsonArray("Transform2D") });
			Assert.InRange(moved["components"]!["Transform2D"]!["Position"]![1]!.GetValue<float>(), 100.01f, 100.2f);
		}
		finally
		{
			Tool("ion_stop", []);
		}
	}

	private static (int ExitCode, string Output) Dotnet(string workingDirectory, params string[] args)
	{
		var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var arg in args) start.ArgumentList.Add(arg);
		start.ArgumentList.Add("--disable-build-servers");
		// Reused build nodes would inherit the output pipes and keep them open after the command ends.
		start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
		using var process = Process.Start(start)!;
		var output = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return (process.ExitCode, output.ToString());
	}
}
