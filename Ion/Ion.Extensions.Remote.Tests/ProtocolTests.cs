using Ion.Extensions.Ecs;
using Ion.Extensions.Metrics;
using Ion.Testing;
using Ion.Tests;

namespace Ion.Extensions.Remote.Tests;

[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class ProtocolTests
{
	[Fact]
	public void DiscoverListsEveryMethodWithItsAccess()
	{
		using var game = new RemoteGame();
		var result = game.Result("rpc.discover")!;
		Assert.Equal("ion-remote", result["protocol"]!.GetValue<string>());
		var methods = result["methods"]!.AsArray().ToDictionary(m => m!["name"]!.GetValue<string>(), m => m!["access"]!.GetValue<string>());

		string[] read = ["rpc.discover", "game.info", "schedule.get", "metrics.get", "screenshot", "events.tail", "log.tail", "resources.list", "resources.get", "registry.schema", "world.list", "world.query", "world.get_components", "world.list_components", "rpc.unwatch"];
		string[] mutate = ["game.pause", "game.resume", "game.step", "game.exit", "input.send", "resources.set", "world.insert_components", "world.mutate_components", "world.remove_components", "world.spawn", "world.despawn"];
		foreach (var name in read) Assert.Equal("read", methods[name]);
		foreach (var name in mutate) Assert.Equal("mutate", methods[name]);
	}

	[Fact]
	public void UnknownMethodIsMethodNotFound()
	{
		using var game = new RemoteGame();
		Assert.Equal(RemoteErrorCodes.MethodNotFound, RemoteGame.ErrorCode(game.Call("world.nope")));
	}

	[Fact]
	public void MalformedRequestsAreRejected()
	{
		using var game = new RemoteGame();
		Assert.Equal(RemoteErrorCodes.ParseError, RemoteGame.ErrorCode(game.Post("{not json", game.Server.ReadToken).Body!));
		Assert.Equal(RemoteErrorCodes.InvalidRequest, RemoteGame.ErrorCode(game.Post("{\"id\":1,\"method\":\"game.info\"}", game.Server.ReadToken).Body!));
		Assert.Equal(RemoteErrorCodes.InvalidRequest, RemoteGame.ErrorCode(game.Post("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"game.info\"}]", game.Server.ReadToken).Body!));
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(game.Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"game.info\",\"params\":[1]}", game.Server.ReadToken).Body!));
	}

	[Fact]
	public void NotificationGetsNoContent()
	{
		using var game = new RemoteGame();
		var (status, body) = game.Post("{\"jsonrpc\":\"2.0\",\"method\":\"game.info\"}", game.Server.ReadToken);
		Assert.Equal(System.Net.HttpStatusCode.NoContent, status);
		Assert.Null(body);
	}

	[Fact]
	public void GameInfoDescribesTheGame()
	{
		using var game = new RemoteGame();
		var info = game.Result("game.info")!;
		Assert.Equal("RemoteTestGame", info["title"]!.GetValue<string>());
		Assert.Equal(Environment.ProcessId, info["pid"]!.GetValue<int>());
		Assert.True(info["headless"]!.GetValue<bool>());
		Assert.False(info["paused"]!.GetValue<bool>());
		Assert.Equal("Last", info["stage"]!.GetValue<string>());
		Assert.Equal("mutate", info["access"]!.GetValue<string>());
	}

	[Fact]
	public void ScheduleGetReturnsTextAndStages()
	{
		using var game = new RemoteGame();
		var schedule = game.Result("schedule.get")!;
		Assert.Contains("RemoteSystem.Process", schedule["text"]!.GetValue<string>(), StringComparison.Ordinal);
		var last = schedule["stages"]!.AsArray().Single(s => s!["stage"]!.GetValue<string>() == "Last")!;
		var remote = last["steps"]!.AsArray().Single(s => s!["name"]!.GetValue<string>().Contains("RemoteSystem.Process", StringComparison.Ordinal))!;
		Assert.Equal(StageOrder.Remote, remote["order"]!.GetValue<int>());
	}

	[Fact]
	public void MetricsGetHasFrameStatsAndCounters()
	{
		using var game = new RemoteGame();
		game.Host.Metrics.Counter("balls").Add(3);
		game.Host.Step(2);
		var metrics = game.Result("metrics.get")!;
		Assert.True(metrics["frame"]!.GetValue<long>() >= 1);
		Assert.True(metrics.AsObject().ContainsKey("frame_ms"));
		Assert.True(metrics.AsObject().ContainsKey("entities"));
		Assert.Equal(3, metrics["counters"]!["balls"]!.GetValue<long>());
	}

	[Fact]
	public void ScreenshotWithoutRenderingIsUnsupported()
	{
		using var game = new RemoteGame();
		var response = game.Call("screenshot");
		Assert.Equal(RemoteErrorCodes.Unsupported, RemoteGame.ErrorCode(response));
		Assert.Contains("Headless:Render", response["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
	}

	[RenderingFact]
	public void ScreenshotReturnsThePng()
	{
		using var game = new RemoteGame(configure: host => host.WithRendering(64, 48));
		game.Host.Step(2);
		var shot = game.Result("screenshot")!;
		Assert.Equal("png", shot["format"]!.GetValue<string>());
		Assert.Equal(64, shot["width"]!.GetValue<int>());
		Assert.Equal(48, shot["height"]!.GetValue<int>());
		var png = Convert.FromBase64String(shot["data"]!.GetValue<string>());
		Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
	}

	[Fact]
	public void InputSendInjectsKeysPointerTextAndGamepad()
	{
		using var game = new RemoteGame();
		var queued = game.Result("input.send", new JsonObject
		{
			["events"] = new JsonArray(
				new JsonObject { ["type"] = "key", ["key"] = "Space", ["action"] = "tap" },
				new JsonObject { ["type"] = "pointer", ["x"] = 12.5, ["y"] = 40, ["action"] = "click" },
				new JsonObject { ["type"] = "text", ["text"] = "hi" },
				new JsonObject { ["type"] = "gamepad", ["action"] = "connect" },
				new JsonObject { ["type"] = "gamepad", ["button"] = "A", ["action"] = "tap", ["delay"] = 1 }),
		})!;
		Assert.Equal(10, queued["queued"]!.GetValue<int>());

		game.Host.Step(3);
		Assert.Contains("space", game.Probe.Seen);
		Assert.Contains("click 12.5,40", game.Probe.Seen);
		Assert.Contains("text hi", game.Probe.Seen);
		Assert.Contains("pad A", game.Probe.Seen);
	}

	[Fact]
	public void InputSendRejectsUnknownKeys()
	{
		using var game = new RemoteGame();
		var response = game.Call("input.send", new JsonObject { ["events"] = new JsonArray(new JsonObject { ["type"] = "key", ["key"] = "NoSuchKey" }) });
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(response));
	}

	[Fact]
	public void EventsTailHasCountsAndRegisteredPayloads()
	{
		using var game = new RemoteGame();
		game.Host.Step(4);
		var tail = game.Result("events.tail", new JsonObject { ["since"] = 0 })!;
		var ping = tail["events"]!.AsArray().Single(e => e!["type"]!.GetValue<string>() == nameof(PingEvent))!;
		Assert.Equal(42, ping["payload"]!["Value"]!.GetValue<int>());
		Assert.Equal(2u, ping["frame"]!.GetValue<uint>());
		Assert.Contains(tail["counts"]!.AsArray(), c => c!["type"]!.GetValue<string>() == nameof(PingEvent) && c["total"]!.GetValue<long>() == 1);

		var next = tail["next"]!.GetValue<long>();
		var again = game.Result("events.tail", new JsonObject { ["since"] = next })!;
		Assert.Empty(again["events"]!.AsArray());
	}

	[Fact]
	public void LogTailReturnsRecentEntries()
	{
		using var game = new RemoteGame();
		game.Host.Step(4);
		var tail = game.Result("log.tail", new JsonObject { ["level"] = "Warning" })!;
		Assert.Contains(tail["entries"]!.AsArray(), e => e!["message"]!.GetValue<string>() == "Probe warning at frame 2" && e["level"]!.GetValue<string>() == "Warning");
	}

	[Fact]
	public void ResourcesCanBeListedReadAndWritten()
	{
		using var game = new RemoteGame();
		var list = game.Result("resources.list")!.AsArray();
		Assert.Contains(list, r => r!["name"]!.GetValue<string>() == "Ion.GameTime" && !r["writable"]!.GetValue<bool>());
		Assert.Contains(list, r => r!["name"]!.GetValue<string>() == "Test.Score" && r["writable"]!.GetValue<bool>());

		var time = game.Result("resources.get", new JsonObject { ["name"] = "Ion.GameTime" })!;
		Assert.Equal("Last", time["stage"]!.GetValue<string>());

		Assert.Equal(7, game.Result("resources.set", new JsonObject { ["name"] = "Test.Score", ["value"] = 7 })!.GetValue<int>());
		Assert.Equal(7, game.Probe.Score);

		Assert.True(game.Result("resources.set", new JsonObject { ["name"] = "Ion.Metrics.Profiling", ["value"] = true })!.GetValue<bool>());
		Assert.True(game.Host.Metrics.IsProfiling);

		Assert.Equal(RemoteErrorCodes.Unsupported, RemoteGame.ErrorCode(game.Call("resources.set", new JsonObject { ["name"] = "Ion.GameTime", ["value"] = 1 })));
		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("resources.get", new JsonObject { ["name"] = "Nope" })));
	}

	[Fact]
	public void PauseStepResumeAndExitControlTheLoop()
	{
		using var game = new RemoteGame().RunInBackground();
		var paused = game.Result("game.pause")!;
		Assert.True(paused["paused"]!.GetValue<bool>());
		Assert.True(game.Server.IsPaused);

		// While paused, the game thread serves requests inside the remote step and time does not move.
		var frame = game.Result("game.info")!["frame"]!.GetValue<uint>();
		Thread.Sleep(50);
		Assert.Equal(frame, game.Result("game.info")!["frame"]!.GetValue<uint>());

		// A step answers once its frames have run, paused again.
		var stepped = game.Result("game.step", new JsonObject { ["frames"] = 2 })!;
		Assert.True(stepped["paused"]!.GetValue<bool>());
		Assert.Equal(frame + 2, stepped["frame"]!.GetValue<uint>());
		Assert.Equal(frame + 2, game.Result("game.info")!["frame"]!.GetValue<uint>());

		Assert.False(game.Result("game.resume")!["paused"]!.GetValue<bool>());
		Assert.False(game.Server.IsPaused);

		Assert.True(game.Result("game.exit")!["exiting"]!.GetValue<bool>());
		for (var i = 0; i < 1000 && !game.BackgroundLoopEnded; i++) Thread.Sleep(5);
		Assert.True(game.Host.IsExitRequested);
	}

	[Fact]
	public void PauseAtFramePausesAfterThatManyFrames()
	{
		using var game = new RemoteGame(configure: host => host.WithConfiguration("Ion:Remote:PauseAtFrame", "3")).RunInBackground();
		for (var i = 0; i < 2000 && !game.Server.IsPaused; i++) Thread.Sleep(5);
		Assert.True(game.Server.IsPaused);
		// Paused in the remote step of frame 2 (the third frame).
		Assert.Equal(2u, game.Result("game.info")!["frame"]!.GetValue<uint>());
		Assert.False(game.Result("game.resume")!["paused"]!.GetValue<bool>());
	}

	[Fact]
	public void RegistrySchemaListsTheSerializableComponents()
	{
		using var game = new RemoteGame();
		var components = game.Result("registry.schema")!["components"]!.AsArray();
		var transform = components.Single(c => c!["name"]!.GetValue<string>() == "Transform2D")!;
		Assert.Equal(typeof(Transform2D).FullName, transform["type"]!.GetValue<string>());
		Assert.True(components.Single(c => c!["name"]!.GetValue<string>() == "Hidden")!["tag"]!.GetValue<bool>());
	}

	[Fact]
	public void WorldListAndQuery()
	{
		using var game = new RemoteGame();
		Assert.Equal(2, game.Result("world.list")!.AsArray()[^1]!["entities"]!.GetValue<int>());

		var all = game.Result("world.query")!;
		Assert.Equal(2, all["total"]!.GetValue<int>());

		var ball = game.Result("world.query", new JsonObject { ["name"] = "Ba*", ["components"] = new JsonArray("Transform2D") })!["entities"]!.AsArray().Single()!;
		Assert.Equal("Ball", ball["name"]!.GetValue<string>());
		Assert.Equal(10, ball["components"]!["Transform2D"]!["Position"]![0]!.GetValue<float>());

		var hidden = game.Result("world.query", new JsonObject { ["with"] = new JsonArray("Hidden") })!["entities"]!.AsArray();
		Assert.Equal("Paddle", hidden.Single()!["name"]!.GetValue<string>());
		var visible = game.Result("world.query", new JsonObject { ["without"] = new JsonArray("Hidden") })!["entities"]!.AsArray();
		Assert.Equal("Ball", visible.Single()!["name"]!.GetValue<string>());

		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("world.query", new JsonObject { ["with"] = new JsonArray("NotAComponent") })));
	}

	[Fact]
	public void WorldGetAndListComponentsByNameOrId()
	{
		using var game = new RemoteGame();
		var byName = game.Result("world.get_components", new JsonObject { ["entity"] = "Ball" })!;
		var id = byName["entity"]!.GetValue<int>();
		var byId = game.Result("world.get_components", new JsonObject { ["entity"] = id, ["components"] = new JsonArray("EntityName") })!;
		Assert.Equal("Ball", byId["components"]!["EntityName"]!["Value"]!.GetValue<string>());

		var listed = game.Result("world.list_components", new JsonObject { ["entity"] = "Ball" })!;
		Assert.Contains(listed["components"]!.AsArray(), c => c!.GetValue<string>() == "Transform2D");
		Assert.Contains(listed["other"]!.AsArray(), c => c!.GetValue<string>() == nameof(GlobalTransform2D));

		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("world.get_components", new JsonObject { ["entity"] = "Nobody" })));
		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("world.get_components", new JsonObject { ["entity"] = "Ball", ["components"] = new JsonArray("Hidden") })));
	}

	[Fact]
	public void WorldInsertMutateAndRemoveComponents()
	{
		using var game = new RemoteGame();
		game.Result("world.insert_components", new JsonObject { ["entity"] = "Ball", ["components"] = new JsonObject { ["Hidden"] = new JsonObject() } });
		var ball = EcsEntitiesForTests.Find(game.World, "Ball");
		Assert.True(game.World.Has<Hidden>(ball));

		var mutated = game.Result("world.mutate_components", new JsonObject { ["entity"] = "Ball", ["component"] = "Transform2D", ["path"] = "Position.1", ["value"] = 99 })!;
		Assert.Equal(99, mutated["value"]!["Position"]![1]!.GetValue<float>());
		Assert.Equal(new Vector2(10, 99), game.World.Get<Transform2D>(ball).Position);

		game.Result("world.mutate_components", new JsonObject { ["entity"] = "Ball", ["component"] = "Transform2D", ["path"] = "rotation", ["value"] = 1.5 });
		Assert.Equal(1.5f, game.World.Get<Transform2D>(ball).Rotation);

		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("world.mutate_components", new JsonObject { ["entity"] = "Ball", ["component"] = "Transform2D", ["path"] = "Nope", ["value"] = 1 })));

		var removed = game.Result("world.remove_components", new JsonObject { ["entity"] = "Ball", ["components"] = new JsonArray("Hidden") })!;
		Assert.Equal("Hidden", removed["removed"]!.AsArray().Single()!.GetValue<string>());
		Assert.False(game.World.Has<Hidden>(ball));
	}

	[Fact]
	public void WorldSpawnAndDespawn()
	{
		using var game = new RemoteGame();
		var spawned = game.Result("world.spawn", new JsonObject
		{
			["name"] = "Brick",
			["components"] = new JsonObject { ["Transform2D"] = new JsonObject { ["Position"] = new JsonArray(5, 6), ["Rotation"] = 0, ["Scale"] = new JsonArray(1, 1) } },
		})!;
		var id = spawned["entity"]!.GetValue<int>();
		Assert.Equal(3, game.World.Size);
		var brick = EcsEntitiesForTests.Find(game.World, "Brick");
		Assert.Equal(new Vector2(5, 6), game.World.Get<Transform2D>(brick).Position);

		// A typo in a component name creates nothing.
		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(game.Call("world.spawn", new JsonObject { ["components"] = new JsonObject { ["Transfrom2D"] = new JsonObject() } })));
		Assert.Equal(3, game.World.Size);

		game.Result("world.despawn", new JsonObject { ["entity"] = id });
		Assert.Equal(2, game.World.Size);
	}
}

internal static class EcsEntitiesForTests
{
	public static Arch.Core.Entity Find(Arch.Core.World world, string name)
	{
		var result = Arch.Core.Entity.Null;
		world.Query(new Arch.Core.QueryDescription().WithAll<EntityName>(), (Arch.Core.Entity entity, ref EntityName n) =>
		{
			if (n.Value == name) result = entity;
		});
		return result;
	}
}

/// <summary>A fact that needs a Vulkan or headless OpenGL ES driver (Mesa lavapipe or llvmpipe on CI).</summary>
public sealed class RenderingFactAttribute : FactAttribute
{
	public RenderingFactAttribute()
	{
		if (!RenderingEnvironment.HasVulkan && !RenderingEnvironment.HasHeadlessGles) Skip = "No Vulkan or headless OpenGL ES driver (install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}
