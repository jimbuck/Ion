using System.Numerics;

using Arch.Core;

namespace Ion.Extensions.Physics2D;

/// <summary>Whether a contact or overlap started or ended.</summary>
public enum ContactPhase : byte
{
	/// <summary>The shapes started touching (or the visitor entered the sensor).</summary>
	Begin,
	/// <summary>The shapes stopped touching (or the visitor left the sensor, or one of them was destroyed).</summary>
	End,
}

/// <summary>
/// Two colliders started or stopped touching during a fixed step (emitted on <see cref="IEvents"/> right after the step,
/// for colliders with <see cref="Collider2D.EnableEvents"/>). Each contact is reported once, in an order that depends only
/// on the simulation (deterministic).
/// </summary>
/// <param name="A">The entity of the first collider.</param>
/// <param name="B">The entity of the second collider.</param>
/// <param name="Phase">Whether the contact began or ended.</param>
/// <param name="Point">A contact point in world units (for <see cref="ContactPhase.Begin"/>; zero for an end).</param>
/// <param name="Normal">The contact normal, pointing from A to B (for <see cref="ContactPhase.Begin"/>; zero for an end).</param>
public readonly record struct Collision2D(Entity A, Entity B, ContactPhase Phase, Vector2 Point, Vector2 Normal)
{
	/// <summary>Whether <paramref name="entity"/> is one of the two colliders.</summary>
	public bool Involves(Entity entity) => A == entity || B == entity;

	/// <summary>The other entity of the contact, seen from <paramref name="entity"/> (which must be <see cref="A"/> or <see cref="B"/>).</summary>
	public Entity Other(Entity entity) => A == entity ? B : A;
}

/// <summary>
/// A collider entered or left a sensor (<see cref="Collider2D.IsSensor"/>) during a fixed step, emitted on
/// <see cref="IEvents"/> right after the step.
/// </summary>
/// <param name="Sensor">The sensor's entity.</param>
/// <param name="Visitor">The entity that entered or left it.</param>
/// <param name="Phase">Whether it entered or left.</param>
public readonly record struct Trigger2D(Entity Sensor, Entity Visitor, ContactPhase Phase);

/// <summary>The closest hit of a ray cast (<see cref="IPhysicsWorld2D.RayCast"/>).</summary>
/// <param name="Entity">The entity whose collider was hit.</param>
/// <param name="Point">The hit point in world units.</param>
/// <param name="Normal">The surface normal at the hit point.</param>
/// <param name="Fraction">Where along the ray the hit is, from 0 (the origin) to 1 (origin + translation).</param>
public readonly record struct RayHit2D(Entity Entity, Vector2 Point, Vector2 Normal, float Fraction);
