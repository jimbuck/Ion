using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Physics2D;
using Ion.Extensions.UI;
using Ion.Tests;

namespace Ion.Extensions.Remote.Tests;

/// <summary>A small screen for the ui.* methods.</summary>
public sealed class PanelSystem(Ui ui)
{
	public int Clicks;

	public bool Sound;

	public float Volume = 0.5f;

	public string Name = "Ada";

	public bool Back;

	[Update]
	public void Build(GameTime dt)
	{
		using (ui.Panel("menu"))
		{
			if (ui.Button("Go")) Clicks++;
			ui.Toggle("Sound", ref Sound);
			ui.Slider("Volume", ref Volume, 0f, 1f, 0.1f);
			ui.TextInput("Name", ref Name, maxLength: 20);
			using (ui.Disabled()) ui.Button("Locked");
		}

		if (ui.BackPressed) Back = true;
	}
}

/// <summary>The UI module's methods over the protocol: ui.tree, ui.click, ui.set_value, ui.focus, ui.type, ui.back.</summary>
[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class UiRemoteTests
{
	private static RemoteGame Game(bool mutations = true) => new(mutations, host => host
		.Configure(static s => s.AddUi().AddUiRemote().AddUiRemote().AddSingleton<PanelSystem>())
		.ConfigureApp(static a => a.UseUi().UseSystem<PanelSystem>()));

	private static JsonObject Node(JsonNode tree, string path) =>
		tree["nodes"]!.AsArray().Select(static n => n!.AsObject()).Single(n => n["path"]!.GetValue<string>() == path);

	[Fact]
	public void TheTreeListsEveryNodeWithItsState()
	{
		using var game = Game();
		game.Host.Step(2);
		var tree = game.Result("ui.tree")!;
		var paths = tree["nodes"]!.AsArray().Select(static n => n!["path"]!.GetValue<string>()).ToList();
		Assert.Equal(["menu", "menu/Go", "menu/Sound", "menu/Volume", "menu/Name", "menu/Locked"], paths);
		Assert.Equal(6, tree["count"]!.GetValue<int>());
		Assert.Equal("button", Node(tree, "menu/Go")["kind"]!.GetValue<string>());
		Assert.Equal("text_input", Node(tree, "menu/Name")["kind"]!.GetValue<string>());
		Assert.Equal("0.5", Node(tree, "menu/Volume")["value"]!.GetValue<string>());
		Assert.False(Node(tree, "menu/Locked")["enabled"]!.GetValue<bool>());
		Assert.Equal(4, Node(tree, "menu/Go")["rect"]!.AsArray().Count);

		var filtered = game.Result("ui.tree", new JsonObject { ["prefix"] = "menu/S" })!;
		Assert.Equal("menu/Sound", Assert.Single(filtered["nodes"]!.AsArray())!["path"]!.GetValue<string>());

		var discover = game.Result("rpc.discover")!["methods"]!.AsArray().ToDictionary(static m => m!["name"]!.GetValue<string>(), static m => m!["access"]!.GetValue<string>());
		Assert.Equal("read", discover["ui.tree"]);
		foreach (var name in new[] { "ui.click", "ui.set_value", "ui.focus", "ui.type", "ui.back" }) Assert.Equal("mutate", discover[name]);
	}

	[Fact]
	public void CommandsDriveTheScreen()
	{
		using var game = Game();
		game.Host.Step(2);
		var panel = game.Host.Get<PanelSystem>();

		game.Result("ui.click", new JsonObject { ["path"] = "menu/Go" });
		game.Result("ui.click", new JsonObject { ["path"] = "menu/Sound" });
		game.Result("ui.set_value", new JsonObject { ["path"] = "menu/Volume", ["value"] = 0.8 });
		game.Result("ui.set_value", new JsonObject { ["path"] = "menu/Name", ["value"] = "Grace" });
		game.Host.Step(2);
		game.Result("ui.type", new JsonObject { ["path"] = "menu/Name", ["text"] = " H." });
		game.Host.Step(2);
		game.Result("ui.focus", new JsonObject { ["path"] = "menu/Volume" });
		game.Host.Step(2);

		Assert.Equal(1, panel.Clicks);
		Assert.True(panel.Sound);
		Assert.Equal(0.8f, panel.Volume, 3);
		Assert.Equal("Grace H.", panel.Name);
		var tree = game.Result("ui.tree")!;
		Assert.Equal("menu/Volume", tree["focused"]!.GetValue<string>());
		Assert.Equal("true", Node(tree, "menu/Sound")["value"]!.GetValue<string>());

		game.Result("ui.back");
		game.Host.Step(2);
		Assert.True(panel.Back);
	}

	[Fact]
	public void BadPathsAndReadSessionsAreRefused()
	{
		using var game = Game();
		game.Host.Step(2);
		var missing = game.Call("ui.click", new JsonObject { ["path"] = "menu/Nope" });
		Assert.Equal(RemoteErrorCodes.NotFound, RemoteGame.ErrorCode(missing));
		Assert.Contains("no node has this path", missing["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
		var disabled = game.Call("ui.click", new JsonObject { ["path"] = "menu/Locked" });
		Assert.Contains("disabled", disabled["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(game.Call("ui.click")));
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(game.Call("ui.set_value", new JsonObject { ["path"] = "menu/Volume", ["value"] = new JsonArray() })));

		var read = game.Call("ui.click", new JsonObject { ["path"] = "menu/Go" }, token: game.Server.ReadToken);
		Assert.Equal(RemoteErrorCodes.Forbidden, RemoteGame.ErrorCode(read));
	}

	[Fact]
	public void WithoutAUiTheMethodsSayWhy()
	{
		using var game = new RemoteGame(configure: static host => host.Configure(static s => s.AddUiRemote()));
		Assert.Equal(RemoteErrorCodes.Unsupported, RemoteGame.ErrorCode(game.Call("ui.tree")));
	}
}

/// <summary>The 2D physics module's methods over the protocol: physics2d.bodies and physics2d.raycast.</summary>
[Trait(TestConstants.CATEGORY, TestConstants.INTEGRATION)]
public sealed class Physics2DRemoteTests
{
	private static RemoteGame Game() => new(configure: static host => host
		.Configure(static s => s.AddPhysics2D(configure: static p => p.GravityY = 0).AddPhysics2DRemote())
		.ConfigureApp(static a =>
		{
			a.UsePhysics2D();
			a.Init((GameTime dt, World world) =>
			{
				world.Create(new EntityName("Wall"), new Transform2D(new Vector2(100, 0)), Collider2D.Box(new Vector2(20, 200)));
				world.Create(new EntityName("Ball"), new Transform2D(new Vector2(0, 50)), Collider2D.Circle(5), RigidBody2D.Dynamic(new Vector2(10, 0)));
				world.Create(new EntityName("Sensor"), new Transform2D(new Vector2(50, 0)), Collider2D.Circle(10) with { IsSensor = true });
			});
		}));

	[Fact]
	public void BodiesListsTheSimulatedEntities()
	{
		using var game = Game();
		game.Host.Step(5);
		var result = game.Result("physics2d.bodies")!;
		var bodies = result["bodies"]!.AsArray().Select(static b => b!.AsObject()).ToDictionary(static b => b["name"]!.GetValue<string>());
		Assert.Equal(3, result["total"]!.GetValue<int>());
		Assert.Equal("static", bodies["Wall"]["type"]!.GetValue<string>());
		Assert.Equal("box", bodies["Wall"]["shape"]!.GetValue<string>());
		Assert.Equal("dynamic", bodies["Ball"]["type"]!.GetValue<string>());
		Assert.Equal("circle", bodies["Ball"]["shape"]!.GetValue<string>());
		Assert.True(bodies["Ball"]["position"]![0]!.GetValue<float>() > 0, "The ball moved right.");
		Assert.Equal(10f, bodies["Ball"]["velocity"]![0]!.GetValue<float>(), 3);
		Assert.True(bodies["Sensor"]["sensor"]!.GetValue<bool>());
		Assert.True(result["stepCount"]!.GetValue<long>() > 0);
		Assert.Equal(0f, result["gravity"]![1]!.GetValue<float>());

		var dynamic = game.Result("physics2d.bodies", new JsonObject { ["type"] = "dynamic" })!;
		Assert.Equal("Ball", Assert.Single(dynamic["bodies"]!.AsArray())!["name"]!.GetValue<string>());
		var named = game.Result("physics2d.bodies", new JsonObject { ["name"] = "W*", ["limit"] = 1 })!;
		Assert.Equal("Wall", Assert.Single(named["bodies"]!.AsArray())!["name"]!.GetValue<string>());
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(game.Call("physics2d.bodies", new JsonObject { ["type"] = "floaty" })));
	}

	[Fact]
	public void RaycastFindsTheClosestHit()
	{
		using var game = Game();
		game.Host.Step(2);
		var hit = game.Result("physics2d.raycast", new JsonObject { ["origin"] = new JsonArray(0, -50), ["to"] = new JsonArray(200, -50) })!;
		Assert.True(hit["hit"]!.GetValue<bool>());
		Assert.Equal("Wall", hit["name"]!.GetValue<string>());
		Assert.Equal(90f, hit["point"]![0]!.GetValue<float>(), 1);
		Assert.Equal(-1f, hit["normal"]![0]!.GetValue<float>(), 3);

		var miss = game.Result("physics2d.raycast", new JsonObject { ["origin"] = new JsonArray(0, -500), ["translation"] = new JsonArray(10, 0) })!;
		Assert.False(miss["hit"]!.GetValue<bool>());
		Assert.Equal(RemoteErrorCodes.InvalidParams, RemoteGame.ErrorCode(game.Call("physics2d.raycast", new JsonObject { ["origin"] = new JsonArray(0) })));

		// Read methods: the read token is enough.
		var read = game.Call("physics2d.raycast", new JsonObject { ["origin"] = new JsonArray(0, 0), ["to"] = new JsonArray(200, 0) }, token: game.Server.ReadToken);
		Assert.Null(read["error"]);
	}
}
