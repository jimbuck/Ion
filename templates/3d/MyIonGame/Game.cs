using System.Globalization;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

namespace MyIonGame;

// The game's settings and state (Program.cs registers them).

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
