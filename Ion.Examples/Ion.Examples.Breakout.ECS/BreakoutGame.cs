using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Arch.Core;

using Ion.Extensions.Graphics;
using Ion.Examples.Breakout.ECS.Common;
using Ion.Examples.Breakout.ECS.Physics;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.ECS;

/// <summary>
/// Game-wide settings bound from configuration.
/// </summary>
/// <param name="Seed">The random seed (<c>Ion:Seed</c>, <see cref="BreakoutGame.DefaultSeed"/> by default).</param>
public sealed record BreakoutSettings(int Seed)
{
	/// <summary>
	/// Creates a random number generator for one consumer. Each consumer passes its own <paramref name="stream"/> so the
	/// sequences do not depend on the order systems run or draw numbers in.
	/// </summary>
	public Random CreateRandom(int stream) => new(HashCode.Combine(Seed, stream));
}

/// <summary>
/// The Breakout ECS game setup, shared by <c>Program.cs</c> and the headless tests: <see cref="Configure"/> registers the
/// services and <see cref="Use"/> wires the pipeline.
/// </summary>
public static class BreakoutGame
{
	/// <summary>The configuration key of the random seed.</summary>
	public const string SeedKey = "Ion:Seed";

	/// <summary>The seed used when <see cref="SeedKey"/> is not configured.</summary>
	public const int DefaultSeed = 6014;

	private static readonly Lock RegistrationLock = new();
	private static bool _componentsRegistered;

	/// <summary>
	/// Registers the game's services with <paramref name="builder"/>, including <c>AddIon</c>. When the configuration asks
	/// for headless mode (<c>Ion:Headless=true</c>) the <see cref="HeadlessAutopilotSystem"/> is registered too.
	/// </summary>
	public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		RegisterComponents();

		var seed = int.TryParse(builder.Configuration[SeedKey], out var configured) ? configured : DefaultSeed;

		builder.Services.AddIon(builder.Configuration, graphics =>
		{
			graphics.ClearColor = new Color(0x333);
		});

		builder.Services.AddSingleton(new BreakoutSettings(seed))
						.AddSingleton<MouseCaptureSystem>()
						.AddSingleton<SpriteRendererSystem>()
						.AddScoped(services => World.Create())
						.AddScoped<ScoreSystem>()
						.AddScoped<SoundEffectsSystem>()
						.AddScoped<PaddleSystem>()
						.AddScoped<BallSystem>()
						.AddScoped<BlockSystem>()
						.AddScoped<PhysicsManager>()
						.AddScoped<PhysicsSystem>()
						.AddScoped<LevelSystem>();

		if (builder.Configuration.IsHeadless()) builder.Services.AddScoped<HeadlessAutopilotSystem>();

		return builder;
	}

	/// <summary>
	/// Adds the engine's systems (<c>UseIon</c>) and the game's systems to <paramref name="app"/>, plus the
	/// <see cref="HeadlessAutopilotSystem"/> when running headless.
	/// </summary>
	public static IIonApplication Use(IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);

		app.UseIon()
			.UseSystem<MouseCaptureSystem>()
			.UseSystem<PhysicsSystem>()
			.UseSystem<LevelSystem>()
			.UseSystem<SoundEffectsSystem>()
			.UseSystem<PaddleSystem>()
			.UseSystem<BallSystem>()
			.UseSystem<BlockSystem>()
			.UseSystem<SpriteRendererSystem>()
			.UseSystem<ScoreSystem>();

		if (app.Configuration.IsHeadless()) app.UseSystem<HeadlessAutopilotSystem>();

		return app;
	}

	/// <summary>
	/// Registers every component array type with Arch. Arch creates component arrays with <c>Array.CreateInstance</c>
	/// unless the array type is registered up front, which NativeAOT cannot do for types it has not seen, so this lets the
	/// sample run under PublishAot. Safe to call more than once.
	/// </summary>
	public static void RegisterComponents()
	{
		lock (RegistrationLock)
		{
			if (_componentsRegistered) return;

			ArrayRegistry.Add<Block>();
			ArrayRegistry.Add<Paddle>();
			ArrayRegistry.Add<Ball>();
			ArrayRegistry.Add<Transform2D>();
			ArrayRegistry.Add<Sprite>();
			ArrayRegistry.Add<DynamicRigidBody>();
			ArrayRegistry.Add<KinematicRigidBody>();
			ArrayRegistry.Add<StaticBody>();

			_componentsRegistered = true;
		}
	}
}
