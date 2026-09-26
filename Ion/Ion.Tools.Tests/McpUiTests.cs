using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.UI;
using Ion.Testing;
using Ion.Tests;

namespace Ion.Tools.Tests;

/// <summary>A one-button screen for the MCP UI tools.</summary>
public sealed class ButtonScreen(Ui ui)
{
	public int Clicks;

	[Update]
	public void Build(GameTime dt)
	{
		using (ui.Panel("menu"))
		{
			ui.Label("Hello", "title");
			if (ui.Button("Start")) Clicks++;
		}
	}
}

/// <summary>ion_ui_tree and ion_ui_click against a live game in this process, connected through its token file.</summary>
[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class McpUiTests
{
	[Fact]
	public void TheUiToolsReadTheTreeAndClickByPath()
	{
		var runDirectory = Repo.TempDirectory("mcp-ui");
		using var host = new IonTestHost()
			.WithArgs("--remote-allow-mutations")
			.WithConfiguration(new Dictionary<string, string?> { ["Ion:Remote:Port"] = "0", ["Ion:Remote:PrintToken"] = "false", ["Ion:Remote:RunDirectory"] = runDirectory })
			.Configure(static s => s.AddUi().AddUiRemote().AddSingleton<ButtonScreen>())
			.ConfigureApp(static a => a.UseUi().UseSystem<ButtonScreen>())
			.Start();
		var stop = false;
		var loop = new Thread(() =>
		{
			while (!Volatile.Read(ref stop))
			{
				host.Step();
				Thread.Sleep(1);
			}
		}) { IsBackground = true };
		loop.Start();
		try
		{
			using var server = new McpServer(TextReader.Null, TextWriter.Null, runDirectory);
			JsonObject Tool(string name, JsonObject arguments)
			{
				var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments } };
				var result = server.Handle(request.ToJsonString())!["result"]!.AsObject();
				Assert.False(result["isError"]?.GetValue<bool>() ?? false, result.ToJsonString());
				return result;
			}

			Tool("ion_connect", new JsonObject { ["runDirectory"] = runDirectory });
			var tree = JsonNode.Parse(Tool("ion_ui_tree", new JsonObject())["content"]![0]!["text"]!.GetValue<string>())!;
			Assert.Equal(["menu", "menu/title", "menu/Start"], tree["nodes"]!.AsArray().Select(static n => n!["path"]!.GetValue<string>()));
			var filtered = JsonNode.Parse(Tool("ion_ui_tree", new JsonObject { ["prefix"] = "menu/S" })["content"]![0]!["text"]!.GetValue<string>())!;
			Assert.Single(filtered["nodes"]!.AsArray());

			var click = JsonNode.Parse(Tool("ion_ui_click", new JsonObject { ["path"] = "menu/Start" })["content"]![0]!["text"]!.GetValue<string>())!;
			Assert.True(click["queued"]!.GetValue<bool>());
			var screen = host.Get<ButtonScreen>();
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (Volatile.Read(ref screen.Clicks) == 0 && DateTime.UtcNow < deadline) Thread.Sleep(5);
			Assert.Equal(1, screen.Clicks);

			var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "ion_ui_click", ["arguments"] = new JsonObject { ["path"] = "menu/Nope" } } };
			var missing = server.Handle(request.ToJsonString())!["result"]!;
			Assert.True(missing["isError"]!.GetValue<bool>());
			Assert.Contains("menu/Nope", missing["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
		}
		finally
		{
			Volatile.Write(ref stop, true);
			loop.Join(TimeSpan.FromSeconds(10));
			try
			{
				Directory.Delete(runDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}
}
