using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ion.Tools;

/// <summary>
/// A Model Context Protocol server on stdio (newline-delimited JSON-RPC 2.0) that lets a coding agent run Ion games and
/// drive a running one through the remote inspection protocol: <c>ion_run</c> (a headless run with screenshot and summary,
/// or a live game with the remote server on), <c>ion_connect</c>, <c>ion_query</c>, <c>ion_get</c>, <c>ion_mutate</c>,
/// <c>ion_spawn</c>, <c>ion_despawn</c>, <c>ion_screenshot</c>, <c>ion_input</c>, <c>ion_step</c>, <c>ion_pause</c>,
/// <c>ion_resume</c>, <c>ion_schedule</c>, <c>ion_metrics</c>, <c>ion_logs</c>, <c>ion_events</c>, <c>ion_call</c> (any
/// remote method), <c>ion_diff</c> and <c>ion_stop</c>. Start it with <c>ion mcp</c>
/// (<c>claude mcp add ion -- ion mcp</c>).
/// </summary>
public sealed class McpServer : IDisposable
{
	/// <summary>The protocol versions this server speaks, newest first.</summary>
	public static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

	private readonly TextReader _input;
	private readonly TextWriter _output;
	private readonly string _workingDirectory;
	private LiveGame? _game;
	private RemoteClient? _client;

	/// <summary>Creates a server over <paramref name="input"/> and <paramref name="output"/>.</summary>
	/// <param name="input">Requests, one JSON object per line.</param>
	/// <param name="output">Responses, one JSON object per line.</param>
	/// <param name="workingDirectory">The directory relative paths are resolved against (the current directory when null).</param>
	public McpServer(TextReader input, TextWriter output, string? workingDirectory = null)
	{
		_input = input;
		_output = output;
		_workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
	}

	/// <summary>Serves until the input ends. Returns 0.</summary>
	public int Run()
	{
		while (_input.ReadLine() is { } line)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;
			var response = Handle(line);
			if (response is null) continue;
			_output.WriteLine(response.ToJsonString());
			_output.Flush();
		}

