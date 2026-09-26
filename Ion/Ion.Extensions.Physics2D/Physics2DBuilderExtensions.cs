using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Physics2D;

/// <summary>Registration of the 2D physics module.</summary>
public static class Physics2DBuilderExtensions
{
	/// <summary>
	/// Registers the 2D physics module: <see cref="Physics2DWorlds"/>, and <see cref="PhysicsWorld2D"/> (also as
	/// <see cref="IPhysicsWorld2D"/>) resolved per scope like the ECS <see cref="World"/> it simulates (the root provider
	/// gets the root world, each scene scope its own, disposed with the scene), the systems (transients, so the root
	/// schedule and each scene get instances bound to their own world), and <see cref="Physics2DConfig"/> bound from
	/// <c>Ion:Physics2D</c> in <paramref name="config"/> and then <paramref name="configure"/>. Needs the ECS module
	/// (<c>AddEcs</c>). Add the systems with <see cref="UsePhysics2D(IIonApplication)"/> (and
	/// <see cref="UsePhysics2D(ISceneBuilder)"/> in scenes that simulate physics).
	/// </summary>
	public static IServiceCollection AddPhysics2D(this IServiceCollection services, IConfiguration? config = null, Action<Physics2DConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		Physics2DComponents.Register();

		services.AddOptions<Physics2DConfig>();
		if (config is not null) services.Configure<Physics2DConfig>(config.GetSection(Physics2DConfig.Section));
		if (configure is not null) services.Configure(configure);

		services.TryAddSingleton(static sp => new Physics2DWorlds(sp, sp.GetRequiredService<IOptions<Physics2DConfig>>().Value));
		services.TryAddScoped(static sp => new Physics2DScope(sp.GetRequiredService<Physics2DWorlds>(), sp));
		services.TryAddTransient(static sp => sp.GetRequiredService<Physics2DWorlds>().WorldFor(sp));
		services.TryAddTransient<IPhysicsWorld2D>(static sp => sp.GetRequiredService<PhysicsWorld2D>());

		services.TryAddTransient(static sp => new Physics2DSystem(sp.GetRequiredService<PhysicsWorld2D>()));
		services.TryAddTransient(static sp => new Physics2DDebugDrawSystem(sp.GetRequiredService<PhysicsWorld2D>(), sp.GetService<ISpriteBatch>()));
		return services;
	}

	/// <summary>
	/// Adds the 2D physics systems to the root schedule (for the root world): the physics step in FixedUpdate at
	/// <see cref="StageOrder.Physics"/> and the debug drawing in Render at <see cref="StageOrder.PhysicsDebugDraw"/>
	/// (drawn only while <see cref="IPhysicsWorld2D.DebugDraw"/> is on).
	/// </summary>
	public static IIonApplication UsePhysics2D(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app
			.UseSystem<Physics2DSystem>()
			.UseSystem<Physics2DDebugDrawSystem>();
	}

	/// <summary>Adds the 2D physics systems to a scene's schedule (for the scene's own world); see <see cref="UsePhysics2D(IIonApplication)"/>.</summary>
	public static ISceneBuilder UsePhysics2D(this ISceneBuilder scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		return scene
			.UseSystem<Physics2DSystem>()
			.UseSystem<Physics2DDebugDrawSystem>();
	}
}

/// <summary>Registers the physics components with Arch (NativeAOT needs every stored component registered up front).</summary>
public static class Physics2DComponents
{
	private static volatile bool _registered;

	/// <summary>Registers the ECS built-in components (the transforms) and <see cref="RigidBody2D"/>, <see cref="Collider2D"/> and <see cref="Joint2D"/> (safe to call more than once).</summary>
	public static void Register()
	{
		if (_registered) return;
		EcsComponents.RegisterBuiltIns();
		EcsComponents.Register<RigidBody2D>();
		EcsComponents.Register<Collider2D>();
		EcsComponents.Register<Joint2D>();
		_registered = true;
	}
}

