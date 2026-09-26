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
	/// sequences do not depend on the order systems run or draw numbers in. The seed is combined without <see cref="HashCode"/>
	/// (randomized per process), so a seed gives the same game in every run (golden-image tests rely on it).
	/// </summary>
	public Random CreateRandom(int stream) => new(unchecked(Seed * 7919 + stream * 104729));
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
						.AddSingleton(services => World.Create())
						.AddSingleton<ScoreSystem>()
						.AddSingleton<SoundEffectsSystem>()
						.AddSingleton<PaddleSystem>()
						.AddSingleton<BallSystem>()
						.AddSingleton<BlockSystem>()
						.AddSingleton<PhysicsManager>()
						.AddSingleton<PhysicsSystem>()
						.AddSingleton<LevelSystem>()
						// The ECS hook of the frame stats: FrameStats.Entities (frame log, overlay, dotnet-counters).
						.AddSingleton<IFrameStatsSource, EntityStatsSource>();

		// The game runs in the root schedule (no scenes), so its systems are singletons: a scoped system there is error ION006.
		if (builder.Configuration.IsHeadless()) builder.Services.AddSingleton<HeadlessAutopilotSystem>();

		return builder;
	}

	/// <summary>
	/// Adds the engine's systems (<c>UseIon</c>) and the game's systems to <paramref name="app"/>, plus the
	/// <see cref="HeadlessAutopilotSystem"/> when running headless.
	/// </summary>
	/// <remarks>
	/// Registration order only breaks ties between steps of equal order: the engine's steps use the reserved order bands
	/// (see <see cref="StageOrder"/>), so a game system can be added before <c>UseIon</c> and its steps still run after
	/// the window, input and sprite batch setup. <see cref="MouseCaptureSystem"/> is added first to show it.
	/// </remarks>
	public static IIonApplication Use(IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);

		app.UseSystem<MouseCaptureSystem>()
			.UseIon()
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

/// <summary>Writes the number of live Arch entities into every frame's <see cref="FrameStats.Entities"/>.</summary>
internal sealed class EntityStatsSource(World world) : IFrameStatsSource
{
	public void Collect(ref FrameStats stats) => stats.Entities = world.Size;
}
