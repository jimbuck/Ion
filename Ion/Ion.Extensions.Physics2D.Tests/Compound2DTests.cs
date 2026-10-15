using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Ecs;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics2D.Tests;

/// <summary>Compound bodies: child colliders without a rigid body are parts of the nearest ancestor body.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class Compound2DTests
{
	private static Entity Part(World world, Entity parent, Vector2 position, Collider2D collider, float rotation = 0f)
	{
		var part = world.Create(new Transform2D(position, rotation), collider);
		world.SetParent(part, parent);
		return part;
	}

	private static float MassOf(IPhysicsWorld2D physics, Entity entity)
	{
		Assert.True(physics.TryGetMass(entity, out var mass, out _));
		return mass;
	}

	[Fact]
	public void PartsAddTheirShapesAndMassToTheParentsBody()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var body = world.Create(new Transform2D(Vector2.Zero), Collider2D.Box(Vector2.One), RigidBody2D.Dynamic());
		var part = Part(world, body, new Vector2(2, 0), Collider2D.Box(Vector2.One));
		var reference = world.Create(new Transform2D(new Vector2(-100, -100)), Collider2D.Box(Vector2.One), RigidBody2D.Dynamic());
		host.Step();

		var pw = (PhysicsWorld2D)physics;
		Assert.Equal(2, physics.BodyCount);
		Assert.Equal(1, pw.PartCount);
		var unit = MassOf(physics, reference);
		Assert.True(physics.TryGetMass(body, out var mass, out var center));
		Assert.Equal(2 * unit, mass, 4);
		Assert.Equal(1f, center.X, 3);
		Assert.Equal(0f, center.Y, 3);

		// The part reports its body's mass and entity.
		Assert.True(physics.TryGetMass(part, out var partMass, out _));
		Assert.Equal(mass, partMass);
		Assert.True(physics.TryGetBodyEntity(part, out var owner));
		Assert.Equal(body, owner);
		Assert.True(physics.TryGetBodyEntity(body, out owner));
		Assert.Equal(body, owner);
		Assert.True(part.Get<Collider2D>().HasBody);
	}

	[Fact]
	public void ARigidBodyWithoutAColliderIsTheBodyOfItsParts()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var reference = world.Create(new Transform2D(new Vector2(-100, -100)), Collider2D.Box(Vector2.One), RigidBody2D.Dynamic() with { GravityScale = 0 });

		// Alone it is not a body.
		var body = world.Create(new Transform2D(Vector2.Zero), RigidBody2D.Dynamic());
		host.Step();
		Assert.Equal(1, physics.BodyCount);
		Assert.False(physics.TryGetMass(body, out _, out _));

		var left = Part(world, body, new Vector2(-1, 0), Collider2D.Box(Vector2.One));
		var right = Part(world, body, new Vector2(1, 0), Collider2D.Box(Vector2.One));
		host.Step();
		Assert.Equal(2, physics.BodyCount);
		Assert.True(physics.TryGetMass(body, out var mass, out var center));
		Assert.Equal(2 * MassOf(physics, reference), mass, 4);
		Assert.Equal(0f, center.X, 3);

		// It falls as one, the parts keeping their local transforms.
		host.Step(30);
		Assert.True(body.Get<Transform2D>().Position.Y > 0.5f);
		Assert.Equal(new Vector2(-1, 0), left.Get<Transform2D>().Position);
		Assert.Equal(new Vector2(1, 0), right.Get<Transform2D>().Position);
		Assert.True(body.Get<RigidBody2D>().LinearVelocity.Y > 1f);

		// Without parts it is not a body again.
		world.Destroy(left);
		world.Destroy(right);
		host.Step();
		Assert.Equal(1, physics.BodyCount);
		Assert.Equal(0, ((PhysicsWorld2D)physics).PartCount);
	}

	[Fact]
	public void PartsCollideWhereTheyAreAndEventsNameThePart()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var collisions = host.Collect<Collision2D>();
		var floor = world.Floor(y: 10f);

		// A dumbbell: two balls six units apart on a body without a collider of its own.
		var body = world.Create(new Transform2D(Vector2.Zero), RigidBody2D.Dynamic());
		var left = Part(world, body, new Vector2(-3, 0), Collider2D.Circle(0.5f));
		var right = Part(world, body, new Vector2(3, 0), Collider2D.Circle(0.5f));

		Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase.Begin), 240));
		var begin = collisions.First(c => c.Phase == ContactPhase.Begin);
		Assert.True(begin.Involves(floor));
		var hitPart = begin.Other(floor);
		Assert.True(hitPart == left || hitPart == right, $"{hitPart}");
		Assert.True(physics.TryGetBodyEntity(hitPart, out var owner));
		Assert.Equal(body, owner);

		// It comes to rest on both balls: the floor's top is at 9.5, so the centers (and the body) are at 9.
		host.Step(240);
		var transform = body.Get<Transform2D>();
		Assert.InRange(transform.Position.Y, 8.9f, 9.05f);
		Assert.InRange(transform.Rotation, -0.01f, 0.01f);
		Assert.InRange(transform.Position.X, -0.05f, 0.05f);
	}

	[Fact]
	public void TheHierarchyPlacesPartsInTheBodysSpace()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		Span<Entity> found = stackalloc Entity[4];

		// A static body turned a quarter, with a group in between that is itself moved and turned.
		var body = world.Create(new Transform2D(new Vector2(5, 5), MathF.PI / 2), RigidBody2D.Static());
		var group = world.Create(new Transform2D(new Vector2(2, 0), MathF.PI / 2));
		world.SetParent(group, body);
		var part = Part(world, group, new Vector2(1, 0), Collider2D.Box(new Vector2(0.5f, 0.5f)));
		host.Step();

		// (1, 0) in the group is (2, 1) in the body (the group is turned a quarter), which is (5 - 1, 5 + 2) in the world.
		Assert.Equal(1, physics.OverlapPoint(new Vector2(4, 7), found));
		Assert.Equal(part, found[0]);
		Assert.Equal(0, physics.OverlapPoint(new Vector2(6, 5), found));

		// Moving the part moves its shape.
		part.Get<Transform2D>().Position = new Vector2(-1, 0);
		host.Step();
		Assert.Equal(0, physics.OverlapPoint(new Vector2(4, 7), found));
		Assert.Equal(1, physics.OverlapPoint(new Vector2(6, 7), found));

		// So does moving the body.
		body.Get<Transform2D>().Position = new Vector2(15, 5);
		host.Step();
		Assert.Equal(1, physics.OverlapPoint(new Vector2(16, 7), found));
		Assert.Equal(part, found[0]);
	}

	[Fact]
	public void RemovingOrReshapingAPartUpdatesTheBody()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var body = world.Create(new Transform2D(Vector2.Zero), Collider2D.Box(Vector2.One), RigidBody2D.Dynamic());
		var part = Part(world, body, new Vector2(2, 0), Collider2D.Box(Vector2.One));
		host.Step();
		var two = MassOf(physics, body);

		// Twice the area: three unit boxes' worth.
		part.Get<Collider2D>().Size = new Vector2(2, 1);
		host.Step();
		Assert.Equal(two * 1.5f, MassOf(physics, body), 3);

		part.Remove<Collider2D>();
		host.Step();
		Assert.Equal(two / 2, MassOf(physics, body), 3);
		Assert.Equal(0, ((PhysicsWorld2D)physics).PartCount);

		// A mass override still wins over the parts.
		part.Add(Collider2D.Box(Vector2.One));
		body.Get<RigidBody2D>().Mass = 7f;
		host.Step();
		Assert.Equal(7f, MassOf(physics, body), 3);
	}

	[Fact]
	public void AChildColliderWithNoBodyAboveIsStaticAtItsWorldPosition()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		Span<Entity> found = stackalloc Entity[4];
		var group = world.Create(new Transform2D(new Vector2(10, 0)));
		var wall = Part(world, group, new Vector2(1, 0), Collider2D.Box(Vector2.One));
		host.Step();

		Assert.Equal(1, physics.BodyCount);
		Assert.Equal(0, ((PhysicsWorld2D)physics).PartCount);
		Assert.Equal(1, physics.OverlapPoint(new Vector2(11, 0), found));
		Assert.Equal(wall, found[0]);
		Assert.True(physics.TryGetBodyEntity(wall, out var owner));
		Assert.Equal(wall, owner);

		// Under a body it becomes a part of it.
		group.Add(RigidBody2D.Static());
		host.Step();
		Assert.Equal(1, physics.BodyCount);
		Assert.Equal(1, ((PhysicsWorld2D)physics).PartCount);
		Assert.True(physics.TryGetBodyEntity(wall, out owner));
		Assert.Equal(group, owner);
	}

	[Fact]
	public void ForcesOnAPartPushItsBody()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld2D>();
		var body = world.Create(new Transform2D(Vector2.Zero), RigidBody2D.Dynamic());
		var part = Part(world, body, new Vector2(0, 0), Collider2D.Circle(0.5f));
		host.Step();
		Assert.True(physics.ApplyLinearImpulse(part, new Vector2(1, 0)));
		host.Step();
		Assert.True(body.Get<RigidBody2D>().LinearVelocity.X > 0);
	}
}
