using Arch.Core;
using Arch.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Testing;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics2D.Tests;

internal static class Hosts
{
	/// <summary>A headless host with the ECS and 2D physics modules in the root schedule (one fixed step per frame).</summary>
	public static IonTestHost Physics(Action<Physics2DConfig>? configure = null) =>
		new IonTestHost()
			.Configure(services => services.AddEcs().AddPhysics2D(configure: configure))
			.ConfigureApp(app => app.UseEcs().UsePhysics2D());

	public static Entity Floor(this World world, float y = 10f, float width = 40f) =>
		world.Create(new Transform2D(new Vector2(0, y)), Collider2D.Box(new Vector2(width, 1f)));

	public static Entity Ball(this World world, Vector2 position, float radius = 0.5f, Vector2 velocity = default) =>
		world.Create(new Transform2D(position), Collider2D.Circle(radius), RigidBody2D.Dynamic(velocity));
}

[Trait(CATEGORY, INTEGRATION)]
public class Physics2DTests
{
	[Fact]
	public void ADynamicBodyFallsAndComesToRestOnAStaticFloor()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Floor(y: 10f);
		var ball = world.Ball(new Vector2(0, 0));

		host.Step(10);
		var falling = ball.Get<Transform2D>().Position.Y;
		Assert.True(falling > 0.1f, $"y {falling} after 10 steps");
		Assert.True(ball.Get<RigidBody2D>().LinearVelocity.Y > 1f, "the pulled velocity points down");

