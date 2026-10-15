using System.Text.Json.Serialization;

using Ion;

namespace MyIonGame;

// The game's settings and components (Program.cs registers them).

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
