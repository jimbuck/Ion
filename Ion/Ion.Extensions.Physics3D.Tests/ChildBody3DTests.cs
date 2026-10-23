using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics3D.Tests;

/// <summary>Rigid bodies on child entities: simulated at their world pose, written back relative to their parent.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class ChildBody3DTests
{
	private static Vector3 WorldPosition(World world, Entity entity) => world.Get<GlobalTransform>(entity).Matrix.Translation;

	[Fact]
	public void ADynamicChildFallsInWorldSpaceAndIsWrittenBackRelativeToItsParent()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Floor();

		// A group turned a quarter around z and scaled by 2: local (1, 0, 0) is world (5, 2, 0).
		var group = world.Create(new Transform(new Vector3(5, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), new Vector3(2)));
		var ball = world.Create(new Transform(new Vector3(1, 0, 0)), Collider3D.Sphere(0.5f), RigidBody3D.Dynamic());
		world.SetParent(ball, group);
		host.Step();
		Assert.InRange(WorldPosition(world, ball).X, 4.99f, 5.01f);
		Assert.InRange(WorldPosition(world, ball).Y, 1.9f, 2.05f);

		// It rests on the floor (top at 0): world y 0.5, local x 0.25 (world y = 2 * local x).
		host.Step(240);
		Assert.InRange(WorldPosition(world, ball).Y, 0.45f, 0.55f);
		Assert.InRange(ball.Get<Transform>().Position.X, 0.22f, 0.28f);
	}

	[Fact]
	public void AKinematicChildIsCarriedByItsMovingParent()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var platform = world.Create(new Transform(Vector3.Zero), Collider3D.Box(new Vector3(2, 2, 2)), RigidBody3D.Dynamic(velocity: new Vector3(5, 0, 0)));
		var paddle = world.Create(new Transform(new Vector3(3, 0, 0)), Collider3D.Box(new Vector3(0.5f, 2, 2)), RigidBody3D.Kinematic());
		world.SetParent(paddle, platform);
		var ball = world.Ball(new Vector3(8, 0, 0));
		host.Step(60);

		var platformX = platform.Get<Transform>().Position.X;
		Assert.InRange(platformX, 4f, 5.5f);
		Assert.InRange(paddle.Get<Transform>().Position.X, 2.99f, 3.01f);
		Assert.InRange(WorldPosition(world, paddle).X - platformX, 2.9f, 3.1f);
		Assert.True(ball.Get<Transform>().Position.X > 8.5f, $"{ball.Get<Transform>().Position}");
	}

	[Fact]
	public void PartsUnderAScaledBodyAreWhereTheyAreDrawn()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var body = world.Create(new Transform(Vector3.Zero, null, new Vector3(3)), RigidBody3D.Static());
		var part = world.Create(new Transform(new Vector3(2, 0, 0)), Collider3D.Box(Vector3.One));
		world.SetParent(part, body);
		host.Step(2);

		Assert.Equal(new Vector3(6, 0, 0), WorldPosition(world, part));
		var physics = host.Get<IPhysicsWorld3D>();
		Assert.True(physics.RayCast(new Vector3(6, 10, 0), -Vector3.UnitY, 20, out var hit));
		Assert.Equal(part, hit.Entity);
		Assert.False(physics.RayCast(new Vector3(2, 10, 0), -Vector3.UnitY, 20, out _));
	}
}
