using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// The local 2D transform of an entity: position, rotation (radians) and scale, relative to its <see cref="Parent"/> (or
/// to the world for a root). The transform propagation system writes the world transform into
/// <see cref="GlobalTransform2D"/>, which it adds to every entity with a <see cref="Transform2D"/>.
/// </summary>
/// <remarks>Create it with a constructor: <c>default(Transform2D)</c> has a zero scale.</remarks>
public record struct Transform2D
{
	/// <summary>The position.</summary>
	public Vector2 Position;

	/// <summary>The rotation in radians (clockwise on screen, where y points down).</summary>
	public float Rotation;

	/// <summary>The scale (1 is the sprite's own size).</summary>
	public Vector2 Scale;

	/// <summary>The identity transform: at the origin, not rotated, scale 1.</summary>
	public Transform2D() => Scale = Vector2.One;

	/// <summary>A transform at <paramref name="position"/>, rotated by <paramref name="rotation"/> radians, with scale 1.</summary>
	public Transform2D(Vector2 position, float rotation = 0f)
	{
		Position = position;
		Rotation = rotation;
		Scale = Vector2.One;
	}

	/// <summary>A transform at <paramref name="position"/>, rotated by <paramref name="rotation"/> radians, scaled by <paramref name="scale"/>.</summary>
	public Transform2D(Vector2 position, float rotation, Vector2 scale)
	{
		Position = position;
		Rotation = rotation;
		Scale = scale;
	}

	/// <summary>The identity transform.</summary>
	public static Transform2D Identity => new();

	/// <summary>The matrix of this transform: scale, then rotation, then translation (row vectors, as <see cref="System.Numerics"/>).</summary>
	public readonly Matrix3x2 ToMatrix()
	{
		var (sin, cos) = MathF.SinCos(Rotation);
		return new Matrix3x2(
			Scale.X * cos, Scale.X * sin,
			-Scale.Y * sin, Scale.Y * cos,
			Position.X, Position.Y);
	}
}

/// <summary>
/// The world transform of an entity with a <see cref="Transform2D"/>, written by the transform propagation system (see
/// <c>StageOrder.TransformPropagation</c>): <see cref="Matrix"/> is the exact world matrix (the local matrix times the
/// parent's), and <see cref="Position"/>, <see cref="Rotation"/> and <see cref="Scale"/> its decomposition, which the sprite
/// extraction draws from.
/// </summary>
/// <remarks>
/// The decomposition composes rotations by addition and scales by multiplication, so under a parent with a non-uniform
/// scale and a rotated child it ignores the resulting shear (the matrix keeps it). For a root entity it is exactly the
/// entity's <see cref="Transform2D"/>.
/// </remarks>
public struct GlobalTransform2D
{
	/// <summary>The world matrix (row vectors).</summary>
	public Matrix3x2 Matrix { readonly get; internal set; }

	/// <summary>The world position (the matrix translation).</summary>
	public Vector2 Position { readonly get; internal set; }

	/// <summary>The world rotation in radians.</summary>
	public float Rotation { readonly get; internal set; }

	/// <summary>The world scale.</summary>
	public Vector2 Scale { readonly get; internal set; }

	// Dirty tracking of the propagation: the local transform and parent version this was computed from, and a version
	// that changes whenever it is recomputed (0: never computed).
	internal Transform2D Local;
	internal uint Version;
	internal uint ParentVersion;

	/// <summary>
	/// Whether the entity is hidden: it is tagged <see cref="Hidden"/>, or an ancestor is. Set by the propagation; the
	/// extraction does not draw hidden entities.
	/// </summary>
	public bool IsHidden { readonly get; internal set; }

	/// <summary>Whether the propagation has computed this transform at least once.</summary>
	public readonly bool IsComputed => Version != 0;

	/// <summary>Transforms a point from the entity's local space to the world.</summary>
	public readonly Vector2 TransformPoint(Vector2 local) => Vector2.Transform(local, Matrix);

	/// <inheritdoc/>
	public override readonly string ToString() => $"GlobalTransform2D {{ Position = {Position}, Rotation = {Rotation}, Scale = {Scale} }}";
}

/// <summary>
/// The world transform of an entity with a <see cref="Transform"/> (the graphics abstractions' 3D transform, used as the
/// ECS component so games see one <c>Transform</c> type): the local matrix times the parent's world matrix
/// (<c>local.ToMatrix() * parent.Matrix</c>, row vectors), written by the transform propagation system. The 3D
/// extraction submits <see cref="Matrix"/> to the renderer (<c>docs/design/ion-rendering3d.md</c>, section 8).
/// </summary>
public struct GlobalTransform
{
	/// <summary>The world matrix (row vectors).</summary>
	public Matrix4x4 Matrix { readonly get; internal set; }

	/// <summary>The world position (the matrix translation).</summary>
	public readonly Vector3 Position => Matrix.Translation;

	internal Transform Local;
	internal uint Version;
	internal uint ParentVersion;

	/// <summary>
	/// Whether the entity is hidden: it is tagged <see cref="Hidden"/>, or an ancestor is. Set by the propagation; the
	/// extraction does not draw hidden entities.
	/// </summary>
	public bool IsHidden { readonly get; internal set; }

	/// <summary>Whether the propagation has computed this transform at least once.</summary>
	public readonly bool IsComputed => Version != 0;

	/// <inheritdoc/>
	public override readonly string ToString() => $"GlobalTransform {{ Position = {Position} }}";
}

/// <summary>
/// Draws the entity between its last two fixed steps instead of where the last one left it, so motion simulated at the
/// fixed rate (3D physics, or any FixedUpdate step) moves smoothly at any frame rate. Add it to the entities to smooth,
/// usually dynamic rigid bodies. Only what is drawn changes: <see cref="Transform"/>, and the <see cref="GlobalTransform"/>
/// of the Last propagation that gameplay reads, keep the simulated values.
/// </summary>
/// <remarks>
/// <para>
/// The transform propagation records the entity's <see cref="Transform"/> at the end of every fixed step
/// (<c>StageOrder.TransformInterpolation</c>), and its Render pass draws the transform between the last two records at
/// <see cref="GameTime.Alpha"/>: up to one fixed step behind the simulation. A transform changed since the last record
/// (in Update) is drawn as it is.
/// </para>
/// <para>Call <see cref="Snap"/> after teleporting the entity in a fixed step, so it does not glide to where it was put.</para>
/// </remarks>
public struct TransformInterpolation
{
	internal Transform Previous;
	internal Transform Current;
	internal bool Recorded;

	/// <summary>Draws the entity where it is from the next record on, without gliding from where it was.</summary>
	public void Snap() => Recorded = false;

	internal void Record(in Transform transform)
	{
		Previous = Recorded ? Current : transform;
		Current = transform;
		Recorded = true;
	}

	/// <summary>Where to draw <paramref name="transform"/>: between the last two records, unless it moved since the last one.</summary>
	internal readonly Transform Drawn(in Transform transform, float alpha)
	{
		if (!Recorded || transform.Position != Current.Position || transform.Rotation != Current.Rotation || transform.Scale != Current.Scale) return transform;
		return new Transform
		{
			Position = Vector3.Lerp(Previous.Position, Current.Position, alpha),
			Rotation = Quaternion.Slerp(Previous.Rotation, Current.Rotation, alpha),
			Scale = Vector3.Lerp(Previous.Scale, Current.Scale, alpha),
		};
	}
}
