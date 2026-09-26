using System.Runtime.InteropServices;

using Arch.Core;
using Arch.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Testing;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics3D.Tests;

internal static class Hosts
{
	/// <summary>A headless host with the ECS and 3D physics modules in the root schedule (one fixed step per frame).</summary>
	public static IonTestHost Physics(Action<Physics3DConfig>? configure = null) =>
		new IonTestHost()
			.Configure(services => services.AddEcs().AddPhysics3D(configure: configure))
			.ConfigureApp(app => app.UseEcs().UsePhysics3D());

	public static Entity Floor(this World world, float y = 0f) =>
		world.Create(new Transform(new Vector3(0, y - 0.5f, 0)), Collider3D.Box(new Vector3(40, 1, 40)));

	public static Entity Ball(this World world, Vector3 position, float radius = 0.5f, Vector3 velocity = default) =>
		world.Create(new Transform(position), Collider3D.Sphere(radius), RigidBody3D.Dynamic(velocity: velocity));
}

[Trait(CATEGORY, INTEGRATION)]
public class Physics3DTests
{
	[Fact]
	public void ADynamicBodyFallsAndRestsOnAStaticFloor()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Floor();
		var ball = world.Ball(new Vector3(0, 5, 0));

		host.Step(10);
		Assert.True(ball.Get<Transform>().Position.Y < 4.95f);
		Assert.True(ball.Get<RigidBody3D>().LinearVelocity.Y < -1f);

