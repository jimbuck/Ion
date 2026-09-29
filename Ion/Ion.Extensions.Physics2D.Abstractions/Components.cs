using System.Numerics;
using System.Runtime.CompilerServices;

using Arch.Core;

namespace Ion.Extensions.Physics2D;

/// <summary>How a body moves.</summary>
public enum RigidBodyType2D : byte
{
	/// <summary>Never moves by itself (walls, floors). Moving its <c>Transform2D</c> teleports it.</summary>
	Static,
	/// <summary>
	/// Moved by the game: every fixed step the body is driven towards its <c>Transform2D</c> with the velocity that gets it
	/// there in one step, so it pushes dynamic bodies out of the way (paddles, moving platforms). Not affected by forces.
	/// </summary>
	Kinematic,
	/// <summary>Moved by the simulation: gravity, contacts, joints, forces and impulses.</summary>
	Dynamic,
}

/// <summary>
/// A rigid body: the simulation moves the entity's <c>Transform2D</c> (position and rotation; the scale is left alone).
/// Every entity with a <see cref="Collider2D"/> is a body; without a <see cref="RigidBody2D"/> it is static.
/// </summary>
/// <remarks>
/// <para>
/// Units are the <c>Transform2D</c>'s ("world units", pixels in most 2D games): velocities in units per second, angles in
/// radians (clockwise on screen, like <c>Transform2D.Rotation</c>). <see cref="Physics2DConfig.UnitsPerMeter"/> only tells
/// the solver how large a meter is, so its tolerances fit the game's scale.
/// </para>
/// <para>
/// The physics step writes the body's velocities back here after every fixed step. Writing <see cref="LinearVelocity"/>
/// or <see cref="AngularVelocity"/> (or any other field) from game code is applied at the next fixed step.
/// </para>
/// <para>Create it with a constructor or a factory: <c>default(RigidBody2D)</c> has a zero <see cref="GravityScale"/>.</para>
/// </remarks>
public struct RigidBody2D : IEquatable<RigidBody2D>
{
	/// <summary>How the body moves.</summary>
	public RigidBodyType2D Type;

	/// <summary>The linear velocity in world units per second.</summary>
	public Vector2 LinearVelocity;

	/// <summary>The angular velocity in radians per second.</summary>
	public float AngularVelocity;

	/// <summary>
	/// The mass in kilograms. Zero (the default) computes it from the collider's <see cref="Collider2D.Density"/> and area
	/// (in meters, see <see cref="Physics2DConfig.UnitsPerMeter"/>); a positive value overrides it (the rotational inertia
	/// is scaled with it).
	/// </summary>
	public float Mass;

	/// <summary>Linear damping: reduces the linear velocity over time (0 means none).</summary>
	public float LinearDamping;

	/// <summary>Angular damping: reduces the angular velocity over time (0 means none).</summary>
	public float AngularDamping;

	/// <summary>The multiplier of the world's gravity for this body (1 is normal, 0 floats).</summary>
	public float GravityScale;

	/// <summary>Prevents the body from rotating.</summary>
	public bool FixedRotation;

	/// <summary>
	/// Treats the body as a fast "bullet": continuous collision against other dynamic bodies too (static ones always use
	/// it), so it does not tunnel through thin moving objects. Costs more; use it for small fast bodies such as balls.
	/// </summary>
	public bool IsBullet;

	/// <summary>A body of <paramref name="type"/> with a gravity scale of 1.</summary>
	public RigidBody2D(RigidBodyType2D type)
	{
		Type = type;
		GravityScale = 1f;
	}

	/// <summary>A dynamic body with <paramref name="velocity"/> (zero when omitted).</summary>
	public static RigidBody2D Dynamic(Vector2 velocity = default) => new(RigidBodyType2D.Dynamic) { LinearVelocity = velocity };

	/// <summary>A kinematic body (driven by its transform).</summary>
	public static RigidBody2D Kinematic() => new(RigidBodyType2D.Kinematic);

	/// <summary>A static body.</summary>
	public static RigidBody2D Static() => new(RigidBodyType2D.Static);

	/// <inheritdoc/>
	public readonly bool Equals(RigidBody2D other) =>
		Type == other.Type && LinearVelocity == other.LinearVelocity && AngularVelocity == other.AngularVelocity && Mass == other.Mass &&
		LinearDamping == other.LinearDamping && AngularDamping == other.AngularDamping && GravityScale == other.GravityScale &&
		FixedRotation == other.FixedRotation && IsBullet == other.IsBullet;

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is RigidBody2D other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode() => HashCode.Combine(Type, LinearVelocity, AngularVelocity, Mass, GravityScale);

