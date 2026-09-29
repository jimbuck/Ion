using System.Globalization;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

using Ion;

namespace MyIonGame;

// The game's settings and state (Program.cs registers them).

/// <summary>Settings from configuration (appsettings.json, command line): <c>Game:*</c> and <c>Ion:Seed</c>.</summary>
public sealed record GameSettings(float PaddleSpeed, float BallSpeed, int Seed)
{
	/// <summary>Reads the settings.</summary>
	public static GameSettings From(IConfiguration config) => new(
		float.TryParse(config["Game:PaddleSpeed"], NumberStyles.Float, CultureInfo.InvariantCulture, out var paddle) ? paddle : 480f,
		float.TryParse(config["Game:BallSpeed"], NumberStyles.Float, CultureInfo.InvariantCulture, out var ball) ? ball : 300f,
		IonRun.Seed(config, fallback: 1));
}

/// <summary>Everything that changes during play. Plain data, so tests snapshot it and tools read and write it.</summary>
public sealed class PlayState
{
	/// <summary>The paddle's left edge.</summary>
	public float PaddleX { get; set; }

	/// <summary>The ball's centre.</summary>
	public float BallX { get; set; }

	/// <summary>The ball's centre.</summary>
	public float BallY { get; set; }

	/// <summary>The ball's velocity in pixels per second.</summary>
	public float BallVelocityX { get; set; }

	/// <summary>The ball's velocity in pixels per second.</summary>
	public float BallVelocityY { get; set; }

	/// <summary>Paddle hits.</summary>
	public int Score { get; set; }

	/// <summary>Balls lost.</summary>
	public int Misses { get; set; }

	/// <summary>Copies every value from <paramref name="other"/>.</summary>
	public void CopyFrom(PlayState other)
	{
		PaddleX = other.PaddleX;
		BallX = other.BallX;
		BallY = other.BallY;
		BallVelocityX = other.BallVelocityX;
		BallVelocityY = other.BallVelocityY;
		Score = other.Score;
		Misses = other.Misses;
	}
}

/// <summary>Source-generated JSON metadata (works under NativeAOT).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlayState))]
public sealed partial class GameJson : JsonSerializerContext
{
}