		host.Step(240);
		Assert.InRange(ball.Get<Transform>().Position.Y, 0.45f, 0.55f);
		Assert.Equal(2, host.Get<IPhysicsWorld3D>().BodyCount);
	}

	[Fact]
	public void VelocityChangesAndTeleportsAreApplied()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var ball = world.Ball(Vector3.Zero);
		host.Step();

		ball.Get<RigidBody3D>().LinearVelocity = new Vector3(3, 0, 0);
		host.Step(60);
		Assert.InRange(ball.Get<Transform>().Position.X, 2.9f, 3.1f);

		ball.Get<Transform>().Position = new Vector3(0, 10, 0);
		host.Step();
		Assert.InRange(ball.Get<Transform>().Position.Y, 9.99f, 10.01f);
		Assert.InRange(ball.Get<Transform>().Position.X, 0.04f, 0.06f);
	}

	[Fact]
	public void GravityScaleAndDampingArePerBody()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var floating = world.Create(new Transform(new Vector3(0, 5, 0)), Collider3D.Sphere(0.5f), RigidBody3D.Dynamic() with { GravityScale = 0 });
		var damped = world.Create(new Transform(new Vector3(5, 5, 0)), Collider3D.Sphere(0.5f), RigidBody3D.Dynamic() with { LinearDamping = 5 });
		var free = world.Ball(new Vector3(-5, 5, 0));

		host.Step(30);
		Assert.Equal(5f, floating.Get<Transform>().Position.Y, 3);
		Assert.True(damped.Get<Transform>().Position.Y > free.Get<Transform>().Position.Y + 0.3f);
	}

	[Fact]
	public void AKinematicBodyFollowsItsTransformAndPushesDynamicBodies()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Floor();
		var pusher = world.Create(new Transform(new Vector3(-3, 1, 0)), Collider3D.Box(new Vector3(1, 2, 4)), RigidBody3D.Kinematic());
		var box = world.Create(new Transform(new Vector3(0, 0.5f, 0)), Collider3D.Box(Vector3.One), RigidBody3D.Dynamic());
		host.Step(30);

		for (var i = 0; i < 40; i++)
		{
			pusher.Get<Transform>().Position += new Vector3(0.1f, 0, 0);
			host.Step();
		}

		Assert.InRange(pusher.Get<Transform>().Position.X, 0.95f, 1.05f);
		Assert.True(box.Get<Transform>().Position.X > 1.4f, $"the box was pushed to {box.Get<Transform>().Position.X}");

		var stopped = pusher.Get<Transform>().Position.X;
		host.Step(30);
		Assert.Equal(stopped, pusher.Get<Transform>().Position.X, 3);
	}

	[Fact]
	public void CollisionEventsReportBeginAndEndWithANormalFromAToB()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var collisions = host.Collect<Collision3D>();
		var floor = world.Floor();
		var ball = world.Ball(new Vector3(0, 2, 0));

		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase3D.Begin), 120));
		var begin = collisions.First(c => c.Phase == ContactPhase3D.Begin);
		Assert.True(begin.Involves(floor) && begin.Involves(ball));
		// The normal points from A to B: towards the ball when A is the floor.
		var towardsBall = begin.A == floor ? begin.Normal.Y : -begin.Normal.Y;
		Assert.InRange(towardsBall, 0.99f, 1.01f);
		Assert.InRange(begin.Point.Y, -0.05f, 0.05f);

		world.Destroy(ball);
		host.Step(2);
		Assert.Contains(collisions, c => c.Phase == ContactPhase3D.End && c.Involves(ball));
		Assert.Equal(1, host.Get<IPhysicsWorld3D>().BodyCount);
	}

	[Fact]
	public void SensorsReportTriggersWithoutColliding()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var triggers = host.Collect<Trigger3D>();
		var collisions = host.Collect<Collision3D>();
		var sensor = world.Create(new Transform(Vector3.Zero), Collider3D.Box(new Vector3(2, 2, 2)) with { IsSensor = true });
		var ball = world.Ball(new Vector3(-5, 0, 0), velocity: new Vector3(10, 0, 0));

		host.Step(60);

		Assert.Contains(triggers, t => t.Phase == ContactPhase3D.Begin && t.Sensor == sensor && t.Visitor == ball);
		Assert.Contains(triggers, t => t.Phase == ContactPhase3D.End && t.Sensor == sensor && t.Visitor == ball);
		Assert.Empty(collisions);
		Assert.True(ball.Get<Transform>().Position.X > 4f);
	}

	[Fact]
	public void LayersAndMasksFilterCollisionsAndQueries()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Create(new Transform(new Vector3(0, -0.5f, 0)), Collider3D.Box(new Vector3(10, 1, 10)) with { Layer = 2 });
		var ghost = world.Create(new Transform(new Vector3(0, 2, 0)), Collider3D.Sphere(0.5f) with { Mask = ~2u }, RigidBody3D.Dynamic());

		host.Step(60);
		Assert.True(ghost.Get<Transform>().Position.Y < -2, "the ball fell through the floor it does not collide with");

		var physics = host.Get<IPhysicsWorld3D>();
		Assert.True(physics.RayCast(new Vector3(0, 10, 0), -Vector3.UnitY, 20, out _, mask: 2));
		Assert.False(physics.RayCast(new Vector3(0, 10, 0), -Vector3.UnitY, 9.4f, out _, mask: 1));
	}

	[Fact]
	public void QueriesFindTheRightEntities()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var box = world.Create(new Transform(new Vector3(-5, 0, 0)), Collider3D.Box(new Vector3(2, 2, 2)));
		var sphere = world.Create(new Transform(new Vector3(5, 0, 0)), Collider3D.Sphere(1));
		var capsule = world.Create(new Transform(new Vector3(0, 5, 0)), Collider3D.Capsule(0.5f, 2));
		host.Step();

		Assert.True(physics.RayCast(new Vector3(-10, 0, 0), Vector3.UnitX, 20, out var hit));
		Assert.Equal(box, hit.Entity);
		Assert.Equal(-6f, hit.Point.X, 2);
		Assert.Equal(-1f, hit.Normal.X, 2);
		Assert.Equal(4f, hit.Distance, 2);

		Assert.True(physics.RayCast(new Vector3(0, 10, 0), -Vector3.UnitY, 20, out hit));
		Assert.Equal(capsule, hit.Entity);
		Assert.Equal(6.5f, hit.Point.Y, 2);

		Span<Entity> found = new Entity[8];
		Assert.Equal(2, physics.OverlapBox(new Vector3(-7, -1, -1), new Vector3(7, 1, 1), found));
		Assert.Equal(1, physics.OverlapSphere(new Vector3(3.5f, 0, 0), 0.6f, found));
		Assert.Equal(sphere, found[0]);
		Assert.Equal(0, physics.OverlapSphere(new Vector3(0, 0, 0), 1f, found));
	}

	[Fact]
	public void ConvexHullsCollide()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		world.Floor();
		var mesh = MeshPrimitives.Cube(1f);
		var hull = physics.CreateConvexHull(mesh.Positions);
		var cube = world.Create(new Transform(new Vector3(0, 3, 0)), Collider3D.ConvexHull(hull), RigidBody3D.Dynamic());

		host.Step(180);
		Assert.InRange(cube.Get<Transform>().Position.Y, 0.45f, 0.55f);
	}

	[Fact]
	public void JointsConstrainTheBodies()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var anchor = world.Create(new Transform(new Vector3(0, 10, 0)), Collider3D.Sphere(0.1f) with { Mask = 0 }, RigidBody3D.Kinematic());
		var bob = world.Create(new Transform(new Vector3(2, 10, 0)), Collider3D.Sphere(0.2f) with { Mask = 0 }, RigidBody3D.Dynamic());
		world.Create(Joint3D.BallSocket(anchor, bob, localAnchorB: new Vector3(-2, 0, 0)));
		var rope = world.Create(new Transform(new Vector3(0, 7, 3)), Collider3D.Sphere(0.2f) with { Mask = 0 }, RigidBody3D.Dynamic());
		world.Create(Joint3D.Distance(anchor, rope));
		var door = world.Create(new Transform(new Vector3(1, 10, -5)), Collider3D.Box(new Vector3(2, 2, 0.1f)) with { Mask = 0 }, RigidBody3D.Dynamic());
		var frame = world.Create(new Transform(new Vector3(0, 10, -5)), Collider3D.Sphere(0.1f) with { Mask = 0 }, RigidBody3D.Kinematic());
		world.Create(Joint3D.Hinge(frame, door, Vector3.UnitY, localAnchorB: new Vector3(-1, 0, 0)));

		var lowest = float.MaxValue;
		for (var i = 0; i < 120; i++)
		{
			host.Step();
			lowest = MathF.Min(lowest, bob.Get<Transform>().Position.Y);
			Assert.InRange(Vector3.Distance(bob.Get<Transform>().Position, new Vector3(0, 10, 0)), 1.95f, 2.05f);
		}

		var physics = host.Get<IPhysicsWorld3D>();
		Assert.Equal(3, physics.JointCount);
		Assert.True(lowest < 8.1f, $"the pendulum swung down to {lowest}");
		Assert.True(Vector3.Distance(rope.Get<Transform>().Position, new Vector3(0, 10, 0)) <= Vector3.Distance(new Vector3(0, 7, 3), new Vector3(0, 10, 0)) + 0.05f);
		// The door hangs on its vertical hinge: it does not fall.
		Assert.InRange(door.Get<Transform>().Position.Y, 9.9f, 10.1f);

		world.Destroy(bob);
		host.Step();
		Assert.Equal(2, physics.JointCount);
	}

	[Fact]
	public void TheDebugDrawingSubmitsEveryCollider()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<PhysicsWorld3D>();
		world.Floor();
		world.Ball(new Vector3(0, 3, 0));
		world.Create(new Transform(new Vector3(3, 3, 0)), Collider3D.Capsule(0.3f, 1));
		host.Step();

		var recorder = new Recorder();
		physics.Draw(recorder);
		// One box (the floor), one sphere (the ball), a capsule as a cylinder and two spheres; in body creation order.
		Assert.Equal([DebugShape3D.Box, DebugShape3D.Sphere, DebugShape3D.Sphere, DebugShape3D.Sphere, DebugShape3D.Cylinder], recorder.Shapes.Order());
	}

	[Fact]
	public void EachScopeHasItsOwnPhysicsWorld()
	{
		using var host = Hosts.Physics();
		var root = host.Get<PhysicsWorld3D>();
		Assert.Same(root, host.Get<PhysicsWorld3D>());

		PhysicsWorld3D scoped;
		using (var scope = host.Services.CreateScope())
		{
			scoped = scope.ServiceProvider.GetRequiredService<PhysicsWorld3D>();
			Assert.NotSame(root, scoped);
			Assert.Same(scope.ServiceProvider.GetRequiredService<World>(), scoped.Entities);
		}

		Assert.Throws<ObjectDisposedException>(() => scoped.Simulation);
	}

	[Fact]
	public void StepsAllocateAlmostNothingAfterWarmUp()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<PhysicsWorld3D>();
		world.Floor();
		var rand = new Random(5);
		for (var i = 0; i < 200; i++)
		{
			var collider = i % 2 == 0 ? Collider3D.Sphere(0.3f) : Collider3D.Box(new Vector3(0.5f));
			world.Create(new Transform(new Vector3(rand.NextSingle() * 10 - 5, 1 + rand.NextSingle() * 10, rand.NextSingle() * 10 - 5)), collider, RigidBody3D.Dynamic());
		}

		host.Step(240);
		long allocated = 0;
		for (var i = 0; i < 120; i++)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			physics.Step(1f / 60);
			allocated += GC.GetAllocatedBytesForCurrentThread() - before;
			host.Step();
		}

		// The adapter allocates nothing; BepuPhysics itself grows a small managed array (int[16] or int[32]) when one of
		// its internal lists reaches a new high-water mark, which a settling pile still does now and then.
		Assert.True(allocated <= 1024, $"{allocated} bytes in 120 steps");
	}

	private sealed class Recorder : IDebugShapeSink
	{
		public List<DebugShape3D> Shapes { get; } = [];

		public void Shape(DebugShape3D shape, in Matrix4x4 world, DebugColor3D color, ConvexHullId hull) => Shapes.Add(shape);
	}
}

