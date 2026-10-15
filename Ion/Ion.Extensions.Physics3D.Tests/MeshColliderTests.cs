using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics3D.Tests;

/// <summary>Triangle mesh colliders: level geometry from the same mesh data the renderer draws.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class MeshColliderTests
{
	private static MeshColliderId Ground(IPhysicsWorld3D physics, bool doubleSided = false)
	{
		// The renderer's own plane (facing up, counterclockwise from above), 20 units wide, in 4 x 4 quads.
		var plane = MeshPrimitives.Plane(20f, 4);
		return physics.CreateMesh(plane.Positions, plane.Indices, doubleSided);
	}

	[Fact]
	public void ABallRestsOnAMeshMadeFromRenderData()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var ground = world.Create(new Transform(Vector3.Zero), Collider3D.TriangleMesh(Ground(physics)));
		var ball = world.Ball(new Vector3(1, 3, 2));
		var collisions = host.Collect<Collision3D>();

		host.Step(240);
		Assert.InRange(ball.Get<Transform>().Position.Y, 0.45f, 0.55f);
		Assert.Contains(collisions, c => c.Phase == ContactPhase3D.Begin && c.Involves(ground) && c.Involves(ball));
	}

	[Fact]
	public void MeshesCollideOnTheirFrontSideUnlessDoubleSided()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var front = world.Create(new Transform(Vector3.Zero), Collider3D.TriangleMesh(Ground(physics)));
		var both = world.Create(new Transform(new Vector3(40, 0, 0)), Collider3D.TriangleMesh(Ground(physics, doubleSided: true)));
		host.Step();

		// From above (the side the plane faces) the ray hits; from below it passes through, unless double-sided.
		Assert.True(physics.RayCast(new Vector3(0, 5, 0), -Vector3.UnitY, 10, out var hit));
		Assert.Equal(front, hit.Entity);
		Assert.Equal(5f, hit.Distance, 3);
		Assert.InRange(hit.Normal.Y, 0.99f, 1.01f);
		Assert.False(physics.RayCast(new Vector3(0, -5, 0), Vector3.UnitY, 10, out _));
		Assert.True(physics.RayCast(new Vector3(40, -5, 0), Vector3.UnitY, 10, out hit));
		Assert.Equal(both, hit.Entity);
	}

	[Fact]
	public void AMeshFollowsItsEntitysTransform()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var ramp = world.Create(new Transform(new Vector3(0, 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6)), Collider3D.TriangleMesh(Ground(physics)), RigidBody3D.Static());
		host.Step();

		// Turned 30 degrees around z: straight down at x = 2 the surface is at 2 + 2 * tan(30).
		Assert.True(physics.RayCast(new Vector3(2, 10, 0), -Vector3.UnitY, 20, out var hit));
		Assert.Equal(ramp, hit.Entity);
		Assert.Equal(2 + 2 * MathF.Tan(MathF.PI / 6), hit.Point.Y, 2);
	}

	[Fact]
	public void MeshesAreForStaticAndKinematicBodiesOnly()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var mesh = Ground(physics);

		world.Create(new Transform(Vector3.Zero), Collider3D.TriangleMesh(mesh), RigidBody3D.Kinematic());
		host.Step();
		Assert.Equal(1, physics.BodyCount);

		world.Create(new Transform(Vector3.Zero), Collider3D.TriangleMesh(mesh), RigidBody3D.Dynamic());
		Assert.Contains("dynamic", Assert.Throws<InvalidOperationException>(() => host.Step()).Message);
	}

	[Fact]
	public void AMeshCannotBeAPartOfACompound()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var body = world.Create(new Transform(Vector3.Zero), Collider3D.Box(Vector3.One), RigidBody3D.Static());
		var part = world.Create(new Transform(Vector3.Zero), Collider3D.TriangleMesh(Ground(physics)));
		world.SetParent(part, body);
		Assert.Contains("cannot be a part", Assert.Throws<InvalidOperationException>(() => host.Step()).Message);
	}

	[Fact]
	public void InvalidMeshDataIsRejected()
	{
		using var host = Hosts.Physics();
		var physics = host.Get<IPhysicsWorld3D>();
		Vector3[] points = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ];
		Assert.Throws<ArgumentException>(() => physics.CreateMesh(points, [0, 1]));
		Assert.Throws<ArgumentException>(() => physics.CreateMesh(points, [0, 1, 3]));
		Assert.Throws<ArgumentException>(() => physics.CreateMesh(points, [0, 0, 1]));
		Assert.True(physics.CreateMesh(points, [0, 1, 2, 0, 0, 1]).IsValid);
	}
}
