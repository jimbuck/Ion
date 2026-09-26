using System.Numerics;

using Arch.Core;

namespace Ion.Extensions.Physics3D;

/// <summary>How a body moves.</summary>
public enum RigidBodyType3D : byte
{
	/// <summary>Never moves by itself. Moving its <c>Transform</c> teleports it.</summary>
	Static,
	/// <summary>
	/// Moved by the game: every fixed step the body is driven towards its <c>Transform</c> with the velocity that gets it
	/// there in one step, so it pushes dynamic bodies out of the way. Infinite mass: not affected by contacts or forces.
	/// </summary>
	Kinematic,
	/// <summary>Moved by the simulation: gravity, contacts, joints, forces and impulses.</summary>
	Dynamic,
}

/// <summary>
/// A rigid body: the simulation moves the entity's <c>Transform</c> (the graphics abstractions' 3D transform: position
/// and rotation; the scale is left alone). Every entity with a <see cref="Collider3D"/> is a body; without a
/// <see cref="RigidBody3D"/> it is static.
/// </summary>
/// <remarks>
/// <para>Units are the transform's (meters in most 3D games): velocities in units per second, angular velocities in radians per second around each axis.</para>
/// <para>
/// The physics step writes the velocities back here after every fixed step; writing <see cref="LinearVelocity"/>,
/// <see cref="AngularVelocity"/> or any other field from game code is applied at the next fixed step.
/// </para>
/// <para>Create it with a constructor or a factory: <c>default(RigidBody3D)</c> has a zero <see cref="Mass"/> and <see cref="GravityScale"/>.</para>
/// </remarks>
public struct RigidBody3D : IEquatable<RigidBody3D>
{
	/// <summary>How the body moves.</summary>
	public RigidBodyType3D Type;

	/// <summary>The linear velocity in world units per second.</summary>
	public Vector3 LinearVelocity;

	/// <summary>The angular velocity in radians per second.</summary>
	public Vector3 AngularVelocity;

	/// <summary>The mass (1 by default); the inertia comes from it and the collider's shape.</summary>
	public float Mass;

	/// <summary>Linear damping: reduces the linear velocity over time (0 means none).</summary>
	public float LinearDamping;

	/// <summary>Angular damping: reduces the angular velocity over time (0 means none).</summary>
	public float AngularDamping;

	/// <summary>The multiplier of the world's gravity for this body (1 is normal, 0 floats).</summary>
	public float GravityScale;

	/// <summary>Prevents the body from rotating (infinite inertia).</summary>
	public bool FixedRotation;

	/// <summary>Continuous collision detection for small fast bodies, so they do not tunnel through thin objects.</summary>
	public bool IsBullet;

	/// <summary>A body of <paramref name="type"/> with a mass of 1 and a gravity scale of 1.</summary>
	public RigidBody3D(RigidBodyType3D type)
	{
		Type = type;
		Mass = 1f;
		GravityScale = 1f;
	}

	/// <summary>A dynamic body of <paramref name="mass"/> with <paramref name="velocity"/>.</summary>
	public static RigidBody3D Dynamic(float mass = 1f, Vector3 velocity = default) => new(RigidBodyType3D.Dynamic) { Mass = mass, LinearVelocity = velocity };

	/// <summary>A kinematic body (driven by its transform).</summary>
	public static RigidBody3D Kinematic() => new(RigidBodyType3D.Kinematic);

	/// <summary>A static body.</summary>
	public static RigidBody3D Static() => new(RigidBodyType3D.Static);

	/// <inheritdoc/>
	public readonly bool Equals(RigidBody3D other) =>
		Type == other.Type && LinearVelocity == other.LinearVelocity && AngularVelocity == other.AngularVelocity && Mass == other.Mass &&
		LinearDamping == other.LinearDamping && AngularDamping == other.AngularDamping && GravityScale == other.GravityScale &&
		FixedRotation == other.FixedRotation && IsBullet == other.IsBullet;

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is RigidBody3D other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode() => HashCode.Combine(Type, LinearVelocity, AngularVelocity, Mass, GravityScale);

	/// <summary>Whether two bodies are equal.</summary>
	public static bool operator ==(RigidBody3D left, RigidBody3D right) => left.Equals(right);

	/// <summary>Whether two bodies differ.</summary>
	public static bool operator !=(RigidBody3D left, RigidBody3D right) => !left.Equals(right);
}

