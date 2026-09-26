using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Physics3D;

/// <summary>Registration of the 3D physics module.</summary>
public static class Physics3DBuilderExtensions
{
	/// <summary>
	/// Registers the 3D physics module: <see cref="Physics3DWorlds"/>, and <see cref="PhysicsWorld3D"/> (also as
	/// <see cref="IPhysicsWorld3D"/>) resolved per scope like the ECS <see cref="World"/> it simulates, the systems
	/// (transients), and <see cref="Physics3DConfig"/> bound from <c>Ion:Physics3D</c> in <paramref name="config"/> and then
	/// <paramref name="configure"/>. Needs the ECS module (<c>AddEcs</c>). Add the systems with
	/// <see cref="UsePhysics3D(IIonApplication)"/> (and <see cref="UsePhysics3D(ISceneBuilder)"/> in scenes).
	/// </summary>
	public static IServiceCollection AddPhysics3D(this IServiceCollection services, IConfiguration? config = null, Action<Physics3DConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		Physics3DComponents.Register();

		services.AddOptions<Physics3DConfig>();
		if (config is not null) services.Configure<Physics3DConfig>(config.GetSection(Physics3DConfig.Section));
		if (configure is not null) services.Configure(configure);

		services.TryAddSingleton(static sp => new Physics3DWorlds(sp, sp.GetRequiredService<IOptions<Physics3DConfig>>().Value));
		services.TryAddScoped(static sp => new Physics3DScope(sp.GetRequiredService<Physics3DWorlds>(), sp));
		services.TryAddTransient(static sp => sp.GetRequiredService<Physics3DWorlds>().WorldFor(sp));
		services.TryAddTransient<IPhysicsWorld3D>(static sp => sp.GetRequiredService<PhysicsWorld3D>());

		services.TryAddTransient(static sp => new Physics3DSystem(sp.GetRequiredService<PhysicsWorld3D>()));
		services.TryAddTransient(static sp => new Physics3DDebugDrawSystem(sp.GetRequiredService<PhysicsWorld3D>(), sp.GetService<IRenderer3D>()));
		return services;
	}

	/// <summary>
	/// Adds the 3D physics systems to the root schedule: the physics step in FixedUpdate at <see cref="StageOrder.Physics"/>
	/// and the debug drawing in Render at <see cref="StageOrder.PhysicsDebugDraw"/> (while <see cref="IPhysicsWorld3D.DebugDraw"/> is on).
	/// </summary>
	public static IIonApplication UsePhysics3D(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app
			.UseSystem<Physics3DSystem>()
			.UseSystem<Physics3DDebugDrawSystem>();
	}

	/// <summary>Adds the 3D physics systems to a scene's schedule (for the scene's own world).</summary>
	public static ISceneBuilder UsePhysics3D(this ISceneBuilder scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		return scene
			.UseSystem<Physics3DSystem>()
			.UseSystem<Physics3DDebugDrawSystem>();
	}
}

/// <summary>Registers the 3D physics components with Arch (NativeAOT needs every stored component registered up front).</summary>
public static class Physics3DComponents
{
	private static volatile bool _registered;

	/// <summary>Registers the ECS built-in components (the transforms) and <see cref="RigidBody3D"/>, <see cref="Collider3D"/> and <see cref="Joint3D"/> (safe to call more than once).</summary>
	public static void Register()
	{
		if (_registered) return;
		EcsComponents.RegisterBuiltIns();
		EcsComponents.Register<RigidBody3D>();
		EcsComponents.Register<Collider3D>();
		EcsComponents.Register<Joint3D>();
		_registered = true;
	}
}

/// <summary>The 3D physics worlds of an application: the root one and one per service scope (each scene).</summary>
public sealed class Physics3DWorlds : IDisposable
{
	private readonly IServiceProvider _root;
	private readonly Physics3DConfig _config;
	private PhysicsWorld3D? _rootWorld;
	private bool _disposed;

	/// <summary>Creates the registry for the application whose root provider is <paramref name="root"/>.</summary>
	public Physics3DWorlds(IServiceProvider root, Physics3DConfig config)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(config);
		_root = root;
		_config = config;
	}

	/// <summary>The settings every world is created with.</summary>
	public Physics3DConfig Config => _config;

	/// <summary>The physics world of <paramref name="services"/>' scope (the root world for the root provider).</summary>
	public PhysicsWorld3D WorldFor(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (ReferenceEquals(services, _root)) return _rootWorld ??= Create(services);
		return services.GetRequiredService<Physics3DScope>().World;
	}

	internal PhysicsWorld3D Create(IServiceProvider services)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return new PhysicsWorld3D(services.GetRequiredService<World>(), services.GetService<IEvents>(), _config);
	}

	/// <summary>Disposes the root world.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_rootWorld?.Dispose();
		_disposed = true;
	}
}