		host.Step(240);
		// The floor's top is at 9.5 and the ball's radius 0.5: it rests with its center near 9.
		var rest = ball.Get<Transform2D>().Position.Y;
		Assert.InRange(rest, 8.9f, 9.05f);
		Assert.Equal(2, host.Get<IPhysicsWorld2D>().BodyCount);
	}

	[Fact]
	public void UnitsPerMeterScalesLengthsAndGravityStaysInWorldUnits()
	{
		// A pixel game: 100 px per meter, gravity 981 px/s^2. After one second of free fall the ball is about 490 px lower.
		using var host = Hosts.Physics(c => { c.UnitsPerMeter = 100; c.GravityY = 981; });
		var world = host.Get<World>();
		var ball = world.Ball(new Vector2(0, 0), radius: 16);

		host.Step(60);
		Assert.InRange(ball.Get<Transform2D>().Position.Y, 470f, 500f);
		Assert.InRange(ball.Get<RigidBody2D>().LinearVelocity.Y, 960f, 990f);
	}

	[Fact]
	public void GameCodeVelocityChangesAreAppliedAtTheNextStep()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var ball = world.Ball(Vector2.Zero);
		host.Step();

		ball.Get<RigidBody2D>().LinearVelocity = new Vector2(6, 0);
		host.Step(60);

		Assert.InRange(ball.Get<Transform2D>().Position.X, 5.8f, 6.1f);
		Assert.Equal(6f, ball.Get<RigidBody2D>().LinearVelocity.X, 3);
	}

	[Fact]
	public void MovingTheTransformOfADynamicBodyTeleportsIt()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var ball = world.Ball(Vector2.Zero);
		host.Step();

		ball.Get<Transform2D>().Position = new Vector2(100, 50);
		host.Step();
		Assert.Equal(new Vector2(100, 50), ball.Get<Transform2D>().Position);
	}

	[Fact]
	public void AKinematicBodyFollowsItsTransformAndPushesDynamicBodies()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var pusher = world.Create(new Transform2D(new Vector2(-3, 0)), Collider2D.Box(new Vector2(1, 4)), RigidBody2D.Kinematic());
		var ball = world.Ball(new Vector2(0, 0));
		host.Step();

		// Move the paddle 0.1 per step to the right, as game code in FixedUpdate would.
		for (var i = 0; i < 40; i++)
		{
			ref var transform = ref pusher.Get<Transform2D>();
			transform.Position += new Vector2(0.1f, 0);
			host.Step();
		}

		Assert.InRange(pusher.Get<Transform2D>().Position.X, 0.95f, 1.05f);
		Assert.True(ball.Get<Transform2D>().Position.X > 1.4f, $"the ball was pushed to {ball.Get<Transform2D>().Position.X}");

		// Once the transform stops changing the kinematic body stops too (it does not keep the target velocity).
		var stopped = pusher.Get<Transform2D>().Position;
		host.Step(30);
		Assert.Equal(stopped.X, pusher.Get<Transform2D>().Position.X, 3);
	}

	[Fact]
	public void CollisionEventsReportBeginAndEndWithTheEntities()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var collisions = host.Collect<Collision2D>();
		var floor = world.Floor(y: 3f);
		var ball = world.Create(new Transform2D(new Vector2(0, 0)), Collider2D.Circle(0.5f) with { Restitution = 0.9f }, RigidBody2D.Dynamic());

		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase.Begin), 120));
		var begin = collisions.First(c => c.Phase == ContactPhase.Begin);
		Assert.True(begin.Involves(floor) && begin.Involves(ball));
		Assert.Equal(floor, begin.Other(ball));
		Assert.InRange(MathF.Abs(begin.Normal.Y), 0.99f, 1.01f);
		Assert.InRange(begin.Point.Y, 2.3f, 2.7f);

		// It bounces off: the contact ends.
		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase.End), 120));
		Assert.True(collisions.First(c => c.Phase == ContactPhase.End).Involves(ball));
	}

	[Fact]
	public void DestroyingAnEntityEndsItsContactsAndRemovesItsBody()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var collisions = host.Collect<Collision2D>();
		var floor = world.Floor(y: 1f);
		var ball = world.Ball(Vector2.Zero);
		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase.Begin), 120));

		world.Destroy(ball);
		host.Step(2);

		var physics = host.Get<IPhysicsWorld2D>();
		Assert.Equal(1, physics.BodyCount);
		Assert.Contains(collisions, c => c.Phase == ContactPhase.End && c.Involves(ball) && c.Involves(floor));
	}

	[Fact]
	public void RemovingTheColliderRemovesTheBodyAndChangingItRebuildsTheShape()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var box = world.Create(new Transform2D(Vector2.Zero), Collider2D.Box(new Vector2(1, 1)));
		host.Step();
		Assert.Equal(1, physics.BodyCount);
		Assert.Equal(1, physics.OverlapPoint(new Vector2(0.4f, 0), new Entity[4]));

		box.Get<Collider2D>().Size = new Vector2(4, 1);
		host.Step();
		Assert.Equal(1, physics.OverlapPoint(new Vector2(1.9f, 0), new Entity[4]));

		box.Remove<Collider2D>();
		host.Step();
		Assert.Equal(0, physics.BodyCount);
	}

	[Fact]
	public void ACopiedColliderGetsItsOwnBody()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var first = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(1));
		host.Step();

		// The copy carries the first body's slot; the world notices it belongs to another entity.
		var second = world.Create(new Transform2D(new Vector2(10, 0)), first.Get<Collider2D>());
		host.Step();

		Assert.Equal(2, physics.BodyCount);
		Span<Entity> found = new Entity[4];
		Assert.Equal(1, physics.OverlapPoint(new Vector2(10, 0), found));
		Assert.Equal(second, found[0]);
	}

	[Fact]
	public void SensorsReportTriggersWithoutColliding()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var triggers = host.Collect<Trigger2D>();
		var collisions = host.Collect<Collision2D>();
		var sensor = world.Create(new Transform2D(Vector2.Zero), Collider2D.Box(new Vector2(2, 2)) with { IsSensor = true });
		var ball = world.Ball(new Vector2(-5, 0), velocity: new Vector2(10, 0));

		host.Step(60);

		Assert.Contains(triggers, t => t.Phase == ContactPhase.Begin && t.Sensor == sensor && t.Visitor == ball);
		Assert.Contains(triggers, t => t.Phase == ContactPhase.End && t.Sensor == sensor && t.Visitor == ball);
		Assert.Empty(collisions);
		Assert.True(ball.Get<Transform2D>().Position.X > 4f, "the ball went through the sensor");
	}

	[Fact]
	public void LayersAndMasksFilterCollisionsAndQueries()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Create(new Transform2D(new Vector2(0, 3)), Collider2D.Box(new Vector2(10, 1)) with { Layer = 2 });
		var ghost = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.5f) with { Mask = ~2u }, RigidBody2D.Dynamic());

		host.Step(90);
		Assert.True(ghost.Get<Transform2D>().Position.Y > 5, "the ball fell through the floor it does not collide with");

		var physics = host.Get<IPhysicsWorld2D>();
		Assert.True(physics.RayCast(new Vector2(0, -10), new Vector2(0, 30), out _, mask: 2));
		Assert.False(physics.RayCast(new Vector2(0, -10), new Vector2(0, 12), out _, mask: 1));
	}

	[Fact]
	public void QueriesFindTheRightEntities()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var left = world.Create(new Transform2D(new Vector2(-5, 0)), Collider2D.Box(new Vector2(2, 2)));
		var right = world.Create(new Transform2D(new Vector2(5, 0)), Collider2D.Circle(1));
		var capsule = world.Create(new Transform2D(new Vector2(0, 5)), Collider2D.Capsule(new Vector2(4, 1)));
		var triangle = world.Create(new Transform2D(new Vector2(0, -5)), Collider2D.Polygon([new(-1, 1), new(1, 1), new(0, -1)]));
		host.Step();

		Assert.True(physics.RayCast(new Vector2(-10, 0), new Vector2(20, 0), out var hit));
		Assert.Equal(left, hit.Entity);
		Assert.Equal(-6f, hit.Point.X, 2);
		Assert.Equal(-1f, hit.Normal.X, 2);
		Assert.Equal(0.2f, hit.Fraction, 2);

		Assert.True(physics.RayCast(new Vector2(10, 0), new Vector2(-20, 0), out hit));
		Assert.Equal(right, hit.Entity);

		Span<Entity> found = new Entity[8];
		Assert.Equal(1, physics.OverlapPoint(new Vector2(1.8f, 5), found));
		Assert.Equal(capsule, found[0]);
		Assert.Equal(0, physics.OverlapPoint(new Vector2(2.1f, 5.4f), found));
		Assert.Equal(1, physics.OverlapPoint(new Vector2(0, -5.5f), found));
		Assert.Equal(triangle, found[0]);

		Assert.Equal(2, physics.OverlapBox(new Vector2(-7, -1), new Vector2(7, 1), found));
		Assert.Equal(1, physics.OverlapCircle(new Vector2(3.5f, 0), 0.6f, found));
		Assert.Equal(right, found[0]);
		Assert.Equal(0, physics.OverlapCircle(new Vector2(3.5f, 0), 0.4f, found));

		// The span bounds the results.
		Assert.Equal(1, physics.OverlapBox(new Vector2(-7, -1), new Vector2(7, 1), found[..1]));
	}

	[Fact]
	public void ForcesAndImpulsesMoveDynamicBodies()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var ball = world.Ball(Vector2.Zero);
		Assert.False(physics.ApplyLinearImpulse(ball, new Vector2(1, 0)), "no body before the first step");
		host.Step();

		var mass = MathF.PI * 0.25f; // density 1, radius 0.5
		Assert.True(physics.ApplyLinearImpulse(ball, new Vector2(2 * mass, 0)));
		host.Step();
		Assert.Equal(2f, ball.Get<RigidBody2D>().LinearVelocity.X, 2);

		Assert.True(physics.ApplyAngularImpulse(ball, 1f));
		host.Step();
		Assert.True(ball.Get<RigidBody2D>().AngularVelocity > 0);
		Assert.True(ball.Get<Transform2D>().Rotation > 0);
	}

	[Fact]
	public void MassOverridesTheDensity()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var heavy = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.5f), RigidBody2D.Dynamic() with { Mass = 10 });
		host.Step();

		physics.ApplyLinearImpulse(heavy, new Vector2(10, 0));
		host.Step();
		Assert.Equal(1f, heavy.Get<RigidBody2D>().LinearVelocity.X, 2);
	}

	[Fact]
	public void ARevoluteJointMakesAPendulum()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var pivot = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.1f) with { Mask = 0 });
		var bob = world.Create(new Transform2D(new Vector2(3, 0)), Collider2D.Circle(0.25f) with { Mask = 0 }, RigidBody2D.Dynamic());
		world.Create(Joint2D.Revolute(pivot, bob, localAnchorB: new Vector2(-3, 0)));

		var physics = host.Get<IPhysicsWorld2D>();
		var lowest = 0f;
		for (var i = 0; i < 120; i++)
		{
			host.Step();
			var position = bob.Get<Transform2D>().Position;
			Assert.InRange(position.Length(), 2.95f, 3.05f);
			lowest = MathF.Max(lowest, position.Y);
		}

		Assert.Equal(1, physics.JointCount);
		Assert.True(lowest > 2.9f, $"the bob swung down to {lowest}");
	}

	[Fact]
	public void DistanceAndPrismaticJointsConstrainTheBodies()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var anchor = world.Create(new Transform2D(Vector2.Zero), Collider2D.Box(new Vector2(0.2f, 0.2f)) with { Mask = 0 });
		var hanging = world.Create(new Transform2D(new Vector2(0, 2)), Collider2D.Circle(0.25f) with { Mask = 0 }, RigidBody2D.Dynamic());
		var slider = world.Create(new Transform2D(new Vector2(5, 0)), Collider2D.Box(new Vector2(0.5f, 0.5f)) with { Mask = 0 }, RigidBody2D.Dynamic());
		world.Create(Joint2D.Distance(anchor, hanging));
		// The slider moves along x only (gravity pulls along y): it stays where it is.
		world.Create(Joint2D.Prismatic(anchor, slider, Vector2.UnitX, localAnchorA: new Vector2(5, 0)));

		host.Step(120);
		Assert.InRange(hanging.Get<Transform2D>().Position.Length(), 1.97f, 2.03f);
		Assert.InRange(slider.Get<Transform2D>().Position.Y, -0.02f, 0.02f);
		Assert.Equal(2, host.Get<IPhysicsWorld2D>().JointCount);

		// Destroying a body removes its joints.
		world.Destroy(hanging);
		host.Step();
		Assert.Equal(1, host.Get<IPhysicsWorld2D>().JointCount);
	}

	[Fact]
	public void TheDebugDrawingDrawsLinesOnlyWhenOn()
	{
		using var host = Hosts.Physics(c => c.DebugDraw = false);
		var world = host.Get<World>();
		world.Floor();
		world.Ball(Vector2.Zero);
		world.Create(new Transform2D(new Vector2(5, 0)), Collider2D.Capsule(new Vector2(3, 1)));
		host.Step();
		var baseline = host.SpriteBatch.LastFrame.Lines;
		Assert.Equal(0, baseline);

		host.Get<IPhysicsWorld2D>().DebugDraw = true;
		host.Step();
		// Box: 4 lines, circle: 16 + 1, capsule: 2 x 16 + 2.
		Assert.Equal(4 + 17 + 34, host.SpriteBatch.LastFrame.Lines);
	}

	[Fact]
	public void EachScopeHasItsOwnPhysicsWorld()
	{
		using var host = Hosts.Physics();
		var root = host.Get<PhysicsWorld2D>();
		Assert.Same(root, host.Get<PhysicsWorld2D>());
		Assert.Same(host.Get<World>(), root.Entities);

		PhysicsWorld2D scoped;
		using (var scope = host.Services.CreateScope())
		{
			scoped = scope.ServiceProvider.GetRequiredService<PhysicsWorld2D>();
			Assert.NotSame(root, scoped);
			Assert.Same(scope.ServiceProvider.GetRequiredService<World>(), scoped.Entities);
		}

		Assert.Throws<ObjectDisposedException>(() => scoped.Gravity);
		Assert.Equal(9.81f, root.Gravity.Y, 3);
	}

	[Fact]
	public void InvalidGeometryFailsWithAMessageInsteadOfAnAssert()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0));

		var error = Assert.Throws<InvalidOperationException>(() => host.Step());
		Assert.Contains("positive radius", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void StepsAllocateNothingAfterWarmUp()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<PhysicsWorld2D>();
		world.Floor(y: 20, width: 60);
		var rand = new Random(3);
		for (var i = 0; i < 200; i++)
		{
			var collider = i % 2 == 0 ? Collider2D.Circle(0.4f) : Collider2D.Box(new Vector2(0.8f, 0.6f));
			world.Create(new Transform2D(new Vector2(rand.NextSingle() * 50 - 25, rand.NextSingle() * 15)), collider with { Restitution = 0.5f }, RigidBody2D.Dynamic());
		}

		host.Step(240);

		// Measure only the physics step (the host's frame has its own costs): run it by hand between frames.
		long allocated = 0;
		for (var i = 0; i < 120; i++)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			physics.Step(1f / 60);
			allocated += GC.GetAllocatedBytesForCurrentThread() - before;
			host.Step();
		}

		Assert.Equal(0, allocated);
	}
}

