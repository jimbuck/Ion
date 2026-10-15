using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Networking;

// The ECS module's 2D transform is replicated too: the networking generator writes its serializer into this assembly.
// Remote entities are drawn interpolated between snapshots.
[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]

namespace Ion.Examples.Breakout.Net;

/// <summary>A ball (replicated). <see cref="Owner"/> is the peer whose launch created it.</summary>
[Replicated]
public record struct Ball(byte Owner);

/// <summary>A block (replicated).</summary>
[Replicated]
public record struct Block(int Row, int Column);

/// <summary>A player's paddle (replicated); <see cref="Player"/> is the owning peer id.</summary>
[Replicated]
public record struct Paddle(byte Player);

/// <summary>
/// Where a paddle is (its center x). Predicted: the owning client moves its paddle from its own input immediately, the
/// server applies the same input when it arrives, and the client corrects when the two differ.
/// </summary>
[Replicated(Authority = Authority.Owner), Predicted]
public record struct PaddleControl(float X);

/// <summary>
/// The score of the game, on a singleton entity of the root world (replicated, so a client that joins late sees it, and
/// kept across levels). <see cref="Round"/> is the number of the level being played or coming next.
/// </summary>
[Replicated]
public record struct Scoreboard(int Score, int BallsLost, int Round);

/// <summary>The game's scenes: the menu between levels, and the level, whose world is networked as <c>"level"</c>.</summary>
public enum GameScene
{
	Menu = 0,
	Level = 1,
}

/// <summary>
/// Where the game is, on a singleton entity of the root world: the scene every peer should be in, and the seconds left
/// before the next level (in the menu) or before the level ends (when levels are timed). The server decides; the clients
/// follow it with their own scene changes (<see cref="FlowSystem"/>).
/// </summary>
[Replicated]
public record struct Stage(GameScene Scene, float SecondsLeft);

/// <summary>A player's input for one tick: where the paddle should go.</summary>
[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct PaddleInput(float TargetX);

/// <summary>A player asks for a ball above its paddle.</summary>
[NetworkMessage(Direction = MessageDirection.ClientToServer)]
public record struct LaunchBall();

/// <summary>The server tells the clients a block broke (for the sound), unreliably: a lost one is only a missing sound.</summary>
[NetworkMessage(Delivery = Delivery.Unreliable)]
public record struct BlockBroken(Vector2 Position);

/// <summary>A wall around the field: created by the server only, never networked.</summary>
public record struct Wall();

/// <summary>
/// The pacing of the game: <c>Breakout:MenuSeconds</c> in the menu before each level once someone plays (default 3), and
/// <c>Breakout:LevelSeconds</c> per level (default 0: until every block is broken).
/// </summary>
public sealed record BreakoutSettings(float MenuSeconds, float LevelSeconds)
{
	public static BreakoutSettings From(Microsoft.Extensions.Configuration.IConfiguration config) => new(
		float.TryParse(config["Breakout:MenuSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var menu) ? Math.Max(0, menu) : 3f,
		float.TryParse(config["Breakout:LevelSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var level) ? Math.Max(0, level) : 0f);
}

/// <summary>The play field, the same on every peer (the window is sized to it).</summary>
public static class Field
{
	public const int Columns = 10;
	public const int Rows = 8;
	public const float Gap = 10f;
	public const int MaxBalls = 64;
	public const float PaddleSpeed = 1500f;
	public const float BallSpeed = 450f;
	public const float PixelsPerMeter = 64f;

	public static readonly Vector2 BlockSize = new(192f, 64f);
	public static readonly Vector2 PaddleSize = new(244f, 64f);
	public static readonly Vector2 BallSize = new(32f, 32f);

	public static float Width => Columns * BlockSize.X + (Columns + 1) * Gap;

	public static float Height => Rows * BlockSize.Y + (Rows + 1) * Gap + 300f + PaddleSize.Y + 20f;

	public static float PaddleY => Height - 20f - PaddleSize.Y / 2f;

	/// <summary>
	/// The paddle's prediction step, run by both the client (for its own paddle, before the server has seen the input)
	/// and the server (for every player's paddle): towards the target at a bounded speed. Deterministic, depends only on
	/// its arguments.
	/// </summary>
	public static void MovePaddle(ref PaddleControl control, in PaddleInput input, float delta)
	{
		var half = PaddleSize.X / 2f;
		var target = Math.Clamp(input.TargetX, half, Width - half);
		var step = PaddleSpeed * delta;
		control.X = Math.Clamp(control.X + Math.Clamp(target - control.X, -step, step), half, Width - half);
	}
}