internal sealed class Physics3DScope(Physics3DWorlds worlds, IServiceProvider services) : IDisposable
{
	private PhysicsWorld3D? _world;

	public PhysicsWorld3D World => _world ??= worlds.Create(services);

	public void Dispose() => _world?.Dispose();
}

/// <summary>The 3D physics step: <see cref="PhysicsWorld3D.Step"/> with the fixed delta at <see cref="StageOrder.Physics"/> in every fixed step.</summary>
public sealed class Physics3DSystem(PhysicsWorld3D physics)
{
	/// <summary>The physics world.</summary>
	public PhysicsWorld3D World => physics;

	/// <summary>Synchronizes the entities, steps the simulation and emits the events.</summary>
	[FixedUpdate(Order = StageOrder.Physics)]
	public void Step(GameTime dt) => physics.Step(dt.Delta);
}

/// <summary>
/// The 3D physics debug drawing: at <see cref="StageOrder.PhysicsDebugDraw"/> in Render, every collider as a translucent
/// unlit mesh submitted to the 3D renderer while <see cref="IPhysicsWorld3D.DebugDraw"/> is on
/// (<see cref="Physics3DConfig.DebugDraw"/>, <c>--Ion:Physics3D:DebugDraw=true</c>). The meshes and materials are created on
/// the first drawn frame and released with the renderer.
/// </summary>
public sealed class Physics3DDebugDrawSystem(PhysicsWorld3D physics, IRenderer3D? renderer)
{
	private RendererSink? _sink;

	/// <summary>Submits the colliders when the debug drawing is on.</summary>
	[Render(Order = StageOrder.PhysicsDebugDraw)]
	public void Draw(GameTime dt)
	{
		if (!physics.DebugDraw || renderer is null) return;
		_sink ??= new RendererSink(renderer, physics);
		physics.Draw(_sink);
	}

	private sealed class RendererSink(IRenderer3D renderer, PhysicsWorld3D physics) : IDebugShapeSink
	{
		private readonly MeshHandle _box = renderer.CreateMesh(MeshPrimitives.Cube(1f));
		private readonly MeshHandle _sphere = renderer.CreateMesh(MeshPrimitives.Sphere(0.5f, 16, 8));
		private readonly MeshHandle _cylinder = renderer.CreateMesh(MeshPrimitives.Cylinder(0.5f, 1f, 16));
		private readonly Dictionary<int, MeshHandle> _hulls = [];
		private readonly MaterialHandle[] _materials =
		[
			Material(renderer, new Color(0x40, 0x60, 0xff, 0x60)),
			Material(renderer, new Color(0x40, 0xd0, 0x40, 0x60)),
			Material(renderer, new Color(0xff, 0x40, 0x40, 0x60)),
			Material(renderer, new Color(0x90, 0x90, 0x90, 0x60)),
			Material(renderer, new Color(0xff, 0xff, 0x00, 0x40)),
		];

		public void Shape(DebugShape3D shape, in Matrix4x4 world, DebugColor3D color, ConvexHullId hull)
		{
			var mesh = shape switch
			{
				DebugShape3D.Sphere => _sphere,
				DebugShape3D.Cylinder => _cylinder,
				DebugShape3D.Hull => HullMesh(hull),
				_ => _box,
			};
			renderer.Submit(new MeshRenderer(mesh, _materials[(int)color]) { CastShadows = false, ReceiveShadows = false }, world);
		}

		private MeshHandle HullMesh(ConvexHullId hull)
		{
			if (_hulls.TryGetValue(hull.Value, out var mesh)) return mesh;
			var (positions, indices) = physics.GetHullMesh(hull);
			var data = new MeshData($"physics-hull-{hull.Value}", positions, indices);
			data.ComputeNormals();
			mesh = renderer.CreateMesh(data);
			_hulls[hull.Value] = mesh;
			return mesh;
		}

		private static MaterialHandle Material(IRenderer3D renderer, Color color) =>
			renderer.CreateMaterial(new UnlitMaterial(color) { AlphaMode = AlphaMode.Blend, DoubleSided = true });
	}
}
