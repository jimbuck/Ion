using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;

namespace MyIonGame;

/// <summary>
/// A paddle (Left/Right or A/D) keeps a ball in play. Systems are plain classes: constructor parameters are injected, and
/// each public method with a stage attribute is a step of that stage, run every frame in order.
/// </summary>
public sealed class PaddleSystem(PlayState state, GameSettings settings, IInputState input, IWindow window, ISpriteBatch sprites, IMetrics metrics)
{
	/// <summary>The paddle size.</summary>
	public static readonly Vector2 PaddleSize = new(120, 16);

	/// <summary>The ball size.</summary>
	public const float BallSize = 12;

	private static readonly Color PaddleColor = new(0xE8, 0xE8, 0xF0);
	private static readonly Color BallColor = new(0xFF, 0x8C, 0x28);

	private readonly MetricsCounter _hits = metrics.Counter("hits", description: "Paddle hits.");
	private readonly Random _random = new(settings.Seed);

	private float PaddleY => window.Size.Y - 40;

	/// <summary>Places the paddle and serves the first ball.</summary>
	[Init]
	public void Start(GameTime dt)
	{
		state.PaddleX = (window.Size.X - PaddleSize.X) / 2;
		Serve();
	}

	/// <summary>Moves the paddle with the keyboard (per frame, so it follows the display rate).</summary>
	[Update]
	public void MovePaddle(GameTime dt)
	{
		var direction = 0f;
		if (input.Down(Key.Left) || input.Down(Key.A)) direction -= 1;
		if (input.Down(Key.Right) || input.Down(Key.D)) direction += 1;
		state.PaddleX = Math.Clamp(state.PaddleX + direction * settings.PaddleSpeed * dt.Delta, 0, window.Size.X - PaddleSize.X);
	}

	/// <summary>Moves the ball (fixed step, 60 Hz by default, so the simulation does not depend on the frame rate).</summary>
	[FixedUpdate]
	public void MoveBall(GameTime dt)
	{
		var size = window.Size;
		state.BallX += state.BallVelocityX * dt.Delta;
		state.BallY += state.BallVelocityY * dt.Delta;

		if (state.BallX < BallSize / 2 || state.BallX > size.X - BallSize / 2)
		{
			state.BallVelocityX = -state.BallVelocityX;
			state.BallX = Math.Clamp(state.BallX, BallSize / 2, size.X - BallSize / 2);
		}

		if (state.BallY < BallSize / 2)
		{
			state.BallVelocityY = Math.Abs(state.BallVelocityY);
		}

		var paddleTop = PaddleY;
		if (state.BallVelocityY > 0 && state.BallY + BallSize / 2 >= paddleTop && state.BallY < paddleTop + PaddleSize.Y
			&& state.BallX >= state.PaddleX && state.BallX <= state.PaddleX + PaddleSize.X)
		{
			state.BallVelocityY = -Math.Abs(state.BallVelocityY);
			state.Score++;
			_hits.Increment();
		}

		if (state.BallY > size.Y + BallSize)
		{
			state.Misses++;
			Serve();
		}
	}

	/// <summary>Draws the paddle and the ball (Render runs inside the engine's sprite batch scope).</summary>
	[Render]
	public void Draw(GameTime dt)
	{
		sprites.DrawRect(PaddleColor, new Vector2(state.PaddleX, PaddleY), PaddleSize);
		sprites.DrawRect(BallColor, new Vector2(state.BallX, state.BallY) - new Vector2(BallSize / 2), new Vector2(BallSize));
	}

	private void Serve()
	{
		state.BallX = window.Size.X / 2;
		state.BallY = window.Size.Y / 3;
		var angle = (0.25f + 0.5f * _random.NextSingle()) * MathF.PI; // downwards, 45 to 135 degrees
		state.BallVelocityX = MathF.Cos(angle) * settings.BallSpeed;
		state.BallVelocityY = MathF.Sin(angle) * settings.BallSpeed;
	}
}
