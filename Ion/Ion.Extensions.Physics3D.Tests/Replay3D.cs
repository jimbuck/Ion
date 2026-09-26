using System.Numerics;

using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Physics3D.Tests;

/// <summary>
/// The 3D replay scene: a seeded mix of 200 boxes, spheres, capsules, cylinders and convex hulls thrown into a closed box,
/// a chain of four links on ball sockets hanging from a kinematic anchor, a hinged door and a kinematic sweeper driven by
/// the step number, run through <see cref="PhysicsWorld3D.Step"/> with a 1/60 s step. The result is a hash of every
/// entity's <see cref="Transform"/> bits (in creation order) and the world's state hash. Shared by the replay tests and the
/// arm64 replay runner (docs/plans/benchmarks/2026-09-physics2d/replay).
/// </summary>
public static class Replay3D
{
	/// <summary>The fixed step.</summary>
	public const float Dt = 1f / 60f;

	/// <summary>Builds the scene with <paramref name="seed"/>, runs <paramref name="steps"/> steps and returns the hash.</summary>
	public static ulong Run(int steps, int seed = 2026, int threads = 0)
	{
		using var world = World.Create();
		using var physics = new PhysicsWorld3D(world, events: null, new Physics3DConfig { ThreadCount = threads });
		var entities = Build(world, physics, seed, out var sweeper);

		for (var step = 0; step < steps; step++)
		{
			Drive(world, sweeper, step);
			physics.Step(Dt);
		}

		return Hash(world, entities, physics);
	}

	/// <summary>Creates the scene's entities; returns them in creation order.</summary>
	public static List<Entity> Build(World world, PhysicsWorld3D physics, int seed, out Entity sweeper)
	{
		var rand = new Random(seed);
		var entities = new List<Entity>
		{
			// A closed 20 x 20 box with a floor at y = 0.
			world.Create(new Transform(new Vector3(0, -0.5f, 0)), Collider3D.Box(new Vector3(22, 1, 22))),
			world.Create(new Transform(new Vector3(-10.5f, 5, 0)), Collider3D.Box(new Vector3(1, 10, 22))),
			world.Create(new Transform(new Vector3(10.5f, 5, 0)), Collider3D.Box(new Vector3(1, 10, 22))),
			world.Create(new Transform(new Vector3(0, 5, -10.5f)), Collider3D.Box(new Vector3(22, 10, 1))),
			world.Create(new Transform(new Vector3(0, 5, 10.5f)), Collider3D.Box(new Vector3(22, 10, 1))),
		};

		var hull = physics.CreateConvexHull(
		[
			new(0, 0.5f, 0), new(-0.4f, -0.3f, -0.4f), new(0.4f, -0.3f, -0.4f), new(0.4f, -0.3f, 0.4f), new(-0.4f, -0.3f, 0.4f),
		]);

		for (var i = 0; i < 200; i++)
		{
			var position = new Vector3(rand.NextSingle() * 16 - 8, 1 + rand.NextSingle() * 12, rand.NextSingle() * 16 - 8);
			var rotation = Quaternion.Normalize(new Quaternion(rand.NextSingle() - 0.5f, rand.NextSingle() - 0.5f, rand.NextSingle() - 0.5f, rand.NextSingle() + 0.1f));
			var size = 0.3f + rand.NextSingle() * 0.4f;
			var collider = (i % 5) switch
			{
				0 => Collider3D.Box(new Vector3(size, size * 0.8f, size * 1.2f)),
				1 => Collider3D.Sphere(size * 0.5f),
				2 => Collider3D.Capsule(size * 0.3f, size),
				3 => Collider3D.Cylinder(size * 0.4f, size),
				_ => Collider3D.ConvexHull(hull),
			};
			collider.Friction = 0.3f + rand.NextSingle() * 0.6f;
			var velocity = new Vector3(rand.NextSingle() * 6 - 3, rand.NextSingle() * 4 - 2, rand.NextSingle() * 6 - 3);
			var body = RigidBody3D.Dynamic(mass: 0.5f + rand.NextSingle(), velocity) with { AngularDamping = rand.NextSingle() * 0.1f };
			entities.Add(world.Create(new Transform(position, rotation), collider, body));
		}

		// A chain from a kinematic anchor.
		var anchor = world.Create(new Transform(new Vector3(0, 9, 0)), Collider3D.Sphere(0.1f) with { Mask = 0 }, RigidBody3D.Kinematic());
		entities.Add(anchor);
		var previous = anchor;
		for (var i = 1; i <= 4; i++)
		{
			var link = world.Create(new Transform(new Vector3(i * 0.6f, 9, 0)), Collider3D.Capsule(0.1f, 0.4f) with { Mask = ~1u }, RigidBody3D.Dynamic());
			entities.Add(link);
			entities.Add(world.Create(Joint3D.BallSocket(previous, link, localAnchorA: i == 1 ? Vector3.Zero : new Vector3(0.3f, 0, 0), localAnchorB: new Vector3(-0.3f, 0, 0))));
			previous = link;
		}

		// A door on a hinge.
		var frame = world.Create(new Transform(new Vector3(-6, 2, -6)), Collider3D.Box(new Vector3(0.2f, 0.2f, 0.2f)) with { Mask = 0 }, RigidBody3D.Kinematic());
		var door = world.Create(new Transform(new Vector3(-5, 2, -6)), Collider3D.Box(new Vector3(2, 3, 0.1f)), RigidBody3D.Dynamic(mass: 3));
		entities.Add(frame);
		entities.Add(door);
		entities.Add(world.Create(Joint3D.Hinge(frame, door, Vector3.UnitY, localAnchorB: new Vector3(-1, 0, 0))));

		// A kinematic sweeper.
		sweeper = world.Create(new Transform(new Vector3(0, 0.5f, 0)), Collider3D.Box(new Vector3(8, 1, 0.5f)), RigidBody3D.Kinematic());
		entities.Add(sweeper);
		return entities;
	}

