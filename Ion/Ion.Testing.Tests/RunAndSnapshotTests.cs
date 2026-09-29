using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Metrics;
using Ion.Testing;

namespace Ion.Tests;

/// <summary>A tiny ECS game for <see cref="IonTestHost.Run{TGame}"/>: a ball that moves one unit per frame.</summary>
public sealed class BallGame : IIonGame
{
	public static void Configure(IonApplicationBuilder builder)
	{
		builder.Services.AddIon(builder.Configuration);
		builder.Services.AddEcs().AddEcsSerialization();
		builder.Services.AddSingleton<BallSystem>();
	}

	public static void Use(IIonApplication app) => app.UseIon().UseEcs().UseSystem<BallSystem>();
}

public sealed class BallSystem(World world, IMetrics metrics)
{
	private readonly MetricsCounter _moves = metrics.Counter("moves");

	[Init]
	public void Spawn(GameTime dt) => world.Create(new EntityName("Ball"), new Transform2D(new System.Numerics.Vector2(0.1234567f, 0)));

	[Update]
	public void Move(GameTime dt)
	{
		world.Query(new QueryDescription().WithAll<Transform2D>(), (ref Transform2D t) => t.Position.X += 1);
		_moves.Increment();
	}
}

public sealed class RunAndSnapshotTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "ion-testing-tests", Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
	}

	[Fact]
	public void RunReturnsStateCountersAndStats()
	{
		using var run = IonTestHost.Run<BallGame>(10);
		Assert.Equal(10, run.Frames);
		Assert.Equal(10, run.Counters["moves"]);
		Assert.Equal(1, run.LastFrame.Entities);
		Assert.Null(run.Image);
		var world = run.Get<EcsWorlds>().Root;
		Assert.Equal(1, world.Size);
		Assert.Contains("\"Transform2D\"", run.WorldJson(), StringComparison.Ordinal);
	}

	[Fact]
	public void WorldSnapshotsAreNormalizedAndCompared()
	{
		using var run = IonTestHost.Run<BallGame>(3);
		var json = run.WorldJson(decimals: 2)!;
		Assert.Contains("3.12", json, StringComparison.Ordinal);
		Assert.DoesNotContain("3.1234", json, StringComparison.Ordinal);

		var path = Path.Combine(_dir, "ball.json");
		var missing = Assert.Throws<JsonSnapshotException>(() => JsonSnapshot.AssertMatches(json, path));
		Assert.Contains("did not exist", missing.Message, StringComparison.Ordinal);
		JsonSnapshot.AssertMatches(json, path);

		run.Host.Step();
		var moved = run.WorldJson(decimals: 2)!;
		var mismatch = Assert.Throws<JsonSnapshotException>(() => JsonSnapshot.AssertMatches(moved, path));
		Assert.Contains("4.12", mismatch.Message, StringComparison.Ordinal);
		Assert.True(File.Exists(Path.ChangeExtension(path, ".actual.json")));
	}

	[Fact]
	public void NormalizeRoundsIgnoresAndSorts()
	{
		var options = JsonSnapshotOptions.Default with { Decimals = 1, Ignore = new HashSet<string> { "time" }, SortProperties = true };
		var normalized = JsonSnapshot.Normalize("{\"b\":1.26,\"a\":-0.01,\"time\":\"now\",\"list\":[2.0,3]}", options);
		Assert.Equal("{\n  \"a\": 0,\n  \"b\": 1.3,\n  \"list\": [\n    2,\n    3\n  ]\n}\n", normalized);
	}

	[Fact]
	public void RunConfigurationWritesTheSummary()
	{
		var summary = Path.Combine(_dir, "run.json");
		using (var host = new IonTestHost().UseGame(BallGame.Configure, BallGame.Use)
			.WithConfiguration("Ion:Run:Summary", summary)
			.WithConfiguration("Ion:Seed", "42"))
		{
			host.Step(5);
		}

		var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(summary))!;
		Assert.Equal("ok", json["status"]!.GetValue<string>());
		Assert.Equal(5, json["frames"]!.GetValue<int>());
		Assert.Equal(42, json["seed"]!.GetValue<int>());
		Assert.Equal(5, json["counters"]!["moves"]!.GetValue<int>());
		Assert.Equal(5, json["frameStats"]!["count"]!.GetValue<int>());
		Assert.Contains("BallSystem.Move", json["schedule"]!.GetValue<string>(), StringComparison.Ordinal);
		Assert.Empty(json["errors"]!.AsArray());
	}
}
