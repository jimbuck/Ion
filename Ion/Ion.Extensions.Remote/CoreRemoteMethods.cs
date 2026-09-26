using System.Buffers;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;
using Ion.Extensions.Scenes;

using static Ion.Extensions.Remote.RemoteSchema;

namespace Ion.Extensions.Remote;

/// <summary>
/// The methods every game has: discovery, the game loop (info, pause, resume, step, exit), schedule, metrics, screenshot,
/// input, events, logs and resources. The ECS module adds <c>world.*</c> and <c>registry.schema</c>.
/// </summary>
internal sealed class CoreRemoteMethods(RemoteServer server) : IRemoteMethodProvider
{
	public void Register(RemoteMethodRegistry methods)
	{
		methods
			.Read("rpc.discover", "Lists every method with its access (read or mutate), description and parameter schema.", Discover)
			.Read("game.info", "The game's title, frame, stage, pause state, active scene, backend and remote settings.", Info, watchable: true)
			.Mutate("game.pause", "Pauses the game at the end of the current frame; the game keeps serving requests while paused.", Pause)
			.Mutate("game.resume", "Resumes a paused game.", Resume)
			.Mutate("game.step", "Runs the given number of frames and pauses again; answers once they have run.", static _ => throw new InvalidOperationException("Handled by the server."),
				Object(("frames?", Integer("How many frames to run (default 1)."))))
			.Mutate("game.exit", "Asks the game loop to exit after the current frame.", Exit)
			.Read("schedule.get", "The schedule: every stage's steps in run order with their orders, scopes and systems, as text and as data.", Schedule)
			.Read("metrics.get", "The last frame's stats (frame time, draw calls, sprites, entities, events, allocations) and every game counter, gauge and histogram, in the frame log's shape.", Metrics, watchable: true)
			.Read("screenshot", "Captures the last rendered frame as a base64 PNG (needs headless rendering, --Ion:Headless:Render=true, or a window).", Screenshot)
			.Mutate("input.send", "Injects input through Input v2's scripted path: keys, pointer, wheel, text and gamepads, applied at the start of the next frame (or after 'delay' frames).", SendInput,
				Object(("events", new JsonObject
				{
					["type"] = "array",
					["description"] = "Input events. {type:'key', key:'Space', action:'tap'|'press'|'release'|'hold', frames?, modifiers?:['Control']}; {type:'pointer', x, y, button?:'Left', action:'move'|'click'|'press'|'release'}; {type:'wheel', delta}; {type:'text', text}; {type:'gamepad', index?, button:'A', action:'tap'|'press'|'release'} or {type:'gamepad', index?, axis:'LeftX', value} or {type:'gamepad', index?, action:'connect'|'disconnect'}. Every event takes an optional 'delay' in frames.",
					["items"] = new JsonObject { ["type"] = "object" },
				})))
			.Read("events.tail", "Event counts per type (this frame, last frame, total) and the payloads of the event types registered for remote (AddRemoteEvent) since a sequence number.", EventsTail,
				Object(("since?", Integer("Return payloads with a sequence number of at least this (the 'next' of the previous call).")), ("limit?", Integer("At most this many payloads (default 256)."))), watchable: true)
			.Read("log.tail", "The application's recent log entries since a sequence number.", LogTail,
				Object(("since?", Integer("Return entries with a sequence number of at least this (the 'next' of the previous call).")), ("level?", String("Minimum level: Trace, Debug, Information, Warning, Error, Critical (default Information).")), ("limit?", Integer("At most this many entries, the newest (default 200)."))), watchable: true)
			.Read("resources.list", "Lists the resources (named game state) with their description and whether they can be written.", ListResources)
			.Read("resources.get", "Reads a resource as JSON.", GetResource, Object(("name", String("The resource name (see resources.list)."))), watchable: true)
			.Mutate("resources.set", "Writes a writable resource from JSON.", SetResource, Object(("name", String("The resource name.")), ("value", Any("The new value."))));
	}

