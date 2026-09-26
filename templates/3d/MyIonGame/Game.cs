using System.Globalization;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Remote;
using Ion.Extensions.Rendering3D;

namespace MyIonGame;

/// <summary>
/// The game's setup: services (engine, 3D renderer, the game's systems) and schedule. Program.cs and the tests both use it.
/// </summary>
public sealed class Game : IIonGame
{
	/// <summary>Registers the engine, the 3D renderer, the game's state and its systems.</summary>
	public static void Configure(IonApplicationBuilder builder)
	{
		builder.Services.AddIon(builder.Configuration);
		builder.Services.AddRendering3D(builder.Configuration);
		builder.Services.AddSingleton(_ => GameSettings.From(builder.Configuration));
		builder.Services.AddSingleton<SpinState>();
		builder.Services.AddSingleton<SceneSystem3D>();

		// Readable and writable on a running game (--remote-allow-mutations): resources.get/set "Game.Spin".
		builder.Services.AddRemoteResource("Game.Spin", "The cubes' spin angle and speed.", GameJson.Default.SpinState,
			static sp => sp.GetRequiredService<SpinState>(),
			static (sp, value) => sp.GetRequiredService<SpinState>().CopyFrom(value));
	}

	/// <summary>Adds the engine's systems, the 3D renderer's and the game's to the schedule.</summary>
	public static void Use(IIonApplication app)
	{
		app.UseIon()
			.UseRendering3D()
			.UseSystem<SceneSystem3D>();
	}
}

/// <summary>Settings from configuration (appsettings.json, command line): <c>Game:*</c>.</summary>
public sealed record GameSettings(int Cubes, float SpinSpeed)
{
	/// <summary>Reads the settings.</summary>
	public static GameSettings From(IConfiguration config) => new(
		int.TryParse(config["Game:Cubes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cubes) ? cubes : 5,
		float.TryParse(config["Game:SpinSpeed"], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : 1.2f);
}

/// <summary>The state that changes during play. Plain data, so tests snapshot it and tools read and write it.</summary>
public sealed class SpinState
{
	/// <summary>The spin angle in radians.</summary>
	public float Angle { get; set; }

	/// <summary>The spin speed in radians per second.</summary>
	public float Speed { get; set; }

	/// <summary>Fixed steps simulated.</summary>
	public long Steps { get; set; }

	/// <summary>Copies every value from <paramref name="other"/>.</summary>
	public void CopyFrom(SpinState other)
	{
		Angle = other.Angle;
		Speed = other.Speed;
		Steps = other.Steps;
	}
}

/// <summary>Source-generated JSON metadata (works under NativeAOT).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SpinState))]
public sealed partial class GameJson : JsonSerializerContext
{
}
