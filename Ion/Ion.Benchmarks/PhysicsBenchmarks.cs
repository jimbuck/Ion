using Arch.Core;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Physics2D;
using Ion.Extensions.Physics3D;


namespace Ion.Benchmarks;

using EcsTransform2D = Ion.Extensions.Ecs.Transform2D;

/// <summary>
/// Stage 5b: one fixed step (1/60 s) of the 2D physics module (Box2D v3 through <see cref="PhysicsWorld2D"/>, with the ECS
/// synchronization and events) on a pile of 1,000 and 10,000 bodies (half boxes, half circles) resting in a box. Sleeping
/// is off so every body stays in the solver (the steady-state cost of a busy scene, not of a sleeping one).
/// </summary>
[MemoryDiagnoser]
public class Physics2DStepBenchmarks
{
	private World _world = null!;
	private PhysicsWorld2D _physics = null!;

	[Params(1_000, 10_000)]
	public int Bodies { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		_physics = new PhysicsWorld2D(_world, events: null, new Physics2DConfig { EnableSleep = false });
		var columns = (int)MathF.Ceiling(MathF.Sqrt(Bodies));
		var half = columns * 0.3f + 1;
		var height = columns * 0.6f + 10;
		_world.Create(new EcsTransform2D(new Vector2(0, 0.5f)), Collider2D.Box(new Vector2(2 * half + 2, 1)));
		_world.Create(new EcsTransform2D(new Vector2(-half - 0.5f, -height / 2)), Collider2D.Box(new Vector2(1, height)));
		_world.Create(new EcsTransform2D(new Vector2(half + 0.5f, -height / 2)), Collider2D.Box(new Vector2(1, height)));
		for (var i = 0; i < Bodies; i++)
		{
			var position = new Vector2((i % columns - (columns - 1) / 2f) * 0.6f, -1 - i / columns * 0.6f);
			var collider = i % 2 == 0 ? Collider2D.Box(new Vector2(0.5f, 0.5f)) : Collider2D.Circle(0.25f);
			_world.Create(new EcsTransform2D(position), collider, RigidBody2D.Dynamic());
		}

		// Let the pile form (creation happens in the first step).
		for (var i = 0; i < 180; i++) _physics.Step(1f / 60);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_physics.Dispose();
		World.Destroy(_world);
	}

	[Benchmark]
	public void Step() => _physics.Step(1f / 60);
}

/// <summary>
/// Stage 5b: one fixed step of the 3D physics module (BepuPhysics v2 through <see cref="PhysicsWorld3D"/>, single-threaded)
/// on 1,000 bodies (boxes, spheres and capsules) piled on a floor, sleeping off.
/// </summary>
[MemoryDiagnoser]
public class Physics3DStepBenchmarks
{
	private World _world = null!;
	private PhysicsWorld3D _physics = null!;

	[Params(1_000)]
	public int Bodies { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		_physics = new PhysicsWorld3D(_world, events: null, new Physics3DConfig { SleepThreshold = 0 });
		_world.Create(new Transform(new Vector3(0, -0.5f, 0)), Collider3D.Box(new Vector3(40, 1, 40)));
		for (var i = 0; i < Bodies; i++)
		{
			var position = new Vector3(i % 10 * 1.1f - 5, 0.6f + i / 100 * 1.1f, i / 10 % 10 * 1.1f - 5);
			var collider = (i % 3) switch
			{
				0 => Collider3D.Box(new Vector3(0.8f)),
				1 => Collider3D.Sphere(0.45f),
				_ => Collider3D.Capsule(0.3f, 0.4f),
			};
			_world.Create(new Transform(position), collider, RigidBody3D.Dynamic());
		}

		for (var i = 0; i < 180; i++) _physics.Step(1f / 60);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_physics.Dispose();
		World.Destroy(_world);
	}

	[Benchmark]
	public void Step() => _physics.Step(1f / 60);
}

/// <summary>
/// Stage 5b: the cost of the ECS adapter (pushing changed transforms and bodies, pulling the moved bodies back into the
/// transforms and rigid bodies) on 10,000 free-falling bodies that all move every step and never touch: the module's
/// <c>Step</c> against the engine's own step alone (<c>SimulateOnly</c>). The difference is the adapter's cost.
/// </summary>
[MemoryDiagnoser]
public class PhysicsSyncBenchmarks
{
	private const int Bodies = 10_000;

	private World _world2D = null!;
	private PhysicsWorld2D _physics2D = null!;
	private World _world3D = null!;
	private PhysicsWorld3D _physics3D = null!;

	[GlobalSetup]
	public void Setup()
	{
		// Far apart and falling slowly: no contacts, every body awake and moving. Gravity is tiny so they stay in range.
		_world2D = World.Create();
		_physics2D = new PhysicsWorld2D(_world2D, events: null, new Physics2DConfig { GravityY = 0.001f, EnableSleep = false });
		_world3D = World.Create();
		_physics3D = new PhysicsWorld3D(_world3D, events: null, new Physics3DConfig { GravityY = -0.001f, SleepThreshold = 0 });
		for (var i = 0; i < Bodies; i++)
		{
			_world2D.Create(new EcsTransform2D(new Vector2(i % 100 * 2f, i / 100 * 2f)), Collider2D.Circle(0.25f), RigidBody2D.Dynamic(new Vector2(0, 0.01f)));
			_world3D.Create(new Transform(new Vector3(i % 100 * 2f, i / 100 * 2f, 0)), Collider3D.Sphere(0.25f), RigidBody3D.Dynamic(velocity: new Vector3(0, -0.01f, 0)));
		}

		for (var i = 0; i < 10; i++)
		{
			_physics2D.Step(1f / 60);
			_physics3D.Step(1f / 60);
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_physics2D.Dispose();
		_physics3D.Dispose();
		World.Destroy(_world2D);
		World.Destroy(_world3D);
	}

	[Benchmark(Baseline = true)]
	public void Box2D_SimulateOnly_10k() => _physics2D.SimulateOnly(1f / 60);

	[Benchmark]
	public void Box2D_StepWithSync_10k() => _physics2D.Step(1f / 60);

	[Benchmark]
	public void Bepu_SimulateOnly_10k() => _physics3D.SimulateOnly(1f / 60);

	[Benchmark]
	public void Bepu_StepWithSync_10k() => _physics3D.Step(1f / 60);
}
