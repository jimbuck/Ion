using System.Numerics;

using Arch.Core;

using Ion.Extensions.Ecs;

namespace Ion.Extensions.Physics2D.Tests;

/// <summary>
/// The 2D replay scene: a seeded mix of 300 boxes, circles, capsules and polygons thrown into a closed box, a chain of
/// five links on revolute joints, a slider on a prismatic joint and a kinematic paddle driven by the step number, run for
/// a number of fixed steps of 1/60 s through <see cref="PhysicsWorld2D.Step"/> (what the FixedUpdate step calls). The
/// result is a hash of every entity's <c>Transform2D</c> bits (in creation order) combined with the world's own state
/// hash. Shared by the replay tests and the arm64 replay runner (docs/plans/benchmarks/2026-09-physics2d/replay).
/// </summary>
public static class Replay2D
{
	/// <summary>The fixed step.</summary>
	public const float Dt = 1f / 60f;

	/// <summary>Builds the scene with <paramref name="seed"/>, runs <paramref name="steps"/> steps and returns the hash.</summary>
	public static ulong Run(int steps, int seed = 2026)
	{
		using var world = World.Create();
		using var physics = new PhysicsWorld2D(world, events: null, new Physics2DConfig { GravityY = 9.81f });
		var entities = Build(world, seed, out var paddle);

		for (var step = 0; step < steps; step++)
		{
			Drive(world, paddle, step);
			physics.Step(Dt);
		}

		return Hash(world, entities, physics);
	}

	/// <summary>Creates the scene's entities; returns them in creation order.</summary>
	public static List<Entity> Build(World world, int seed, out Entity paddle)
	{
		var rand = new Random(seed);
		var entities = new List<Entity>();

		// A closed 40 x 30 box.
		entities.Add(world.Create(new Transform2D(new Vector2(0, 15)), Collider2D.Box(new Vector2(42, 1))));
		entities.Add(world.Create(new Transform2D(new Vector2(0, -15)), Collider2D.Box(new Vector2(42, 1))));
		entities.Add(world.Create(new Transform2D(new Vector2(-20.5f, 0)), Collider2D.Box(new Vector2(1, 30))));
		entities.Add(world.Create(new Transform2D(new Vector2(20.5f, 0)), Collider2D.Box(new Vector2(1, 30))));

		for (var i = 0; i < 300; i++)
		{
			var position = new Vector2(rand.NextSingle() * 36 - 18, rand.NextSingle() * 20 - 13);
			var rotation = rand.NextSingle() * MathF.Tau;
			var size = 0.3f + rand.NextSingle() * 0.5f;
			var collider = (i % 4) switch
			{
				0 => Collider2D.Box(new Vector2(size, size * 0.7f)),
				1 => Collider2D.Circle(size * 0.5f),
				2 => Collider2D.Capsule(new Vector2(size * 1.5f, size * 0.6f)),
				_ => Collider2D.Polygon([new(-size * 0.5f, size * 0.4f), new(size * 0.5f, size * 0.4f), new(0, -size * 0.5f)]),
			};
			collider.Restitution = rand.NextSingle() * 0.6f;
			collider.Friction = 0.2f + rand.NextSingle() * 0.6f;
			var velocity = new Vector2(rand.NextSingle() * 10 - 5, rand.NextSingle() * 10 - 5);
			var body = RigidBody2D.Dynamic(velocity) with { AngularVelocity = rand.NextSingle() * 4 - 2, LinearDamping = rand.NextSingle() * 0.1f };
			entities.Add(world.Create(new Transform2D(position, rotation), collider, body));
		}

		// A chain hanging from the ceiling.
		var anchor = world.Create(new Transform2D(new Vector2(0, -14)), Collider2D.Box(new Vector2(0.4f, 0.4f)) with { Mask = 0 });
		entities.Add(anchor);
		var previous = anchor;
		for (var i = 1; i <= 5; i++)
		{
			var link = world.Create(new Transform2D(new Vector2(i * 1.0f, -14)), Collider2D.Capsule(new Vector2(-0.4f, 0), new Vector2(0.4f, 0), 0.15f), RigidBody2D.Dynamic());
			entities.Add(link);
			entities.Add(world.Create(Joint2D.Revolute(previous, link, localAnchorA: i == 1 ? Vector2.Zero : new Vector2(0.5f, 0), localAnchorB: new Vector2(-0.5f, 0))));
			previous = link;
		}

		// A slider on the floor.
		var slider = world.Create(new Transform2D(new Vector2(-10, 13)), Collider2D.Box(new Vector2(2, 0.5f)), RigidBody2D.Dynamic());
		entities.Add(slider);
		entities.Add(world.Create(Joint2D.Prismatic(entities[0], slider, Vector2.UnitX, localAnchorA: new Vector2(-10, -2)) with { EnableLimit = true, Lower = -5, Upper = 5 }));

		// A kinematic paddle sweeping across the middle.
		paddle = world.Create(new Transform2D(new Vector2(0, 5)), Collider2D.Capsule(new Vector2(6, 0.8f)), RigidBody2D.Kinematic());
		entities.Add(paddle);
		return entities;
	}

	/// <summary>Moves the paddle as game code would before step <paramref name="step"/>.</summary>
	public static void Drive(World world, Entity paddle, int step)
	{
		ref var transform = ref world.Get<Transform2D>(paddle);
		// A triangle wave over 480 steps between x = -12 and 12, and a slow rotation: no trigonometry on the input side.
		var phase = step % 480;
		var x = phase < 240 ? -12 + phase * 0.1f : 12 - (phase - 240) * 0.1f;
		transform.Position = new Vector2(x, 5);
		transform.Rotation = (step % 720) * (MathF.PI / 360f);
	}

	/// <summary>The hash of every entity's transform bits in <paramref name="entities"/> order, then the world's state hash.</summary>
	public static ulong Hash(World world, List<Entity> entities, PhysicsWorld2D physics)
	{
		var hash = 14695981039346656037UL;
		foreach (var entity in entities)
		{
			if (!world.TryGet<Transform2D>(entity, out var transform)) continue;
			hash = Mix(hash, transform.Position.X);
			hash = Mix(hash, transform.Position.Y);
			hash = Mix(hash, transform.Rotation);
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
