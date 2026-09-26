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

	/// <summary>Whether the propagation has computed this transform at least once.</summary>
	public readonly bool IsComputed => Version != 0;

	/// <inheritdoc/>
	public override readonly string ToString() => $"GlobalTransform {{ Position = {Position} }}";
}