[Trait(CATEGORY, INTEGRATION)]
public class Replay3DTests(Xunit.Abstractions.ITestOutputHelper output)
{
	[Fact]
	public void TenThousandStepsOfASeededSceneAreIdenticalOnEveryRun()
	{
		var first = Replay3D.Run(10_000);
		var second = Replay3D.Run(10_000);
		output.WriteLine($"3D replay hash after 10,000 steps: {first:X16} ({RuntimeInformation.RuntimeIdentifier}, Vector<float>.Count = {Vector<float>.Count})");
		Assert.Equal(first, second);

		// BepuPhysics works in bundles of Vector<float>.Count bodies, so the result depends on the vector width, not on the
		// processor: 4 lanes (NativeAOT x64, arm64 NEON, or DOTNET_MaxVectorTBitWidth=128) and 8 lanes (the JIT on AVX2)
		// each have one hash, the same on linux-x64 and linux-arm64 (recorded on Linux; see docs/design/ion-physics.md).
		if (OperatingSystem.IsLinux())
		{
			if (Vector<float>.Count == 4) Assert.Equal(GoldenFourLanes, first);
			if (Vector<float>.Count == 8) Assert.Equal(GoldenEightLanes, first);
		}
	}

	/// <summary>The hash with 4-lane vectors (linux-x64 NativeAOT and 128-bit JIT, linux-arm64 NativeAOT under QEMU).</summary>
	public const ulong GoldenFourLanes = 0x0987708E02C11079;

	/// <summary>The hash with 8-lane vectors (linux-x64 JIT on AVX2).</summary>
	public const ulong GoldenEightLanes = 0x0A315BD5EFFEF90B;

	[Fact]
	public void AMultithreadedStepIsDeterministicToo()
	{
		var first = Replay3D.Run(600, threads: 4);
		var second = Replay3D.Run(600, threads: 4);
		output.WriteLine($"3D replay hash after 600 steps on 4 threads: {first:X16}; single-threaded: {Replay3D.Run(600):X16}");
		Assert.Equal(first, second);
	}
}
