using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics3D.Tests;

/// <summary>Compound bodies: child colliders without a rigid body are parts of the nearest ancestor body.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class Compound3DTests
{
	private static Entity Part(World world, Entity parent, Vector3 position, Collider3D collider, Quaternion? rotation = null)
	{
		var part = world.Create(new Transform(position, rotation), collider);
		world.SetParent(part, parent);
		return part;
	}

	[Fact]
	public void TheMassIsSharedByVolumeAndPlacesTheCenter()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var body = world.Create(new Transform(Vector3.Zero), Collider3D.Box(Vector3.One), RigidBody3D.Dynamic(mass: 3f));
		var part = Part(world, body, new Vector3(2, 0, 0), Collider3D.Box(new Vector3(2, 1, 1)));
		host.Step();

		Assert.Equal(1, physics.BodyCount);
		Assert.Equal(1, ((PhysicsWorld3D)physics).PartCount);
		Assert.True(physics.TryGetMass(body, out var mass, out var center));
		Assert.Equal(3f, mass, 3);
		// Volumes 1 and 2: the center is two thirds of the way to the part.
		Assert.Equal(4f / 3f, center.X, 3);
		Assert.Equal(0f, center.Y, 3);

		Assert.True(physics.TryGetBodyEntity(part, out var owner));
		Assert.Equal(body, owner);
		Assert.True(physics.TryGetMass(part, out var partMass, out _));
		Assert.Equal(mass, partMass);
		Assert.True(part.Get<Collider3D>().HasBody);

		// The transform stays the entity's (not the center of mass's).
		host.Step(5);
		Assert.Equal(Vector3.Zero, body.Get<Transform>().Position);

		// Removing the part rebuilds the body around the box alone.
		part.Remove<Collider3D>();
		host.Step();
		Assert.True(physics.TryGetMass(body, out _, out center));
		Assert.Equal(Vector3.Zero, center);
		Assert.Equal(0, ((PhysicsWorld3D)physics).PartCount);
	}

	[Fact]
	public void ADumbbellFallsAsOneAndRestsOnBothEnds()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var collisions = host.Collect<Collision3D>();
		var floor = world.Floor();

		// A rigid body without a collider of its own: alone it is not a body.
		var body = world.Create(new Transform(new Vector3(0, 3, 0)), RigidBody3D.Dynamic());
		host.Step();
		Assert.Equal(1, physics.BodyCount);

		var left = Part(world, body, new Vector3(-2, 0, 0), Collider3D.Sphere(0.5f));
		var right = Part(world, body, new Vector3(2, 0, 0), Collider3D.Sphere(0.5f));
		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase3D.Begin), 240));
		var begin = collisions.First(c => c.Phase == ContactPhase3D.Begin);
		Assert.True(begin.Involves(floor));
		var hit = begin.Other(floor);
		Assert.True(hit == left || hit == right, $"{hit}");
		Assert.True(physics.TryGetBodyEntity(hit, out var owner));
		Assert.Equal(body, owner);

		host.Step(240);
		var transform = body.Get<Transform>();
		Assert.InRange(transform.Position.Y, 0.45f, 0.55f);
		Assert.InRange(MathF.Abs(transform.Rotation.W), 0.999f, 1.001f);
		Assert.Equal(new Vector3(-2, 0, 0), left.Get<Transform>().Position);
		Assert.Equal(2, physics.BodyCount);
	}

	[Fact]
	public void RayCastsHitPartsWhereTheHierarchyPutsThemAndFilterThemOneByOne()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();

		// A static body turned a quarter around y: a part at x = 2 is at z = -2 in the world.
		var body = world.Create(new Transform(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)), RigidBody3D.Static());
		var near = Part(world, body, new Vector3(2, 0, 0), Collider3D.Box(Vector3.One));
		var far = Part(world, body, new Vector3(-2, 0, 0), Collider3D.Box(Vector3.One) with { Layer = 2 });
		host.Step();

		Assert.True(physics.RayCast(new Vector3(10, 10, -2), -Vector3.UnitY, 20, out var hit));
		Assert.Equal(near, hit.Entity);
		Assert.Equal(0.5f, hit.Point.Y, 2);
		Assert.True(physics.RayCast(new Vector3(10, 10, 2), -Vector3.UnitY, 20, out hit));
		Assert.Equal(far, hit.Entity);

		// Layer 1 only: the far part is skipped.
		Assert.False(physics.RayCast(new Vector3(10, 10, 2), -Vector3.UnitY, 20, out _, mask: 1));
		Assert.True(physics.RayCast(new Vector3(10, 10, -2), -Vector3.UnitY, 20, out _, mask: 1));
	}

	[Fact]
	public void ASensorPartTriggersWithoutStopping()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var triggers = host.Collect<Trigger3D>();
		world.Floor(y: -10f);

		// A kinematic gate: a solid post and a sensor beside it.
		var gate = world.Create(new Transform(Vector3.Zero), RigidBody3D.Kinematic());
		var post = Part(world, gate, new Vector3(-3, 0, 0), Collider3D.Box(Vector3.One));
		var sensor = Part(world, gate, new Vector3(3, 0, 0), Collider3D.Box(new Vector3(2, 1, 2)) with { IsSensor = true });
		var ball = world.Ball(new Vector3(3, 3, 0));

		Assert.True(host.RunUntil(() => triggers.Any(t => t.Phase == ContactPhase3D.Begin), 120));
		var begin = triggers.First(t => t.Phase == ContactPhase3D.Begin);
		Assert.Equal(sensor, begin.Sensor);
		Assert.Equal(ball, begin.Visitor);

		// It falls through to the floor, ten units down.
		host.Step(240);
		Assert.True(ball.Get<Transform>().Position.Y < -8f, $"{ball.Get<Transform>().Position}");
		_ = post;
	}

	[Fact]
	public void AChildColliderWithNoBodyAboveIsStaticAtItsWorldPosition()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var group = world.Create(new Transform(new Vector3(10, 0, 0)));
		var wall = Part(world, group, new Vector3(0, 0, 3), Collider3D.Box(Vector3.One));
		host.Step();

		Assert.Equal(1, physics.BodyCount);
		Assert.True(physics.RayCast(new Vector3(10, 10, 3), -Vector3.UnitY, 20, out var hit));
		Assert.Equal(wall, hit.Entity);
		Assert.True(physics.TryGetBodyEntity(wall, out var owner));
		Assert.Equal(wall, owner);

		// Under a body it becomes one of its parts.
		group.Add(RigidBody3D.Static());
		host.Step();
		Assert.True(physics.TryGetBodyEntity(wall, out owner));
		Assert.Equal(group, owner);
		Assert.True(physics.RayCast(new Vector3(10, 10, 3), -Vector3.UnitY, 20, out hit));
		Assert.Equal(wall, hit.Entity);
	}
}