/// <summary>
/// The 2D physics worlds of an application: the root one (for the root provider) and one per service scope (each scene),
/// each created on first use for the ECS world of the same scope and disposed with it.
/// </summary>
public sealed class Physics2DWorlds : IDisposable
{
	private readonly IServiceProvider _root;
	private readonly Physics2DConfig _config;
	private PhysicsWorld2D? _rootWorld;
	private bool _disposed;

	/// <summary>Creates the registry for the application whose root provider is <paramref name="root"/>.</summary>
	public Physics2DWorlds(IServiceProvider root, Physics2DConfig config)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(config);
		_root = root;
		_config = config;
	}

	/// <summary>The settings every world is created with.</summary>
	public Physics2DConfig Config => _config;

	/// <summary>The physics world of <paramref name="services"/>' scope (the root world for the root provider).</summary>
	public PhysicsWorld2D WorldFor(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (ReferenceEquals(services, _root)) return _rootWorld ??= Create(services);
		return services.GetRequiredService<Physics2DScope>().World;
	}

	internal PhysicsWorld2D Create(IServiceProvider services)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return new PhysicsWorld2D(services.GetRequiredService<World>(), services.GetService<IEvents>(), _config);
	}

	/// <summary>Disposes the root world.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_rootWorld?.Dispose();
		_disposed = true;
	}
}

/// <summary>The physics world of one service scope (a scene), disposed with it.</summary>
internal sealed class Physics2DScope(Physics2DWorlds worlds, IServiceProvider services) : IDisposable
{
	private PhysicsWorld2D? _world;

	public PhysicsWorld2D World => _world ??= worlds.Create(services);

	public void Dispose() => _world?.Dispose();
}

/// <summary>
/// The 2D physics step: at <see cref="StageOrder.Physics"/> in every fixed step, <see cref="PhysicsWorld2D.Step"/> with
/// the fixed delta (never the frame's wall-clock time), so a replay with the same inputs gives the same results.
/// </summary>
public sealed class Physics2DSystem(PhysicsWorld2D physics)
{
	/// <summary>The physics world.</summary>
	public PhysicsWorld2D World => physics;

	/// <summary>Synchronizes the entities, steps the world and emits the events.</summary>
	[FixedUpdate(Order = StageOrder.Physics)]
	public void Step(GameTime dt) => physics.Step(dt.Delta);
}

/// <summary>
/// The 2D physics debug drawing: at <see cref="StageOrder.PhysicsDebugDraw"/> in Render (inside the sprite batch scope,
/// over the frame's sprites), every collider and joint as lines while <see cref="IPhysicsWorld2D.DebugDraw"/> is on
/// (<see cref="Physics2DConfig.DebugDraw"/>, <c>--Ion:Physics2D:DebugDraw=true</c>).
/// </summary>
public sealed class Physics2DDebugDrawSystem(PhysicsWorld2D physics, ISpriteBatch? spriteBatch)
{
	private readonly SpriteBatchLines _lines = new(spriteBatch);

	/// <summary>The line thickness in pixels.</summary>
	public float Thickness
	{
		get => _lines.Thickness;
		set => _lines.Thickness = value;
	}

	/// <summary>Draws the colliders and joints when the debug drawing is on.</summary>
	[Render(Order = StageOrder.PhysicsDebugDraw)]
	public void Draw(GameTime dt)
	{
		if (!physics.DebugDraw || spriteBatch is null) return;
		physics.Draw(_lines);
	}

	private sealed class SpriteBatchLines(ISpriteBatch? spriteBatch) : ILineSink
	{
		public float Thickness { get; set; } = 1f;

		public void Line(Vector2 a, Vector2 b, DebugColor color) => spriteBatch?.DrawLine(ToColor(color), a, b, Thickness);

		private static Color ToColor(DebugColor color) => color switch
		{
			DebugColor.Static => Color.Blue,
			DebugColor.Kinematic => Color.Green,
			DebugColor.Dynamic => Color.Red,
			DebugColor.Sleeping => Color.Gray,
			DebugColor.Sensor => Color.Yellow,
			_ => Color.White,
		};
	}
}
