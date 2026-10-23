using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Examples.Breakout.ECS.Common;
using Ion.Extensions.Physics2D;

namespace Ion.Examples.Breakout.ECS;

/// <summary>
/// Game-wide settings bound from configuration.
/// </summary>
/// <param name="Seed">The random seed (<c>Ion:Seed</c>, <see cref="BreakoutGame.DefaultSeed"/> by default).</param>
public sealed record BreakoutSettings(int Seed)
{
	/// <summary>
	/// Creates a random number generator for one consumer. Each consumer passes its own <paramref name="stream"/> so the
	/// sequences do not depend on the order systems run or draw numbers in. The seed is combined without <see cref="HashCode"/>
	/// (randomized per process), so a seed gives the same game in every run (golden-image tests rely on it).
	/// </summary>
	public Random CreateRandom(int stream) => new(unchecked(Seed * 7919 + stream * 104729));
}

/// <summary>
/// The Breakout ECS game as a module: <see cref="AddBreakout"/> registers it and <see cref="UseBreakout"/> adds its systems.
/// <c>Program.cs</c> is the two calls; the mobile heads (<c>Ion.Examples.Breakout.ECS.Android</c> and <c>.iOS</c>) compile
/// this file, not <c>Program.cs</c>, and make the same two calls from their own entry point (<see cref="BreakoutMobile"/>).
/// </summary>
public static class BreakoutGame
{
	/// <summary>The configuration key of the random seed.</summary>
	public const string SeedKey = "Ion:Seed";

	/// <summary>The seed used when <see cref="SeedKey"/> is not configured.</summary>
	public const int DefaultSeed = 6014;

	/// <summary>
	/// Registers the game: the engine, the ECS module with the sprite extraction, the 2D physics module and the game's
	/// systems. When the configuration asks for headless mode (<c>Ion:Headless=true</c>) the
	/// <see cref="HeadlessAutopilotSystem"/> is registered too.
	/// </summary>
	public static IonApplicationBuilder AddBreakout(this IonApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		RegisterComponents();

		var seed = int.TryParse(builder.Configuration[SeedKey], out var configured) ? configured : DefaultSeed;
		var debugDraw = builder.Configuration[$"{Physics2DConfig.Section}:DebugDraw"] is null;

		// The engine, and the ECS module: the root World (resolved by every system below), Commands played back at the end
		// of every stage, transform propagation, FrameStats.Entities, and the sprite extraction into the sprite batch.
		builder.AddIon(graphics => graphics.ClearColor = new Color(0x333))
			.AddEcsRendering()
			// The 2D physics module (Box2D v3): no gravity, the world in pixels (64 px to the meter), and the debug drawing
			// of every collider on unless Ion:Physics2D:DebugDraw says otherwise.
			.AddPhysics2D(physics =>
			{
				physics.GravityY = 0;
				physics.UnitsPerMeter = BreakoutPhysics.PixelsPerMeter;
				physics.DebugDraw |= debugDraw;
			})
			// The game runs in the root schedule (no scenes), so its systems are singletons: a scoped system there is error ION006.
			.AddSystem<MouseCaptureSystem>()
			.AddSystem<ScoreSystem>()
			.AddSystem<SoundEffectsSystem>()
			.AddSystem<PaddleSystem>()
			.AddSystem<BallSystem>()
			.AddSystem<BlockSystem>()
			.AddSystem<CollisionEventSystem>()
			.AddSystem<LevelSystem>();
		builder.Services.AddSingleton(new BreakoutSettings(seed));

		if (builder.Configuration.IsHeadless()) builder.AddSystem<HeadlessAutopilotSystem>();

		return builder;
	}

	/// <summary>
	/// Adds the game's systems and the ones of the modules it uses (the engine's, the ECS module's with the sprite
	/// extraction, the 2D physics'), plus the <see cref="HeadlessAutopilotSystem"/> when running headless.
	/// </summary>
	/// <remarks>
	/// Registration order only breaks ties between steps of equal order: the engine's steps use the reserved order bands
	/// (see <see cref="StageOrder"/>), so a game system can be added before the engine's and its steps still run after the
	/// window, input and sprite batch setup. <see cref="MouseCaptureSystem"/> is added first to show it.
	/// </remarks>
	public static IIonApplication UseBreakout(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);

		app.UseSystem<MouseCaptureSystem>()
			.UseEcsRendering()
			.UsePhysics2D()
			.UseSystem<CollisionEventSystem>()
			.UseSystem<LevelSystem>()
			.UseSystem<SoundEffectsSystem>()
			.UseSystem<PaddleSystem>()
			.UseSystem<BallSystem>()
			.UseSystem<BlockSystem>()
			.UseSystem<ScoreSystem>();

		if (app.Configuration.IsHeadless()) app.UseSystem<HeadlessAutopilotSystem>();

		return app;
	}

	/// <summary>
	/// Registers the game's own component types with Arch. Arch creates component arrays with <c>Array.CreateInstance</c>
	/// unless the array type is registered up front, which NativeAOT cannot do for types it has not seen. The ECS module
	/// registers its built-in components (Transform2D, Sprite, ...) and the generator those of every [Query] method; this
	/// covers the components the game only creates. Safe to call more than once.
	/// </summary>
	public static void RegisterComponents()
	{
		EcsComponents.Register<Block>();
		EcsComponents.Register<Paddle>();
		EcsComponents.Register<Ball>();
		EcsComponents.Register<Wall>();
	}
}
