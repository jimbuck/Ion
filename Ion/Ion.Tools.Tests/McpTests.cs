using System.Diagnostics;
using System.Text;

using Ion.Tests;

namespace Ion.Tools.Tests;

[Trait(TestConstants.CATEGORY, TestConstants.UNIT)]
public sealed class McpServerTests
{
	private static JsonObject Handle(McpServer server, string json) => server.Handle(json)!;

	[Fact]
	public void InitializeNegotiatesTheProtocolVersion()
	{
		using var server = new McpServer(TextReader.Null, TextWriter.Null);
		var result = Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{}}}")["result"]!;
		Assert.Equal("2025-03-26", result["protocolVersion"]!.GetValue<string>());
		Assert.Equal("ion", result["serverInfo"]!["name"]!.GetValue<string>());
		Assert.NotNull(result["capabilities"]!["tools"]);

		var unknown = Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"1999-01-01\"}}")["result"]!;
		Assert.Equal(McpServer.ProtocolVersions[0], unknown["protocolVersion"]!.GetValue<string>());
	}

	[Fact]
	public void ToolsAreListedWithSchemas()
	{
		using var server = new McpServer(TextReader.Null, TextWriter.Null);
		var tools = Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}")["result"]!["tools"]!.AsArray();
		var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
		foreach (var name in (string[])["ion_run", "ion_connect", "ion_stop", "ion_call", "ion_query", "ion_get", "ion_mutate", "ion_spawn", "ion_despawn", "ion_screenshot", "ion_input", "ion_step", "ion_pause", "ion_resume", "ion_schedule", "ion_metrics", "ion_logs", "ion_events", "ion_diff"])
		{
			Assert.Contains(name, names);
		}

		foreach (var tool in tools)
		{
			Assert.Equal("object", tool!["inputSchema"]!["type"]!.GetValue<string>());
			Assert.False(string.IsNullOrWhiteSpace(tool["description"]!.GetValue<string>()));
		}
	}

	[Fact]
	public void NotificationsGetNoResponseAndUnknownMethodsAnError()
	{
		using var server = new McpServer(TextReader.Null, TextWriter.Null);
		Assert.Null(server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
		Assert.Equal(-32601, Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"resources/list\"}")["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32700, Handle(server, "{oops")["error"]!["code"]!.GetValue<int>());
		Assert.NotNull(Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}")["result"]);
	}

	[Fact]
	public void ToolsWithoutAGameReportAToolError()
	{
		using var server = new McpServer(TextReader.Null, TextWriter.Null);
		var result = Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"ion_query\",\"arguments\":{}}}")["result"]!;
		Assert.True(result["isError"]!.GetValue<bool>());
		Assert.Contains("ion_run", result["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);

		var unknown = Handle(server, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"ion_fly\"}}");
		Assert.Equal(-32602, unknown["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public void ConnectReadsTheTokenFile()
	{
		var dir = Repo.TempDirectory("connect");
		using var server = new McpServer(TextReader.Null, TextWriter.Null, dir);
		var missing = Handle(server, $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"ion_connect\",\"arguments\":{{\"runDirectory\":\"{dir}\"}}}}}}")["result"]!;
		Assert.True(missing["isError"]!.GetValue<bool>());
		Assert.Contains("remote.json", missing["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
		Directory.Delete(dir, recursive: true);
	}

	[Fact]
	public void TheToolServesMcpOverStdio()
	{
		var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add(Repo.ToolDll);
		start.ArgumentList.Add("mcp");
		using var process = Process.Start(start)!;
		var input = process.StandardInput;
		var output = process.StandardOutput;

		JsonObject Request(string json)
		{
			input.WriteLine(json);
			input.Flush();
			var line = output.ReadLine() ?? throw new InvalidOperationException("The MCP server closed its output: " + process.StandardError.ReadToEnd());
			return JsonNode.Parse(line)!.AsObject();
		}

		var init = Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
		Assert.Equal(1, init["id"]!.GetValue<int>());
		Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());

		input.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
		var tools = Request("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
		Assert.Contains(tools["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "ion_run");

		var noGame = Request("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"ion_metrics\",\"arguments\":{}}}");
		Assert.True(noGame["result"]!["isError"]!.GetValue<bool>());

		input.Close();
		Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)));
		Assert.Equal(0, process.ExitCode);
	}
}