/// <summary>The geometry of a <see cref="Collider3D"/>.</summary>
public enum ColliderShape3D : byte
{
	/// <summary>A box of <see cref="Collider3D.Size"/> (full width, height and depth).</summary>
	Box,
	/// <summary>A sphere of <see cref="Collider3D.Radius"/>.</summary>
	Sphere,
	/// <summary>A capsule along the local y axis: a cylinder of <see cref="Collider3D.Length"/> capped by half spheres of <see cref="Collider3D.Radius"/>.</summary>
	Capsule,
	/// <summary>A cylinder along the local y axis of <see cref="Collider3D.Radius"/> and <see cref="Collider3D.Length"/>.</summary>
	Cylinder,
	/// <summary>A convex hull created with <see cref="IPhysicsWorld3D.CreateConvexHull"/> (<see cref="Collider3D.Hull"/>), for example from a mesh's vertices.</summary>
	ConvexHull,
}

/// <summary>A convex hull registered with a physics world (<see cref="IPhysicsWorld3D.CreateConvexHull"/>).</summary>
/// <param name="Value">The world's hull index plus one (0 is no hull).</param>
public readonly record struct ConvexHullId(int Value)
{
	/// <summary>Whether this names a hull.</summary>
	public bool IsValid => Value > 0;
}

/// <summary>
/// The collision shape of an entity and its surface: the entity becomes a physics body (static unless it also has a
/// <see cref="RigidBody3D"/>), centered on the entity's <c>Transform</c> position and rotation (the transform's scale is
/// not applied).
/// </summary>
/// <remarks>
/// <para>Create one with a factory (<see cref="Box"/>, <see cref="Sphere"/>, <see cref="Capsule"/>, <see cref="Cylinder"/>, <see cref="ConvexHull"/>), which set the surface defaults.</para>
/// <para>
/// Changing any field after the body exists rebuilds it at the next fixed step. Two colliders collide when each one's
/// <see cref="Layer"/> is in the other's <see cref="Mask"/>; the queries filter by the same bits.
/// </para>
/// </remarks>
public struct Collider3D
{
	/// <summary>The geometry.</summary>
	public ColliderShape3D Shape;

	/// <summary>The full size of a <see cref="ColliderShape3D.Box"/>.</summary>
	public Vector3 Size;

	/// <summary>The radius of a sphere, capsule or cylinder.</summary>
	public float Radius;

	/// <summary>The length of a capsule's or cylinder's axis (along local y; a capsule's caps are added to it).</summary>
	public float Length;

	/// <summary>The hull of a <see cref="ColliderShape3D.ConvexHull"/>.</summary>
	public ConvexHullId Hull;

	/// <summary>The friction coefficient (usually 0 to 1).</summary>
	public float Friction;

	/// <summary>
	/// The bounciness from 0 to 1. The solver (BepuPhysics) has no restitution coefficient: this makes the contact spring
	/// softer and less damped and lets it push the bodies apart faster, which approximates a bounce.
	/// </summary>
	public float Restitution;

	/// <summary>A sensor detects overlaps (<see cref="Trigger3D"/> events) without colliding.</summary>
	public bool IsSensor;

	/// <summary>The collision layers this collider belongs to (a bit mask; bit 0 by default).</summary>
	public uint Layer;

	/// <summary>The layers this collider collides with (all by default).</summary>
	public uint Mask;

	/// <summary>Whether contacts of this collider raise <see cref="Collision3D"/> events and it can enter sensors (on by default).</summary>
	public bool EnableEvents;

	internal int BodySlot;

	private Collider3D(ColliderShape3D shape)
	{
		Shape = shape;
		Friction = 0.6f;
		Layer = 1;
		Mask = uint.MaxValue;
		EnableEvents = true;
	}

	/// <summary>A box of <paramref name="size"/> (full width, height and depth).</summary>
	public static Collider3D Box(Vector3 size) => new(ColliderShape3D.Box) { Size = size };

	/// <summary>A sphere of <paramref name="radius"/>.</summary>
	public static Collider3D Sphere(float radius) => new(ColliderShape3D.Sphere) { Radius = radius };

	/// <summary>A capsule along local y with <paramref name="radius"/> and a cylinder part of <paramref name="length"/>.</summary>
	public static Collider3D Capsule(float radius, float length) => new(ColliderShape3D.Capsule) { Radius = radius, Length = length };

	/// <summary>A cylinder along local y with <paramref name="radius"/> and <paramref name="length"/>.</summary>
	public static Collider3D Cylinder(float radius, float length) => new(ColliderShape3D.Cylinder) { Radius = radius, Length = length };

	/// <summary>A convex hull registered with <see cref="IPhysicsWorld3D.CreateConvexHull"/>.</summary>
	public static Collider3D ConvexHull(ConvexHullId hull) => new(ColliderShape3D.ConvexHull) { Hull = hull };

	/// <summary>Whether the physics world has created a body for this collider.</summary>
	public readonly bool HasBody => BodySlot != 0;