	private JsonNode Discover(RemoteRequest request)
	{
		var list = new JsonArray();
		foreach (var method in server.Methods.Methods)
		{
			var entry = new JsonObject
			{
				["name"] = method.Name,
				["access"] = method.Access == RemoteAccess.Mutate ? "mutate" : "read",
				["description"] = method.Description,
				["watchable"] = method.Watchable,
			};
			if (method.Params is not null) entry["params"] = method.Params.DeepClone();
			list.Add((JsonNode)entry);
		}

		list.Add((JsonNode)new JsonObject
		{
			["name"] = RemoteServer.UnwatchMethod,
			["access"] = "read",
			["description"] = "Stops a watch of this session (the id of the '+watch' request), or all of them without an id.",
			["watchable"] = false,
			["params"] = Object(("id?", Any("The watch request's id."))),
		});

		return new JsonObject
		{
			["protocol"] = "ion-remote",
			["version"] = 1,
			["jsonrpc"] = "2.0",
			["access"] = request.Access == RemoteAccess.Mutate ? "mutate" : "read",
			["watchSuffix"] = RemoteServer.WatchSuffix,
			["methods"] = list,
		};
	}

	private JsonNode Info(RemoteRequest request)
	{
		var services = request.Services;
		var context = services.GetService<GameLoopContext>();
		var config = services.GetService<IOptions<GameConfig>>()?.Value;
		var scenes = services.GetService<SceneSystem>();
		var metrics = services.GetService<IMetrics>();
		var configuration = services.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
		return new JsonObject
		{
			["title"] = config?.Title,
			["pid"] = Environment.ProcessId,
			["frame"] = context?.Frame ?? 0,
			["stage"] = (context?.Stage ?? GameLoopStage.None).ToString(),
			["fixedSteps"] = context?.FixedStepCount ?? 0,
			["paused"] = server.IsPaused,
			["headless"] = configuration is not null && bool.TryParse(configuration["Ion:Headless"], out var headless) && headless,
			["headlessRender"] = configuration is not null && bool.TryParse(configuration["Ion:Headless:Render"], out var render) && render,
			["scene"] = scenes?.CurrentSceneId,
			["sceneLoading"] = scenes?.IsLoading ?? false,
			["fps"] = metrics is null ? null : Math.Round(metrics.LastFrame.Fps, 2),
			["allowMutations"] = server.Options.AllowMutations,
			["access"] = request.Access == RemoteAccess.Mutate ? "mutate" : "read",
		};
	}

	private JsonNode Pause(RemoteRequest request)
	{
		server.Pause();
		return new JsonObject { ["paused"] = true, ["frame"] = request.Services.GetService<GameLoopContext>()?.Frame ?? 0 };
	}

	private JsonNode Resume(RemoteRequest request)
	{
		server.Resume();
		return new JsonObject { ["paused"] = false };
	}

	private JsonNode Exit(RemoteRequest request)
	{
		var loop = request.Services.GetService<GameLoopContext>()?.Loop ?? throw new RemoteException(RemoteErrorCodes.Unsupported, "No game loop is running.");
		loop.Stop();
		server.Resume();
		return new JsonObject { ["exiting"] = true };
	}

	private static JsonNode Schedule(RemoteRequest request)
	{
		var schedule = request.Services.GetService<GameLoopContext>()?.Schedule ?? throw new RemoteException(RemoteErrorCodes.Unsupported, "The game loop has not been built yet.");
		return new JsonObject
		{
			["text"] = schedule.Print(),
			["stages"] = Stages(schedule.Plan),
			["scenes"] = new JsonArray([.. schedule.Plan.Nested.Select(static n => (JsonNode)new JsonObject { ["name"] = n.Name, ["stages"] = Stages(n.Plan) })]),
		};

		static JsonArray Stages(SchedulePlan plan)
		{
			var stages = new JsonArray();
			foreach (var stage in plan.Stages)
			{
				var steps = new JsonArray();
				foreach (var step in stage.Steps)
				{
					steps.Add((JsonNode)new JsonObject
					{
						["order"] = step.Order,
						["kind"] = step.Kind.ToString(),
						["name"] = step.Name,
						["end"] = step.EndName,
						["depth"] = step.Depth,
					});
				}

				stages.Add((JsonNode)new JsonObject { ["stage"] = stage.Stage.ToString(), ["steps"] = steps });
			}

			return stages;
		}
	}

