using System.Numerics;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics;

/// <summary>
/// A position, rotation and scale in 3D: the local transform of an object (an ECS component, or a field of a game
/// object). Unmanaged, 40 bytes.
/// </summary>
/// <remarks>
/// <para>
/// Conventions (the renderer's, System.Numerics'): right-handed, y up, cameras and lights look down their local -Z axis,
/// row vectors (a point is transformed as <c>Vector3.Transform(p, matrix)</c>), so <see cref="ToMatrix"/> is
/// scale, then rotation, then translation: <c>S * R * T</c>. A parent's matrix is applied after a child's:
/// <c>world = child.ToMatrix() * parentWorld</c>.
/// </para>
/// <para>
/// <c>default(Transform)</c> has a zero scale and a zero quaternion, which collapses everything to a point; create one
/// with <c>new Transform()</c> or <see cref="Identity"/> (identity rotation, unit scale).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Transform : IEquatable<Transform>
{
	/// <summary>The position (translation).</summary>
	public Vector3 Position;

	/// <summary>The rotation, a unit quaternion.</summary>
	public Quaternion Rotation;

	/// <summary>The scale per axis.</summary>
	public Vector3 Scale;

	/// <summary>The identity transform: at the origin, not rotated, unit scale.</summary>
	public Transform()
	{
		Rotation = Quaternion.Identity;
		Scale = Vector3.One;
	}

	/// <summary>A transform at <paramref name="position"/> with <paramref name="rotation"/> (identity when omitted) and <paramref name="scale"/> (one when omitted).</summary>
	public Transform(Vector3 position, Quaternion? rotation = null, Vector3? scale = null)
	{
		Position = position;
		Rotation = rotation ?? Quaternion.Identity;
		Scale = scale ?? Vector3.One;
	}

	/// <summary>The identity transform.</summary>
	public static Transform Identity => new();

	/// <summary>A transform at <paramref name="eye"/> whose -Z axis looks at <paramref name="target"/>, with <paramref name="up"/> (y when omitted) as the up hint.</summary>
	public static Transform LookAt(Vector3 eye, Vector3 target, Vector3? up = null) => new(eye, LookRotation(target - eye, up ?? Vector3.UnitY));

	/// <summary>
	/// The rotation whose -Z axis points along <paramref name="forward"/> and whose y axis is as close to
	/// <paramref name="up"/> as possible. A zero forward gives the identity; a forward parallel to up picks another up.
	/// </summary>
	public static Quaternion LookRotation(Vector3 forward, Vector3 up)
	{
		if (forward.LengthSquared() < 1e-12f) return Quaternion.Identity;
		var f = Vector3.Normalize(forward);
		var right = Vector3.Cross(f, up);
		if (right.LengthSquared() < 1e-12f) right = Vector3.Cross(f, MathF.Abs(f.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
		right = Vector3.Normalize(right);
		var u = Vector3.Cross(right, f);
		// Rows are the basis vectors (row-vector convention): x = right, y = up, z = -forward.
		var m = new Matrix4x4(
			right.X, right.Y, right.Z, 0,
			u.X, u.Y, u.Z, 0,
			-f.X, -f.Y, -f.Z, 0,
			0, 0, 0, 1);
		return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
	}

	/// <summary>
	/// Decomposes an affine matrix (no shear) into a transform. Returns the identity for a matrix that cannot be
	/// decomposed (for example with a zero scale).
	/// </summary>
	public static Transform FromMatrix(in Matrix4x4 matrix) =>
		Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation) ? new Transform(translation, rotation, scale) : new Transform(matrix.Translation);

	/// <summary>The local -Z axis in parent space (where a camera or light looks).</summary>
	public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

	/// <summary>The local x axis in parent space.</summary>
	public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

	/// <summary>The local y axis in parent space.</summary>
	public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

	/// <summary>The matrix <c>Scale * Rotation * Translation</c> (row-vector convention).</summary>
	public readonly Matrix4x4 ToMatrix()
	{
		var m = Matrix4x4.CreateFromQuaternion(Rotation);
		m.M11 *= Scale.X; m.M12 *= Scale.X; m.M13 *= Scale.X;
		m.M21 *= Scale.Y; m.M22 *= Scale.Y; m.M23 *= Scale.Y;
		m.M31 *= Scale.Z; m.M32 *= Scale.Z; m.M33 *= Scale.Z;
		m.M41 = Position.X; m.M42 = Position.Y; m.M43 = Position.Z;
		return m;
	}

	/// <summary>Transforms a point from local to parent space.</summary>
	public readonly Vector3 TransformPoint(Vector3 point) => Vector3.Transform(point * Scale, Rotation) + Position;

	/// <summary>Rotates (and scales) a direction from local to parent space, without translation.</summary>
	public readonly Vector3 TransformDirection(Vector3 direction) => Vector3.Transform(direction * Scale, Rotation);

	/// <summary>Turns this transform so that its -Z axis looks at <paramref name="target"/>.</summary>
	public void LookAtTarget(Vector3 target, Vector3? up = null) => Rotation = LookRotation(target - Position, up ?? Vector3.UnitY);

	/// <inheritdoc/>
	public readonly bool Equals(Transform other) => Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is Transform other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);

	/// <summary>Equality.</summary>
	public static bool operator ==(Transform left, Transform right) => left.Equals(right);

	/// <summary>Inequality.</summary>
	public static bool operator !=(Transform left, Transform right) => !left.Equals(right);

	/// <inheritdoc/>
	public override readonly string ToString() => $"Transform(position {Position}, rotation {Rotation}, scale {Scale})";
}