	internal static bool SameShape(in Collider3D a, in Collider3D b) =>
		a.Shape == b.Shape && a.Size == b.Size && a.Radius == b.Radius && a.Length == b.Length && a.Hull == b.Hull &&
		a.Friction == b.Friction && a.Restitution == b.Restitution && a.IsSensor == b.IsSensor && a.Layer == b.Layer &&
		a.Mask == b.Mask && a.EnableEvents == b.EnableEvents;
}

/// <summary>The kind of a <see cref="Joint3D"/>.</summary>
public enum JointType3D : byte
{
	/// <summary>The anchors stay together; the bodies rotate freely around them.</summary>
	BallSocket,
	/// <summary>The anchors stay together and the bodies rotate only around <see cref="Joint3D.Axis"/>.</summary>
	Hinge,
	/// <summary>The bodies keep their relative position and orientation.</summary>
	Weld,
	/// <summary>The anchors stay between <see cref="Joint3D.MinDistance"/> and <see cref="Joint3D.MaxDistance"/> apart.</summary>
	Distance,
}

/// <summary>
/// A joint between the bodies of two entities. Both need a <see cref="Collider3D"/> and a <see cref="RigidBody3D"/>
/// (BepuPhysics constrains bodies only: anchor a joint to the world with a kinematic body). Put it on any entity; it
/// exists while this component and both bodies do. Anchors and the axis are in each body's local space.
/// </summary>
/// <remarks>Changing any field after the joint exists recreates it at the next fixed step.</remarks>
public struct Joint3D
{
	/// <summary>The kind of joint.</summary>
	public JointType3D Type;

	/// <summary>The first body's entity.</summary>
	public Entity BodyA;

	/// <summary>The second body's entity.</summary>
	public Entity BodyB;

	/// <summary>The anchor on body A, in its local space.</summary>
	public Vector3 LocalAnchorA;

	/// <summary>The anchor on body B, in its local space.</summary>
	public Vector3 LocalAnchorB;

	/// <summary>The hinge axis, in body A's local space (body B's axis is the same direction at creation).</summary>
	public Vector3 Axis;

	/// <summary>The minimum anchor distance of a distance joint.</summary>
	public float MinDistance;

	/// <summary>The maximum anchor distance of a distance joint (0: the distance when the joint is created).</summary>
	public float MaxDistance;

	/// <summary>The constraint stiffness as a frequency in hertz (30 by default: rigid for a 60 Hz step).</summary>
	public float Frequency;

	/// <summary>The constraint damping ratio (1 by default).</summary>
	public float DampingRatio;

	internal int JointSlot;

	/// <summary>A joint of <paramref name="type"/> between <paramref name="bodyA"/> and <paramref name="bodyB"/>.</summary>
	public Joint3D(JointType3D type, Entity bodyA, Entity bodyB)
	{
		Type = type;
		BodyA = bodyA;
		BodyB = bodyB;
		Axis = Vector3.UnitY;
		Frequency = 30f;
		DampingRatio = 1f;
	}

	/// <summary>A ball socket at the anchors (which should be at the same world point when the joint is created).</summary>
	public static Joint3D BallSocket(Entity bodyA, Entity bodyB, Vector3 localAnchorA = default, Vector3 localAnchorB = default) =>
		new(JointType3D.BallSocket, bodyA, bodyB) { LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>A hinge around <paramref name="axis"/> (in body A's local space) at the anchors.</summary>
	public static Joint3D Hinge(Entity bodyA, Entity bodyB, Vector3 axis, Vector3 localAnchorA = default, Vector3 localAnchorB = default) =>
		new(JointType3D.Hinge, bodyA, bodyB) { Axis = axis, LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>A weld that keeps the bodies' current relative pose.</summary>
	public static Joint3D Weld(Entity bodyA, Entity bodyB) => new(JointType3D.Weld, bodyA, bodyB);

	/// <summary>A rope or rod: the anchors stay between <paramref name="minDistance"/> and <paramref name="maxDistance"/> apart (0: the current distance).</summary>
	public static Joint3D Distance(Entity bodyA, Entity bodyB, float minDistance = 0f, float maxDistance = 0f, Vector3 localAnchorA = default, Vector3 localAnchorB = default) =>
		new(JointType3D.Distance, bodyA, bodyB) { MinDistance = minDistance, MaxDistance = maxDistance, LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>Whether the physics world has created this joint.</summary>
	public readonly bool IsCreated => JointSlot != 0;

	internal static bool SameDefinition(in Joint3D a, in Joint3D b) =>
		a.Type == b.Type && a.BodyA == b.BodyA && a.BodyB == b.BodyB && a.LocalAnchorA == b.LocalAnchorA && a.LocalAnchorB == b.LocalAnchorB &&
		a.Axis == b.Axis && a.MinDistance == b.MinDistance && a.MaxDistance == b.MaxDistance && a.Frequency == b.Frequency && a.DampingRatio == b.DampingRatio;
}