	/// <summary>Whether two bodies are equal.</summary>
	public static bool operator ==(RigidBody2D left, RigidBody2D right) => left.Equals(right);

	/// <summary>Whether two bodies differ.</summary>
	public static bool operator !=(RigidBody2D left, RigidBody2D right) => !left.Equals(right);
}

/// <summary>The geometry of a <see cref="Collider2D"/>.</summary>
public enum ColliderShape2D : byte
{
	/// <summary>A rectangle of <see cref="Collider2D.Size"/>, optionally with rounded corners (<see cref="Collider2D.Radius"/>).</summary>
	Box,
	/// <summary>A circle of <see cref="Collider2D.Radius"/>.</summary>
	Circle,
	/// <summary>
	/// A capsule: the segment from <see cref="Collider2D.PointA"/> to <see cref="Collider2D.PointB"/> inflated by
	/// <see cref="Collider2D.Radius"/> (a rectangle with two half circles, such as a paddle).
	/// </summary>
	Capsule,
	/// <summary>The convex hull of up to <see cref="Polygon2D.MaxVertices"/> <see cref="Collider2D.Vertices"/>, optionally rounded.</summary>
	Polygon,
}

/// <summary>Up to eight polygon vertices stored inline (the component stays unmanaged).</summary>
[InlineArray(MaxVertices)]
public struct Polygon2D
{
	/// <summary>The maximum number of vertices of a polygon collider (Box2D's limit).</summary>
	public const int MaxVertices = 8;

	private Vector2 _element0;
}

/// <summary>
/// The collision shape of an entity and its surface: the entity becomes a physics body (static unless it also has a
/// <see cref="RigidBody2D"/>). Geometry is in the entity's local space and world units, at the entity's
/// <c>Transform2D</c> position and rotation (the transform's scale is not applied).
/// </summary>
/// <remarks>
/// <para>Create one with a factory (<see cref="Box"/>, <see cref="Circle"/>, <see cref="Capsule(Vector2, Vector2, float)"/>, <see cref="Polygon"/>), which set the surface defaults.</para>
/// <para>
/// Changing any field after the body exists rebuilds its shape at the next fixed step. Collision filtering: two colliders
/// collide when each one's <see cref="Layer"/> is in the other's <see cref="Mask"/>. The ray cast and overlap queries
/// filter by the same bits.
/// </para>
/// </remarks>
public struct Collider2D
{
	/// <summary>The geometry.</summary>
	public ColliderShape2D Shape;

	/// <summary>The full width and height of a <see cref="ColliderShape2D.Box"/>.</summary>
	public Vector2 Size;

	/// <summary>
	/// The radius of a <see cref="ColliderShape2D.Circle"/> or <see cref="ColliderShape2D.Capsule"/>; for a box or a
	/// polygon, the rounding radius added around it (0 for sharp corners).
	/// </summary>
	public float Radius;

	/// <summary>The center of the shape in the entity's local space (circles, boxes).</summary>
	public Vector2 Offset;

	/// <summary>The rotation of a box in the entity's local space, in radians.</summary>
	public float Angle;

	/// <summary>The first end of a capsule's segment, in local space.</summary>
	public Vector2 PointA;

	/// <summary>The second end of a capsule's segment, in local space.</summary>
	public Vector2 PointB;

	/// <summary>The vertices of a polygon, in local space (the first <see cref="VertexCount"/> are used).</summary>
	public Polygon2D Vertices;

	/// <summary>The number of polygon vertices (3 to <see cref="Polygon2D.MaxVertices"/>).</summary>
	public int VertexCount;

	/// <summary>The density in kilograms per square meter (the mass of dynamic bodies comes from it).</summary>
	public float Density;

	/// <summary>The friction coefficient (usually 0 to 1).</summary>
	public float Friction;

	/// <summary>The restitution (bounciness): 0 does not bounce, 1 bounces back at the same speed.</summary>
	public float Restitution;

	/// <summary>
	/// A sensor detects overlaps (<see cref="Trigger2D"/> events) without colliding. Sensors see dynamic and kinematic
	/// bodies, not static ones.
	/// </summary>
	public bool IsSensor;

	/// <summary>The collision layers this collider belongs to (a bit mask; bit 0 by default).</summary>
	public uint Layer;

	/// <summary>The layers this collider collides with (all by default).</summary>
	public uint Mask;

	/// <summary>Whether contacts of this collider raise <see cref="Collision2D"/> events and it can enter sensors (on by default).</summary>
	public bool EnableEvents;

	// The body slot in the physics world (0: none yet), maintained by the physics step. Copying a collider to another
	// entity copies it too; the world notices that the slot belongs to another entity and creates a new body.
	internal int BodySlot;

