using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

namespace MyIonGame;

/// <summary>
/// The game's setup: services (engine, ECS, the game's systems) and schedule. Program.cs and the tests both use it.
/// </summary>
public sealed class Game : IIonGame
{
	/// <summary>Registers the engine, the ECS module, the component serializers and the game's systems.</summary>
	public static void Configure(IonApplicationBuilder builder)
	{
		builder.Services.AddIon(builder.Configuration, graphics => graphics.ClearColor = new Color(0x1B, 0x26, 0x3B));
		builder.Services.AddEcs();

		// Components registered here are saved by the world serializers (world snapshots in tests) and are readable and
		// writable over the remote protocol (world.query, world.mutate_components). Register every component you add.
		builder.Services.AddEcsSerialization(components => components
			.AddUnmanaged("Velocity", GameJson.Default.Velocity)
			.AddTag<Ball>("Ball"));

		builder.Services.AddSingleton<GameSettings>(_ => GameSettings.From(builder.Configuration));
		builder.Services.AddSingleton<BallSystem>();
		builder.Services.AddSingleton<DrawSystem>();
	}

	/// <summary>Adds the engine's systems, the ECS systems and the game's systems to the schedule.</summary>
	public static void Use(IIonApplication app)
	{
		app.UseIon()
			.UseEcs()
			.UseSystem<BallSystem>()
			.UseSystem<DrawSystem>();
	}
}

/// <summary>Settings from configuration (appsettings.json, command line): <c>Game:Balls</c> and <c>Ion:Seed</c>.</summary>
public sealed record GameSettings(int Balls, int Seed)
{
	/// <summary>Reads the settings.</summary>
	public static GameSettings From(Microsoft.Extensions.Configuration.IConfiguration config) =>
		new(int.TryParse(config["Game:Balls"], out var balls) ? balls : 8, IonRun.Seed(config, fallback: 1));
}

/// <summary>A ball's velocity in pixels per second.</summary>
public record struct Velocity(float X, float Y);

/// <summary>Tags ball entities.</summary>
public record struct Ball;

/// <summary>Source-generated JSON metadata of the game's components (works under NativeAOT).</summary>
[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(Velocity))]
internal sealed partial class GameJson : JsonSerializerContext
{
}