	private static JsonNode Metrics(RemoteRequest request)
	{
		var metrics = request.GetService<IMetrics>("The game has no metrics module (AddMetrics, included in AddIon).");
		var buffer = new ArrayBufferWriter<byte>(1024);
		using (var json = new Utf8JsonWriter(buffer))
		{
			var stats = metrics.LastFrame;
			json.WriteStartObject();
			FrameLogWriter.WriteStats(json, in stats);
			WriteInstruments(json, metrics.Instruments);
			json.WriteBoolean("profiling", metrics.IsProfiling);
			json.WriteEndObject();
		}

		return JsonNode.Parse(buffer.WrittenSpan)!;

		static void WriteInstruments(Utf8JsonWriter json, IReadOnlyList<MetricsInstrument> instruments)
		{
			json.WriteStartObject("counters");
			foreach (var i in instruments) if (i is MetricsCounter c) json.WriteNumber(c.Name, c.Value);
			json.WriteEndObject();
			json.WriteStartObject("gauges");
			foreach (var i in instruments) if (i is MetricsGauge g) json.WriteNumber(g.Name, g.Value);
			json.WriteEndObject();
			json.WriteStartObject("histograms");
			foreach (var i in instruments)
			{
				if (i is not MetricsHistogram h) continue;
				json.WriteStartObject(h.Name);
				json.WriteNumber("count", h.Count);
				json.WriteNumber("sum", h.Sum);
				json.WriteNumber("min", h.Min);
				json.WriteNumber("max", h.Max);
				json.WriteNumber("total_count", h.TotalCount);
				json.WriteEndObject();
			}

			json.WriteEndObject();
		}
	}

	private static JsonNode Screenshot(RemoteRequest request)
	{
		var source = request.GetService<IScreenshotSource>("Screenshots need a rendering backend: run headless with --Ion:Headless:Render=true, or windowed.");
		Screenshot shot;
		try
		{
			shot = source.Capture();
		}
		catch (InvalidOperationException ex)
		{
			throw new RemoteException(RemoteErrorCodes.Unsupported, ex.Message);
		}

		var png = PngWriter.Encode(shot);
		return new JsonObject
		{
			["format"] = "png",
			["width"] = shot.Width,
			["height"] = shot.Height,
			["frame"] = request.Services.GetService<GameLoopContext>()?.Frame ?? 0,
			["data"] = Convert.ToBase64String(png),
		};
	}