	/// <summary>Moves the sweeper as game code would before step <paramref name="step"/> (no trigonometry on the input side).</summary>
	public static void Drive(World world, Entity sweeper, int step)
	{
		ref var transform = ref world.Get<Transform>(sweeper);
		var phase = step % 600;
		var z = phase < 300 ? -8 + phase * (16f / 300) : 8 - (phase - 300) * (16f / 300);
		transform.Position = new Vector3(0, 0.5f, z);
	}

	/// <summary>The hash of every entity's transform bits in <paramref name="entities"/> order, then the world's state hash.</summary>
	public static ulong Hash(World world, List<Entity> entities, PhysicsWorld3D physics)
	{
		var hash = 14695981039346656037UL;
		foreach (var entity in entities)
		{
			if (!world.TryGet<Transform>(entity, out var transform)) continue;
			hash = Mix(hash, transform.Position.X);
			hash = Mix(hash, transform.Position.Y);
			hash = Mix(hash, transform.Position.Z);
			hash = Mix(hash, transform.Rotation.X);
			hash = Mix(hash, transform.Rotation.Y);
			hash = Mix(hash, transform.Rotation.Z);
			hash = Mix(hash, transform.Rotation.W);
		}

		var state = physics.ComputeStateHash();
		return Mix(Mix(hash, (uint)state), (uint)(state >> 32));
	}

	/// <summary>FNV-1a over the bytes of a float's bits.</summary>
	public static ulong Mix(ulong hash, float value) => Mix(hash, (uint)BitConverter.SingleToInt32Bits(value));

	/// <summary>FNV-1a over the bytes of <paramref name="value"/>.</summary>
	public static ulong Mix(ulong hash, uint value)
	{
		for (var i = 0; i < 4; i++)
		{
			hash ^= (byte)(value >> (8 * i));
			hash *= 1099511628211UL;
		}

		return hash;
	}
}
