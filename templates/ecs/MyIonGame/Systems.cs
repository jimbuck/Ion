using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;

namespace MyIonGame;

/// <summary>
/// Spawns the balls and moves them, bouncing off the window edges. Systems are plain classes: constructor parameters are
/// injected, and each public method with a stage attribute is a step of that stage. <c>[Query]</c> methods run once per
/// entity with the components of their <c>ref</c>/<c>in</c> parameters (the class must be <c>partial</c>).
/// </summary>
public sealed partial class BallSystem(World world, IWindow window, GameSettings settings, IMetrics metrics)
{
	/// <summary>The ball radius in pixels.</summary>
	public const float Radius = 12f;

	private readonly MetricsCounter _bounces = metrics.Counter("bounces", description: "Balls bouncing off a window edge.");

	/// <summary>Creates the balls at seeded random positions and velocities.</summary>
	[Init]
	public void Spawn(GameTime dt)
	{
		var random = new Random(settings.Seed);
		var size = window.Size;
		for (var i = 0; i < settings.Balls; i++)
		{
			var position = new Vector2(Radius + random.NextSingle() * (size.X - 2 * Radius), Radius + random.NextSingle() * (size.Y - 2 * Radius));
			var angle = random.NextSingle() * MathF.Tau;
			var speed = 120f + random.NextSingle() * 180f;
			world.Create(new EntityName($"Ball{i}"), new Ball(), new Transform2D(position), new Velocity(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed));
		}
	}

	/// <summary>Moves every ball by its velocity and bounces it off the window edges (fixed step: 60 Hz by default).</summary>
	[FixedUpdate, Query]
	private void Move(ref Transform2D transform, ref Velocity velocity, [Data] in float dt)
	{
		var size = window.Size;
		var p = transform.Position + new Vector2(velocity.X, velocity.Y) * dt;
		if (p.X < Radius || p.X > size.X - Radius)
		{
			velocity.X = -velocity.X;
			p.X = Math.Clamp(p.X, Radius, size.X - Radius);
			_bounces.Increment();
		}

		if (p.Y < Radius || p.Y > size.Y - Radius)
		{
			velocity.Y = -velocity.Y;
			p.Y = Math.Clamp(p.Y, Radius, size.Y - Radius);
			_bounces.Increment();
		}

		transform.Position = p;
	}
}

/// <summary>Draws every ball as a square with the sprite batch (Render runs inside the engine's sprite batch scope).</summary>
public sealed partial class DrawSystem(ISpriteBatch sprites)
{
	private static readonly Color BallColor = new(0xFF, 0x8C, 0x28);

	/// <summary>Draws one ball.</summary>
	[Render, Query, All<Ball>]
	private void Draw(in Transform2D transform) =>
		sprites.DrawRect(BallColor, transform.Position - new Vector2(BallSystem.Radius), new Vector2(BallSystem.Radius * 2));
}