	private Collider2D(ColliderShape2D shape)
	{
		Shape = shape;
		Density = 1f;
		Friction = 0.6f;
		Layer = 1;
		Mask = uint.MaxValue;
		EnableEvents = true;
	}

	/// <summary>A box of <paramref name="size"/> (full width and height) centered on <paramref name="offset"/>, rotated by <paramref name="angle"/>.</summary>
	public static Collider2D Box(Vector2 size, Vector2 offset = default, float angle = 0f) => new(ColliderShape2D.Box) { Size = size, Offset = offset, Angle = angle };

	/// <summary>A circle of <paramref name="radius"/> centered on <paramref name="offset"/>.</summary>
	public static Collider2D Circle(float radius, Vector2 offset = default) => new(ColliderShape2D.Circle) { Radius = radius, Offset = offset };

	/// <summary>A capsule around the segment from <paramref name="pointA"/> to <paramref name="pointB"/> with <paramref name="radius"/>.</summary>
	public static Collider2D Capsule(Vector2 pointA, Vector2 pointB, float radius) => new(ColliderShape2D.Capsule) { PointA = pointA, PointB = pointB, Radius = radius };

	/// <summary>
	/// A horizontal capsule filling a <paramref name="size"/> rectangle with round ends (a pill: the height is the diameter),
	/// or a vertical one when the height is the larger side.
	/// </summary>
	public static Collider2D Capsule(Vector2 size)
	{
		if (size.X >= size.Y)
		{
			var half = (size.X - size.Y) / 2f;
			return Capsule(new Vector2(-half, 0), new Vector2(half, 0), size.Y / 2f);
		}

		var halfY = (size.Y - size.X) / 2f;
		return Capsule(new Vector2(0, -halfY), new Vector2(0, halfY), size.X / 2f);
	}

	/// <summary>The convex hull of <paramref name="vertices"/> (3 to <see cref="Polygon2D.MaxVertices"/> points in local space).</summary>
	public static Collider2D Polygon(ReadOnlySpan<Vector2> vertices, float radius = 0f)
	{
		if (vertices.Length is < 3 or > Polygon2D.MaxVertices) throw new ArgumentOutOfRangeException(nameof(vertices), vertices.Length, $"A polygon collider needs 3 to {Polygon2D.MaxVertices} vertices.");
		var collider = new Collider2D(ColliderShape2D.Polygon) { Radius = radius, VertexCount = vertices.Length };
		vertices.CopyTo(collider.Vertices);
		return collider;
	}

	/// <summary>The polygon's vertices (the first <see cref="VertexCount"/>).</summary>
	[System.Diagnostics.CodeAnalysis.UnscopedRef]
	public readonly ReadOnlySpan<Vector2> GetVertices() => ((ReadOnlySpan<Vector2>)Vertices)[..Math.Clamp(VertexCount, 0, Polygon2D.MaxVertices)];

	/// <summary>Whether the physics world has created a body for this collider.</summary>
	public readonly bool HasBody => BodySlot != 0;

	// Compares the fields that define the shape and its surface (not the slot).
	internal static bool SameShape(in Collider2D a, in Collider2D b)
	{
		if (a.Shape != b.Shape || a.Size != b.Size || a.Radius != b.Radius || a.Offset != b.Offset || a.Angle != b.Angle ||
			a.PointA != b.PointA || a.PointB != b.PointB || a.VertexCount != b.VertexCount || a.Density != b.Density ||
			a.Friction != b.Friction || a.Restitution != b.Restitution || a.IsSensor != b.IsSensor || a.Layer != b.Layer ||
			a.Mask != b.Mask || a.EnableEvents != b.EnableEvents)
		{
			return false;
		}

		return a.Shape != ColliderShape2D.Polygon || a.GetVertices().SequenceEqual(b.GetVertices());
	}
}

/// <summary>The kind of a <see cref="Joint2D"/>.</summary>
public enum JointType2D : byte
{
	/// <summary>Keeps the anchors at <see cref="Joint2D.Length"/> (a rod), or within a range, or as a spring.</summary>
	Distance,
	/// <summary>A hinge: the bodies share the anchor point and rotate freely around it (optionally limited or motorized).</summary>
	Revolute,
	/// <summary>A slider: body B moves along <see cref="Joint2D.Axis"/> of body A without rotating relative to it.</summary>
	Prismatic,
	/// <summary>Glues the two bodies together (optionally soft).</summary>
	Weld,
}