		return 0;
	}

	/// <summary>Handles one message; returns the response, or null for a notification.</summary>
	public JsonObject? Handle(string line)
	{
		JsonObject request;
		try
		{
			request = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Not an object.");
		}
		catch (JsonException ex)
		{
			return Error(null, -32700, $"Parse error: {ex.Message}");
		}

		var id = request["id"]?.DeepClone();
		var method = request["method"]?.GetValue<string>();
		if (method is null) return id is null ? null : Error(id, -32600, "Missing method.");
		if (!request.ContainsKey("id")) return null; // notifications (notifications/initialized, cancelled, ...)

		var parameters = request["params"] as JsonObject ?? [];
		try
		{
			JsonNode result = method switch
			{
				"initialize" => Initialize(parameters),
				"ping" => new JsonObject(),
				"tools/list" => new JsonObject { ["tools"] = Tools() },
				"tools/call" => CallTool(parameters),
				_ => throw new McpException(-32601, $"Method not found: {method}"),
			};
			return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
		}
		catch (McpException ex)
		{
			return Error(id, ex.Code, ex.Message);
		}
	}

	private static JsonObject Initialize(JsonObject parameters)
	{
		var requested = parameters["protocolVersion"]?.GetValue<string>();
		var version = requested is not null && ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0];
		return new JsonObject
		{
			["protocolVersion"] = version,
			["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
			["serverInfo"] = new JsonObject { ["name"] = "ion", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.3.0" },
			["instructions"] = "Drives Ion games. ion_run runs a game headless for N frames (screenshot + summary), or with live=true starts it with the remote protocol and pauses after N frames; then ion_query/ion_get read ECS entities, ion_mutate/ion_spawn/ion_despawn change them, ion_input injects input, ion_step advances frames, ion_screenshot captures the frame, ion_call reaches any remote method (ion_call rpc.discover lists them). ion_stop ends the live game.",
		};
	}

	private static JsonArray Tools()
	{
		var tools = new JsonArray();
		foreach (var tool in ToolDefinitions) tools.Add((JsonNode)tool.DeepClone());
		return tools;
	}

	private JsonObject CallTool(JsonObject parameters)
	{
		var name = parameters["name"]?.GetValue<string>() ?? throw new McpException(-32602, "tools/call needs a tool name.");
		var args = parameters["arguments"] as JsonObject ?? [];
		try
		{
			return name switch
			{
				"ion_run" => IonRun(args),
				"ion_connect" => Connect(args),
				"ion_stop" => Stop(),
				"ion_call" => Text(Remote(Str(args, "method") ?? throw Bad("method is required."), args["params"] as JsonObject)),
				"ion_query" => Text(Remote("world.query", Pick(args, "components", "with", "without", "name", "limit", "world"))),
				"ion_get" => Text(Remote("world.get_components", Pick(args, "entity", "components", "world"))),
				"ion_mutate" => Mutate(args),
				"ion_spawn" => Text(Remote("world.spawn", Pick(args, "components", "name", "world"))),
				"ion_despawn" => Text(Remote("world.despawn", Pick(args, "entity", "world"))),
				"ion_screenshot" => Screenshot(args),
				"ion_input" => Text(Remote("input.send", Pick(args, "events"))),
				"ion_step" => Text(Remote("game.step", Pick(args, "frames"))),
				"ion_pause" => Text(Remote("game.pause", null)),
				"ion_resume" => Text(Remote("game.resume", null)),
				"ion_schedule" => new JsonObject { ["content"] = new JsonArray(TextContent(Remote("schedule.get", null)?["text"]?.GetValue<string>() ?? "")) },
				"ion_metrics" => Text(Remote("metrics.get", null)),
				"ion_logs" => Text(Remote("log.tail", Pick(args, "since", "level", "limit"))),
				"ion_events" => Text(Remote("events.tail", Pick(args, "since", "limit"))),
				"ion_diff" => Diff(args),
				_ => throw new McpException(-32602, $"Unknown tool: {name}"),
			};
		}
		catch (McpException)
		{
			throw;
		}
		catch (RemoteCallException ex)
		{
			return ToolError($"The game answered with error {ex.Code.ToString(CultureInfo.InvariantCulture)}: {ex.Message}");
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TaskCanceledException or FileNotFoundException or ArgumentException or JsonException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
		{
			return ToolError(ex.Message);
		}
	}

	private JsonObject IonRun(JsonObject args)
	{
		var live = Bool(args, "live") ?? false;
		var frames = Int(args, "frames");
		var options = new GameRunOptions
		{
			Project = Path(args, "project") ?? _workingDirectory,
			Headless = Bool(args, "headless") ?? true,
			Render = Bool(args, "render") ?? true,
			Seed = Int(args, "seed"),
			Screenshot = Path(args, "screenshot"),
			Summary = Path(args, "summary"),
			Configuration = Str(args, "configuration") ?? "Debug",
			ExtraArgs = args["args"] is JsonArray extra ? [.. extra.Select(static a => a!.GetValue<string>())] : [],
		};

		if (live)
		{
			StopGame();
			_game = GameRunner.Launch(options with { Remote = true, AllowMutations = Bool(args, "allowMutations") ?? true, PauseAtFrame = frames });
			_client = _game.Client;
			var info = _client.Call("game.info");
			if (frames is not null)
			{
				// Answer once the game has run its frames and paused, so the next tool sees frame N.
				var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
				while (info?["paused"]?.GetValue<bool>() != true && _game.IsRunning && DateTime.UtcNow < deadline)
				{
					Thread.Sleep(50);
					info = _client.Call("game.info");
				}
			}
			return Text(new JsonObject
			{
				["started"] = true,
				["pausedAfterFrames"] = frames,
				["runDirectory"] = _game.RunDirectory,
				["log"] = _game.LogPath,
				["info"] = info,
			});
		}

		var summary = options.Summary ?? System.IO.Path.Combine(GameRunner.DefaultRunDirectory(GameRunner.ResolveProject(options.Project)), "summary.json");
		var output = new StringWriter();
		var exitCode = GameRunner.Run(options with { Frames = frames ?? 600, Summary = summary }, output, output);
		var result = new JsonObject
		{
			["exitCode"] = exitCode,
			["summaryPath"] = System.IO.Path.GetFullPath(summary),
			["summary"] = File.Exists(summary) ? JsonNode.Parse(File.ReadAllText(summary)) : null,
			["outputTail"] = string.Join('\n', output.ToString().Split('\n').TakeLast(30)),
		};
		var response = Text(result);
		if (exitCode != 0) response["isError"] = true;
		return response;
	}

	private JsonObject Connect(JsonObject args)
	{
		var tokenFile = Path(args, "tokenFile");
		if (tokenFile is null)
		{
			var runDirectory = Path(args, "runDirectory") ?? GameRunner.DefaultRunDirectory(GameRunner.ResolveProject(Path(args, "project") ?? _workingDirectory));
			tokenFile = System.IO.Path.Combine(runDirectory, GameRunner.TokenFileName);
		}

		if (!File.Exists(tokenFile)) throw new FileNotFoundException($"No token file at '{tokenFile}'. Start the game with --remote (or ion_run live=true).");
		StopGame();
		_client = RemoteClient.FromTokenFile(tokenFile);
		return Text(new JsonObject { ["connected"] = true, ["url"] = _client.Url.ToString(), ["info"] = _client.Call("game.info") });
	}

	private JsonObject Stop()
	{
		if (_game is null && _client is null) return Text(new JsonObject { ["stopped"] = false, ["reason"] = "No game." });
		int? exitCode = null;
		if (_game is not null)
		{
			exitCode = _game.Stop();
		}
		else
		{
			try
			{
				_client!.Call("game.exit");
			}
			catch (RemoteCallException)
			{
			}
		}

		StopGame();
		return Text(new JsonObject { ["stopped"] = true, ["exitCode"] = exitCode });
	}

	private JsonObject Mutate(JsonObject args)
	{
		if (args["components"] is JsonObject)
		{
			return Text(Remote("world.insert_components", Pick(args, "entity", "components", "world")));
		}

		var parameters = Pick(args, "entity", "component", "path", "world");
		if (!args.ContainsKey("value")) throw Bad("ion_mutate needs 'value' (with 'component' and optional 'path') or a 'components' object.");
		parameters["value"] = args["value"]?.DeepClone();
		return Text(Remote("world.mutate_components", parameters));
	}

	private JsonObject Screenshot(JsonObject args)
	{
		var shot = Remote("screenshot", null) ?? throw new InvalidOperationException("No screenshot.");
		var data = shot["data"]!.GetValue<string>();
		var meta = new JsonObject { ["width"] = shot["width"]?.DeepClone(), ["height"] = shot["height"]?.DeepClone(), ["frame"] = shot["frame"]?.DeepClone() };
		if (Path(args, "path") is { } path)
		{
			var full = System.IO.Path.GetFullPath(path);
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
			File.WriteAllBytes(full, Convert.FromBase64String(data));
			meta["path"] = full;
		}

		var content = new JsonArray(TextContent(meta.ToJsonString(Indented)));
		if (Bool(args, "includeImage") ?? true) content.Add((JsonNode)new JsonObject { ["type"] = "image", ["data"] = data, ["mimeType"] = "image/png" });
		return new JsonObject { ["content"] = content };
	}

	private JsonObject Diff(JsonObject args)
	{
		var actual = Path(args, "actual") ?? throw Bad("actual is required.");
		var expected = Path(args, "expected") ?? throw Bad("expected is required.");
		var tolerance = Int(args, "tolerance") ?? 2;
		var maxRatio = args["maxMismatchRatio"]?.GetValue<double>() ?? 0;
		var diffPath = Path(args, "diff") ?? System.IO.Path.ChangeExtension(actual, ".diff.png");
		var result = ImageDiff.Compare(actual, expected, tolerance, diffPath);
		var response = Text(new JsonObject
		{
			["match"] = result.Matches(maxRatio),
			["sameSize"] = result.SameSize,
			["width"] = result.Width,
			["height"] = result.Height,
			["mismatchedPixels"] = result.MismatchedPixels,
			["mismatchRatio"] = Math.Round(result.MismatchRatio, 6),
			["maxChannelDifference"] = result.MaxChannelDifference,
			["diff"] = result.DiffPath,
		});
		return response;
	}

	private JsonNode? Remote(string method, JsonObject? parameters)
	{
		var client = _client ?? throw new InvalidOperationException("No game is connected: call ion_run with live=true, or ion_connect.");
		return client.Call(method, parameters);
	}

	private void StopGame()
	{
		if (_game is not null)
		{
			_game.Dispose();
			_game = null;
			_client = null;
		}

		_client?.Dispose();
		_client = null;
	}

	private string? Path(JsonObject args, string name) =>
		Str(args, name) is { } value ? System.IO.Path.GetFullPath(value, _workingDirectory) : null;

	private static string? Str(JsonObject args, string name) => args[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

	private static int? Int(JsonObject args, string name) => args[name] is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : null;

	private static bool? Bool(JsonObject args, string name) => args[name] is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

	private static JsonObject Pick(JsonObject args, params string[] names)
	{
		var result = new JsonObject();
		foreach (var name in names)
		{
			if (args[name] is { } value) result[name] = value.DeepClone();
		}

		return result;
	}

	private static JsonObject Text(JsonNode? value) => new() { ["content"] = new JsonArray(TextContent(value?.ToJsonString(Indented) ?? "null")) };

	private static JsonObject TextContent(string text) => new() { ["type"] = "text", ["text"] = text };

	private static JsonObject ToolError(string message) => new() { ["content"] = new JsonArray(TextContent(message)), ["isError"] = true };

	private static McpException Bad(string message) => new(-32602, message);

	private static JsonObject Error(JsonNode? id, int code, string message) => new()
	{
		["jsonrpc"] = "2.0",
		["id"] = id,
		["error"] = new JsonObject { ["code"] = code, ["message"] = message },
	};

	/// <inheritdoc/>
	public void Dispose() => StopGame();

	private sealed class McpException(int code, string message) : Exception(message)
	{
		public int Code { get; } = code;
	}

	private static JsonObject Schema(params (string Name, string Type, string Description)[] properties)
	{
		var props = new JsonObject();
		var required = new JsonArray();
		foreach (var (rawName, type, description) in properties)
		{
			var optional = rawName.EndsWith('?');
			var name = optional ? rawName[..^1] : rawName;
			var schema = new JsonObject { ["description"] = description };
			if (type.Length > 0)
			{
				if (type.Contains('|', StringComparison.Ordinal)) schema["type"] = new JsonArray([.. type.Split('|').Select(static t => (JsonNode)t)]);
				else schema["type"] = type;
			}

			if (type == "array") schema["items"] = new JsonObject();
			props[name] = schema;
			if (!optional) required.Add((JsonNode)name);
		}

		var result = new JsonObject { ["type"] = "object", ["properties"] = props };
		if (required.Count > 0) result["required"] = required;
		return result;
	}

	private static JsonObject Tool(string name, string description, JsonObject schema) => new() { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };

	private const string EntityDescription = "The entity: its id (number from ion_query) or its EntityName (string).";

	/// <summary>The tools, with their input schemas.</summary>
	internal static readonly JsonObject[] ToolDefinitions =
	[
		Tool("ion_run", "Runs an Ion game. By default headless for 'frames' frames (600) with a deterministic clock, then returns the run summary (frame stats, counters, warnings, errors, exception, schedule) and writes 'screenshot' (PNG of the last frame) if given. With live=true, starts it with the remote protocol (mutations allowed unless allowMutations=false), pauses after 'frames' frames if given, and keeps it running for the other tools.",
			Schema(("project?", "string", "The game project (.csproj or its directory). Default: the current directory."),
				("frames?", "integer", "Frames to run (default 600), or with live=true the frame to pause after."),
				("seed?", "integer", "The random seed (Ion:Seed)."),
				("headless?", "boolean", "Headless backends (default true)."),
				("render?", "boolean", "Render headless into an offscreen target so screenshots work (default true)."),
				("screenshot?", "string", "Write the last frame to this PNG."),
				("summary?", "string", "Write the summary JSON here (default .ion/run/summary.json)."),
				("live?", "boolean", "Keep the game running with the remote protocol (default false)."),
				("allowMutations?", "boolean", "With live=true: grant the mutate scope (default true)."),
				("configuration?", "string", "Build configuration (default Debug)."),
				("args?", "array", "Extra game arguments, for example [\"--Ion:Window:Width=640\"]."))),
		Tool("ion_connect", "Connects to a game already running with --remote, through its token file.",
			Schema(("tokenFile?", "string", "The token file (default <project dir>/.ion/run/remote.json)."), ("runDirectory?", "string", "The run directory holding remote.json."), ("project?", "string", "The game project, to find its run directory."))),
		Tool("ion_stop", "Stops the live game (game.exit, then kill after a timeout).", Schema()),
		Tool("ion_call", "Calls any remote protocol method of the connected game (rpc.discover lists them with their parameters).",
			Schema(("method", "string", "The method, for example rpc.discover, game.info, world.query, resources.get."), ("params?", "object", "The method's parameters."))),
		Tool("ion_query", "Finds ECS entities by components and name and returns their components as JSON.",
			Schema(("components?", "array", "Components to return (default all remote-visible ones)."), ("with?", "array", "Only entities with all of these."), ("without?", "array", "Only entities with none of these."), ("name?", "string", "EntityName, exact or prefix ending in '*'."), ("limit?", "integer", "At most this many (default 1000)."), ("world?", "integer", "World index (world.list)."))),
		Tool("ion_get", "Reads the components of one entity.",
			Schema(("entity", "integer|string", EntityDescription), ("components?", "array", "Components to read (default all)."), ("world?", "integer", "World index."))),
		Tool("ion_mutate", "Changes an entity: sets one component field by path (component, path, value; path like 'Position.0'; empty path replaces the component), or inserts/replaces whole components (components object).",
			Schema(("entity", "integer|string", EntityDescription), ("component?", "string", "The component name."), ("path?", "string", "The field path inside the component's JSON."), ("value?", "", "The new value."), ("components?", "object", "Component name to JSON value, to insert or replace."), ("world?", "integer", "World index."))),
		Tool("ion_spawn", "Creates an entity with components and an optional name.",
			Schema(("components?", "object", "Component name to JSON value."), ("name?", "string", "An EntityName."), ("world?", "integer", "World index."))),
		Tool("ion_despawn", "Destroys an entity.", Schema(("entity", "integer|string", EntityDescription), ("world?", "integer", "World index."))),
		Tool("ion_screenshot", "Captures the last rendered frame of the live game (needs headless rendering or a window); returns the image and saves it to 'path' if given.",
			Schema(("path?", "string", "Save the PNG here."), ("includeImage?", "boolean", "Return the image content (default true)."))),
		Tool("ion_input", "Injects input into the live game, applied at the start of the next frame: keys, pointer, wheel, text, gamepad (see input.send in rpc.discover).",
			Schema(("events", "array", "For example [{\"type\":\"key\",\"key\":\"Space\",\"action\":\"tap\"}, {\"type\":\"pointer\",\"x\":10,\"y\":20,\"action\":\"click\"}, {\"type\":\"text\",\"text\":\"hi\"}]."))),
		Tool("ion_step", "Runs frames of the paused live game and pauses again; answers when they have run.", Schema(("frames?", "integer", "Frames to run (default 1)."))),
		Tool("ion_pause", "Pauses the live game at the end of the current frame (it keeps answering).", Schema()),
		Tool("ion_resume", "Resumes the live game.", Schema()),
		Tool("ion_schedule", "The live game's schedule: every stage's steps in run order with orders and scopes.", Schema()),
		Tool("ion_metrics", "The live game's last frame stats and game counters.", Schema()),
		Tool("ion_logs", "The live game's recent log entries.", Schema(("since?", "integer", "Sequence number from the previous call's 'next'."), ("level?", "string", "Minimum level (default Information)."), ("limit?", "integer", "At most this many."))),
		Tool("ion_events", "Event counts per type and recent payloads of remote-registered events.", Schema(("since?", "integer", "Sequence number from the previous call's 'next'."), ("limit?", "integer", "At most this many payloads."))),
		Tool("ion_diff", "Compares a PNG with a golden PNG: a pixel mismatches when a channel differs by more than 'tolerance'; writes a diff image (mismatches in red).",
			Schema(("actual", "string", "The PNG to check."), ("expected", "string", "The golden PNG."), ("tolerance?", "integer", "Per-channel tolerance (default 2)."), ("maxMismatchRatio?", "number", "Fraction of pixels allowed to mismatch (default 0)."), ("diff?", "string", "Where to write the diff image (default <actual>.diff.png)."))),
	];
}
