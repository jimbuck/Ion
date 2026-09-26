using System.Numerics;

using Arch.Core;

namespace Ion.Extensions.Physics3D;

/// <summary>Whether a contact or overlap started or ended.</summary>
public enum ContactPhase3D : byte
{
	/// <summary>The colliders started touching (or the visitor entered the sensor).</summary>
	Begin,
	/// <summary>The colliders stopped touching (or the visitor left the sensor, or one of them was removed).</summary>
	End,
}

/// <summary>
/// Two colliders started or stopped touching during a fixed step (emitted on <see cref="IEvents"/> right after the step,
/// sorted so the order does not depend on threads), for colliders with <see cref="Collider3D.EnableEvents"/>.
/// </summary>
/// <param name="A">The entity of the first collider.</param>
/// <param name="B">The entity of the second collider.</param>
/// <param name="Phase">Whether the contact began or ended.</param>
/// <param name="Point">A contact point in world space (for <see cref="ContactPhase3D.Begin"/>; zero for an end).</param>
/// <param name="Normal">The contact normal pointing from A to B (for <see cref="ContactPhase3D.Begin"/>; zero for an end).</param>
public readonly record struct Collision3D(Entity A, Entity B, ContactPhase3D Phase, Vector3 Point, Vector3 Normal)
{
	/// <summary>Whether <paramref name="entity"/> is one of the two colliders.</summary>
	public bool Involves(Entity entity) => A == entity || B == entity;

	/// <summary>The other entity of the contact, seen from <paramref name="entity"/>.</summary>
	public Entity Other(Entity entity) => A == entity ? B : A;
}

/// <summary>A collider entered or left a sensor (<see cref="Collider3D.IsSensor"/>) during a fixed step.</summary>
/// <param name="Sensor">The sensor's entity.</param>
/// <param name="Visitor">The entity that entered or left it.</param>
/// <param name="Phase">Whether it entered or left.</param>
public readonly record struct Trigger3D(Entity Sensor, Entity Visitor, ContactPhase3D Phase);

/// <summary>The closest hit of a ray cast (<see cref="IPhysicsWorld3D.RayCast"/>).</summary>
/// <param name="Entity">The entity whose collider was hit.</param>
/// <param name="Point">The hit point.</param>
/// <param name="Normal">The surface normal at the hit point.</param>
/// <param name="Distance">The distance from the origin along the (normalized) direction.</param>
public readonly record struct RayHit3D(Entity Entity, Vector3 Point, Vector3 Normal, float Distance);

/// <summary>
/// The 3D physics world of a scope (the root world, or a scene's): queries, forces and impulses on the bodies of the ECS
/// entities it simulates, kept in step with their <see cref="Collider3D"/>, <see cref="RigidBody3D"/> and
/// <see cref="Joint3D"/> components by the physics step (<see cref="StageOrder.Physics"/> in FixedUpdate).
/// </summary>
/// <remarks>Queries see the state of the last fixed step and write into caller-provided spans: nothing allocates.</remarks>
public interface IPhysicsWorld3D
{
	/// <summary>The gravity in world units per second squared.</summary>
	Vector3 Gravity { get; set; }

	/// <summary>The number of bodies (static ones included).</summary>
	int BodyCount { get; }

	/// <summary>The number of joints.</summary>
	int JointCount { get; }

	/// <summary>The number of fixed steps simulated so far.</summary>
	long StepCount { get; }

	/// <summary>Whether the debug drawing is on (starts from <see cref="Physics3DConfig.DebugDraw"/>).</summary>
	bool DebugDraw { get; set; }

	/// <summary>
	/// Registers the convex hull of <paramref name="points"/> (for example a mesh's vertex positions) for
	/// <see cref="Collider3D.ConvexHull"/>. The hull lives as long as the world.
	/// </summary>
	ConvexHullId CreateConvexHull(ReadOnlySpan<Vector3> points);

	/// <summary>Casts a ray from <paramref name="origin"/> along <paramref name="direction"/> up to <paramref name="maxDistance"/>; returns the closest hit on a collider in <paramref name="mask"/>.</summary>
	bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit3D hit, uint mask = uint.MaxValue);

	/// <summary>Writes the entities whose bounding boxes overlap the box from <paramref name="min"/> to <paramref name="max"/>; returns how many (at most the span's length).</summary>
	int OverlapBox(Vector3 min, Vector3 max, Span<Entity> results, uint mask = uint.MaxValue);

	/// <summary>Writes the entities whose bounding boxes overlap the sphere; returns how many (at most the span's length).</summary>
	int OverlapSphere(Vector3 center, float radius, Span<Entity> results, uint mask = uint.MaxValue);

	/// <summary>Applies a linear impulse at the body's center (changes its velocity immediately). Returns false when the entity has no dynamic body yet.</summary>
	bool ApplyLinearImpulse(Entity entity, Vector3 impulse);

	/// <summary>Applies an angular impulse. Returns false when the entity has no dynamic body yet.</summary>
	bool ApplyAngularImpulse(Entity entity, Vector3 impulse);

	/// <summary>A hash of every body's pose and velocity bits in body creation order (the replay tests compare it).</summary>
	ulong ComputeStateHash();
}