	private static JsonNode SendInput(RemoteRequest request)
	{
		var input = request.GetService<ScriptedInput>("The game has no scripted input (AddRemote registers it; is the input tracker in use?).");
		var events = request.GetArray("events") ?? throw RemoteException.InvalidParams("Missing required array parameter 'events'.");
		var queued = 0;
		for (var i = 0; i < events.Count; i++)
		{
			if (events[i] is not JsonObject e) throw RemoteException.InvalidParams($"events[{i}] must be an object.");
			var item = new RemoteRequest(request.Method, e, request.Services, request.Access);
			var delay = item.GetInt32("delay", 0, 0, 100_000);
			var type = item.GetString("type");
			switch (type)
			{
				case "key":
				{
					var key = ParseEnum<Key>(item.GetString("key"), $"events[{i}].key");
					var modifiers = ModifierKeys.None;
					foreach (var m in item.GetStrings("modifiers")) modifiers |= ParseEnum<ModifierKeys>(m, $"events[{i}].modifiers");
					var action = item.GetOptionalString("action") ?? "tap";
					switch (action)
					{
						case "tap":
							input.Enqueue(InputEvent.ForKey(key, true, modifiers: modifiers), delay);
							input.Enqueue(InputEvent.ForKey(key, false, modifiers: modifiers), delay);
							queued += 2;
							break;
						case "press":
							input.Enqueue(InputEvent.ForKey(key, true, modifiers: modifiers), delay);
							queued++;
							break;
						case "release":
							input.Enqueue(InputEvent.ForKey(key, false, modifiers: modifiers), delay);
							queued++;
							break;
						case "hold":
							var frames = item.GetInt32("frames", 1, 1, 100_000);
							input.Enqueue(InputEvent.ForKey(key, true, modifiers: modifiers), delay);
							input.Enqueue(InputEvent.ForKey(key, false, modifiers: modifiers), delay + frames);
							queued += 2;
							break;
						default:
							throw RemoteException.InvalidParams($"events[{i}].action must be tap, press, release or hold.");
					}

					break;
				}
				case "pointer":
				{
					var action = item.GetOptionalString("action") ?? "click";
					var button = ParseEnum<MouseButton>(item.GetOptionalString("button") ?? "Left", $"events[{i}].button");
					if (item.GetOptionalDouble("x") is { } x && item.GetOptionalDouble("y") is { } y)
					{
						input.Enqueue(InputEvent.ForMouseMove(new Vector2((float)x, (float)y)), delay);
						queued++;
					}
					else if (action == "move")
					{
						throw RemoteException.InvalidParams($"events[{i}] (pointer move) needs x and y.");
					}

					if (action is "click" or "press") { input.Enqueue(InputEvent.ForMouseButton(button, true), delay); queued++; }
					if (action is "click" or "release") { input.Enqueue(InputEvent.ForMouseButton(button, false), delay); queued++; }
					if (action is not ("click" or "press" or "release" or "move")) throw RemoteException.InvalidParams($"events[{i}].action must be move, click, press or release.");
					break;
				}
				case "wheel":
					input.Enqueue(InputEvent.ForWheel((float)(item.GetOptionalDouble("delta") ?? throw RemoteException.InvalidParams($"events[{i}] (wheel) needs delta."))), delay);
					queued++;
					break;
				case "text":
					foreach (var c in item.GetString("text")) { input.Enqueue(InputEvent.ForText(c), delay); queued++; }
					break;
				case "gamepad":
				{
					var index = item.GetInt32("index", 0, 0, InputTracker.MaxGamepads - 1);
					var action = item.GetOptionalString("action");
					if (item.GetOptionalString("axis") is { } axisName)
					{
						var axis = ParseEnum<GamepadAxis>(axisName, $"events[{i}].axis");
						input.Enqueue(InputEvent.ForGamepadAxis(index, axis, (float)(item.GetOptionalDouble("value") ?? 0)), delay);
						queued++;
					}
					else if (item.GetOptionalString("button") is { } buttonName)
					{
						var button = ParseEnum<GamepadButton>(buttonName, $"events[{i}].button");
						if (action is null or "tap" or "press") { input.Enqueue(InputEvent.ForGamepadButton(index, button, true), delay); queued++; }
						if (action is null or "tap" or "release") { input.Enqueue(InputEvent.ForGamepadButton(index, button, false), delay); queued++; }
						if (action is not (null or "tap" or "press" or "release")) throw RemoteException.InvalidParams($"events[{i}].action must be tap, press or release.");
					}
					else if (action is "connect" or "disconnect")
					{
						input.Enqueue(InputEvent.ForGamepadConnection(index, action == "connect"), delay);
						queued++;
					}
					else
					{
						throw RemoteException.InvalidParams($"events[{i}] (gamepad) needs a button, an axis, or action connect/disconnect.");
					}

					break;
				}
				default:
					throw RemoteException.InvalidParams($"events[{i}].type must be key, pointer, wheel, text or gamepad.");
			}
		}

		return new JsonObject { ["queued"] = queued, ["frame"] = request.Services.GetService<GameLoopContext>()?.Frame ?? 0 };
	}