/// <summary>
/// A joint between the bodies of two entities (each with a <see cref="Collider2D"/>). Put it on any entity, typically a
/// third one or body B; the joint exists while this component and both bodies do. Anchors are in each body's local space
/// and world units.
/// </summary>
/// <remarks>Changing any field after the joint exists recreates it at the next fixed step.</remarks>
public struct Joint2D
{
	/// <summary>The kind of joint.</summary>
	public JointType2D Type;

	/// <summary>The first body's entity.</summary>
	public Entity BodyA;

	/// <summary>The second body's entity.</summary>
	public Entity BodyB;

	/// <summary>The anchor on body A, in its local space.</summary>
	public Vector2 LocalAnchorA;

	/// <summary>The anchor on body B, in its local space.</summary>
	public Vector2 LocalAnchorB;

	/// <summary>The sliding axis of a prismatic joint in body A's local space (normalized when the joint is created).</summary>
	public Vector2 Axis;

	/// <summary>The rest length of a distance joint (0: the distance between the anchors when the joint is created).</summary>
	public float Length;

	/// <summary>Enables the limit: <see cref="Lower"/> and <see cref="Upper"/> (distance range, angle range in radians, or translation range).</summary>
	public bool EnableLimit;

	/// <summary>The lower limit.</summary>
	public float Lower;

	/// <summary>The upper limit.</summary>
	public float Upper;

	/// <summary>Enables the motor.</summary>
	public bool EnableMotor;

	/// <summary>The motor speed (radians per second for a revolute joint, world units per second otherwise).</summary>
	public float MotorSpeed;

	/// <summary>The maximum motor force (a torque for a revolute joint).</summary>
	public float MaxMotorForce;

	/// <summary>Makes a distance, revolute or prismatic joint springy (and a weld joint soft) with <see cref="Hertz"/> and <see cref="DampingRatio"/>.</summary>
	public bool EnableSpring;

	/// <summary>The spring stiffness as a frequency in hertz.</summary>
	public float Hertz;

	/// <summary>The spring damping ratio (1 is critical damping).</summary>
	public float DampingRatio;

	/// <summary>Whether the two bodies still collide with each other.</summary>
	public bool CollideConnected;

	internal int JointSlot;

	/// <summary>A joint of <paramref name="type"/> between <paramref name="bodyA"/> and <paramref name="bodyB"/>.</summary>
	public Joint2D(JointType2D type, Entity bodyA, Entity bodyB)
	{
		Type = type;
		BodyA = bodyA;
		BodyB = bodyB;
		Axis = Vector2.UnitX;
		DampingRatio = 1f;
	}

	/// <summary>A distance joint (a rod of the current length, or <paramref name="length"/>) between the anchors.</summary>
	public static Joint2D Distance(Entity bodyA, Entity bodyB, Vector2 localAnchorA = default, Vector2 localAnchorB = default, float length = 0f) =>
		new(JointType2D.Distance, bodyA, bodyB) { LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB, Length = length };

	/// <summary>A hinge at the anchors (which should be at the same world point when the joint is created).</summary>
	public static Joint2D Revolute(Entity bodyA, Entity bodyB, Vector2 localAnchorA = default, Vector2 localAnchorB = default) =>
		new(JointType2D.Revolute, bodyA, bodyB) { LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>A slider along <paramref name="axis"/> (in body A's local space).</summary>
	public static Joint2D Prismatic(Entity bodyA, Entity bodyB, Vector2 axis, Vector2 localAnchorA = default, Vector2 localAnchorB = default) =>
		new(JointType2D.Prismatic, bodyA, bodyB) { Axis = axis, LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>A weld at the anchors.</summary>
	public static Joint2D Weld(Entity bodyA, Entity bodyB, Vector2 localAnchorA = default, Vector2 localAnchorB = default) =>
		new(JointType2D.Weld, bodyA, bodyB) { LocalAnchorA = localAnchorA, LocalAnchorB = localAnchorB };

	/// <summary>Whether the physics world has created this joint.</summary>
	public readonly bool IsCreated => JointSlot != 0;

	internal static bool SameDefinition(in Joint2D a, in Joint2D b) =>
		a.Type == b.Type && a.BodyA == b.BodyA && a.BodyB == b.BodyB && a.LocalAnchorA == b.LocalAnchorA && a.LocalAnchorB == b.LocalAnchorB &&
		a.Axis == b.Axis && a.Length == b.Length && a.EnableLimit == b.EnableLimit && a.Lower == b.Lower && a.Upper == b.Upper &&
		a.EnableMotor == b.EnableMotor && a.MotorSpeed == b.MotorSpeed && a.MaxMotorForce == b.MaxMotorForce && a.EnableSpring == b.EnableSpring &&
		a.Hertz == b.Hertz && a.DampingRatio == b.DampingRatio && a.CollideConnected == b.CollideConnected;
}