[Trait(CATEGORY, INTEGRATION)]
public class Replay2DTests(Xunit.Abstractions.ITestOutputHelper output)
{
	/// <summary>
	/// The hash after 10,000 steps on linux-x64 (JIT and NativeAOT), which is also the hash on linux-arm64 with Box2D
	/// built without fused multiply-add contraction (docs/design/ion-physics.md, "Determinism"). The packaged arm64
	/// natives contract to FMA and give 0x5780ABC152A90013 instead. Checked on Linux x64 only (the platform it was
	/// recorded on); every platform checks that repeated runs are identical.
	/// </summary>
	public const ulong GoldenLinuxX64 = 0x7DF39E42CA6D46A6;

	[Fact]
	public void TenThousandStepsOfASeededSceneAreIdenticalOnEveryRun()
	{
		var first = Replay2D.Run(10_000);
		var second = Replay2D.Run(10_000);
		output.WriteLine($"2D replay hash after 10,000 steps: {first:X16} ({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier})");

		Assert.Equal(first, second);
		if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
		{
			Assert.Equal(GoldenLinuxX64, first);
		}
	}

	[Fact]
	public void DifferentSeedsGiveDifferentStates() => Assert.NotEqual(Replay2D.Run(600, seed: 1), Replay2D.Run(600, seed: 2));
}