	private JsonNode EventsTail(RemoteRequest request)
	{
		var since = request.GetOptionalInt64("since") ?? 0;
		var limit = request.GetInt32("limit", 256, 1, RemoteEventLog.Capacity);
		var counts = new JsonArray();
		if (request.Services.GetService<EventBus>() is { } bus)
		{
			foreach (var channel in bus.Channels)
			{
				counts.Add((JsonNode)new JsonObject
				{
					["type"] = channel.EventType.Name,
					["id"] = channel.Id,
					["thisFrame"] = channel.CurrentFrameCount,
					["lastFrame"] = channel.PreviousFrameCount,
					["total"] = channel.EmittedCount,
				});
			}
		}

		var events = new JsonArray();
		var log = server.EventLog;
		var items = log.Since(since).ToList();
		if (items.Count > limit) items.RemoveRange(0, items.Count - limit);
		foreach (var (seq, frame, name, payload) in items)
		{
			events.Add((JsonNode)new JsonObject { ["seq"] = seq, ["frame"] = frame, ["type"] = name, ["payload"] = payload?.DeepClone() });
		}

		return new JsonObject
		{
			["next"] = request.IsWatch ? null : log.Next,
			["counts"] = counts,
			["events"] = events,
		};
	}

	private static JsonNode LogTail(RemoteRequest request)
	{
		var buffer = request.GetService<RemoteLogBuffer>();
		var since = request.GetOptionalInt64("since") ?? 0;
		var limit = request.GetInt32("limit", 200, 1, RemoteLogBuffer.Capacity);
		var level = ParseEnum<LogLevel>(request.GetOptionalString("level") ?? "Information", "level");
		var entries = new JsonArray();
		foreach (var e in buffer.Tail(since, level, limit))
		{
			entries.Add((JsonNode)new JsonObject
			{
				["seq"] = e.Seq,
				["time"] = e.Time.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
				["level"] = e.Level.ToString(),
				["category"] = e.Category,
				["message"] = e.Message,
				["exception"] = e.Exception,
			});
		}

		return new JsonObject { ["next"] = request.IsWatch ? null : buffer.Next, ["entries"] = entries };
	}

	private static JsonNode ListResources(RemoteRequest request)
	{
		var list = new JsonArray();
		foreach (var resource in request.Services.GetServices<RemoteResource>())
		{
			list.Add((JsonNode)new JsonObject
			{
				["name"] = resource.Name,
				["description"] = resource.Description,
				["writable"] = resource.CanWrite,
				["schema"] = resource.Schema?.DeepClone(),
			});
		}

		return list;
	}

	private static JsonNode? GetResource(RemoteRequest request) => FindResource(request).Get(request.Services);

	private static JsonNode? SetResource(RemoteRequest request)
	{
		var resource = FindResource(request);
		if (!resource.CanWrite) throw new RemoteException(RemoteErrorCodes.Unsupported, $"The resource '{resource.Name}' is read-only.");
		resource.Set(request.Services, request.Get("value")?.DeepClone());
		return resource.Get(request.Services);
	}

	private static RemoteResource FindResource(RemoteRequest request)
	{
		var name = request.GetString("name");
		return request.Services.GetServices<RemoteResource>().FirstOrDefault(r => r.Name == name)
			?? throw RemoteException.NotFound($"No resource named '{name}'. Call resources.list.");
	}

	internal static T ParseEnum<T>(string value, string parameter) where T : struct, Enum =>
		Enum.TryParse<T>(value, ignoreCase: true, out var result) && !int.TryParse(value, out _)
			? result
			: throw RemoteException.InvalidParams($"'{value}' is not a valid {typeof(T).Name} for {parameter}.");
}
