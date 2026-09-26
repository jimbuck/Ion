using System.Numerics;

using Arch.Core;

namespace Ion.Extensions.Physics2D;

/// <summary>
/// The 2D physics world of a scope (the root world, or a scene's): queries, forces and impulses on the bodies of the ECS
/// entities it simulates. The physics step (<see cref="StageOrder.Physics"/> in FixedUpdate) keeps it in step with the
/// entities' <see cref="Collider2D"/>, <see cref="RigidBody2D"/> and <see cref="Joint2D"/> components.
/// </summary>
/// <remarks>
/// Everything is in world units (the <c>Transform2D</c>'s). Queries see the state of the last fixed step. Layer masks
/// select colliders whose <see cref="Collider2D.Layer"/> intersects them. Query results are written into caller-provided
/// spans: nothing allocates.
/// </remarks>
public interface IPhysicsWorld2D
{
	/// <summary>The gravity in world units per second squared (y points down on screen).</summary>
	Vector2 Gravity { get; set; }

	/// <summary>The number of bodies.</summary>
	int BodyCount { get; }

	/// <summary>The number of joints.</summary>
	int JointCount { get; }

	/// <summary>The number of fixed steps simulated so far.</summary>
	long StepCount { get; }

	/// <summary>Whether the debug drawing is on (starts from <see cref="Physics2DConfig.DebugDraw"/>).</summary>
	bool DebugDraw { get; set; }

	/// <summary>
	/// Casts a ray from <paramref name="origin"/> along <paramref name="translation"/> and returns the closest hit on a
	/// collider in <paramref name="mask"/> (sensors are skipped).
	/// </summary>
	bool RayCast(Vector2 origin, Vector2 translation, out RayHit2D hit, uint mask = uint.MaxValue);

	/// <summary>Writes the entities whose colliders overlap the box from <paramref name="min"/> to <paramref name="max"/> into <paramref name="results"/>; returns how many were found (at most its length).</summary>
	int OverlapBox(Vector2 min, Vector2 max, Span<Entity> results, uint mask = uint.MaxValue);

	/// <summary>Writes the entities whose colliders overlap the circle into <paramref name="results"/>; returns how many were found (at most its length).</summary>
	int OverlapCircle(Vector2 center, float radius, Span<Entity> results, uint mask = uint.MaxValue);

	/// <summary>Writes the entities whose colliders contain <paramref name="point"/> into <paramref name="results"/>; returns how many were found (at most its length).</summary>
	int OverlapPoint(Vector2 point, Span<Entity> results, uint mask = uint.MaxValue);

	/// <summary>Applies a force (world units, newtons scaled by <see cref="Physics2DConfig.UnitsPerMeter"/>) at the body's center of mass until the next step. Returns false when the entity has no body yet.</summary>
	bool ApplyForce(Entity entity, Vector2 force);

	/// <summary>Applies a linear impulse at the body's center of mass (changes its velocity immediately). Returns false when the entity has no body yet.</summary>
	bool ApplyLinearImpulse(Entity entity, Vector2 impulse);

	/// <summary>Applies a torque until the next step. Returns false when the entity has no body yet.</summary>
	bool ApplyTorque(Entity entity, float torque);

	/// <summary>Applies an angular impulse. Returns false when the entity has no body yet.</summary>
	bool ApplyAngularImpulse(Entity entity, float impulse);

	/// <summary>
	/// A hash of every body's position, rotation and velocities, in body creation order, bit for bit: two runs of the same
	/// scene with the same inputs produce the same hash (the replay tests compare it).
	/// </summary>
	ulong ComputeStateHash();
}
