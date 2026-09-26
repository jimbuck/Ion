using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics;

/// <summary>
/// An axis-aligned bounding box. Meshes carry one in local space; the renderer transforms it by each object's world
/// matrix (<see cref="Transform(in Matrix4x4)"/>) and culls it against each camera's <see cref="Frustum"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Aabb : IEquatable<Aabb>
{
	/// <summary>The minimum corner.</summary>
	public Vector3 Min;

	/// <summary>The maximum corner.</summary>
	public Vector3 Max;

	/// <summary>A box from its corners.</summary>
	public Aabb(Vector3 min, Vector3 max)
	{
		Min = min;
		Max = max;
	}

	/// <summary>The empty box (inverted, so the first <see cref="Encapsulate(Vector3)"/> sets it to a point).</summary>
	public static Aabb Empty => new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

	/// <summary>A box from its center and half size.</summary>
	public static Aabb FromCenterExtents(Vector3 center, Vector3 extents) => new(center - extents, center + extents);

	/// <summary>The smallest box around <paramref name="points"/> (<see cref="Empty"/> for none).</summary>
	public static Aabb FromPoints(ReadOnlySpan<Vector3> points)
	{
		var box = Empty;
		foreach (var point in points) box.Encapsulate(point);
		return box;
	}

	/// <summary>True when the box is not empty (<see cref="Min"/> is at most <see cref="Max"/> on every axis).</summary>
	public readonly bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

	/// <summary>The center.</summary>
	public readonly Vector3 Center => (Min + Max) * 0.5f;

	/// <summary>The half size.</summary>
	public readonly Vector3 Extents => (Max - Min) * 0.5f;

	/// <summary>The size.</summary>
	public readonly Vector3 Size => Max - Min;

	/// <summary>Grows the box to contain <paramref name="point"/>.</summary>
	public void Encapsulate(Vector3 point)
	{
		Min = Vector3.Min(Min, point);
		Max = Vector3.Max(Max, point);
	}

	/// <summary>Grows the box to contain <paramref name="other"/>.</summary>
	public void Encapsulate(in Aabb other)
	{
		if (!other.IsValid) return;
		Min = Vector3.Min(Min, other.Min);
		Max = Vector3.Max(Max, other.Max);
	}

	/// <summary>The smallest box containing both.</summary>
	public static Aabb Union(in Aabb a, in Aabb b)
	{
		var result = a;
		result.Encapsulate(b);
		return result;
	}

	/// <summary>True when <paramref name="point"/> is inside or on the box.</summary>
	public readonly bool Contains(Vector3 point) =>
		point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y && point.Z >= Min.Z && point.Z <= Max.Z;

	/// <summary>True when the boxes overlap (touching counts).</summary>
	public readonly bool Intersects(in Aabb other) =>
		Min.X <= other.Max.X && Max.X >= other.Min.X && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

	/// <summary>
	/// The box around this box transformed by <paramref name="matrix"/> (an affine world matrix, row-vector convention):
	/// the center is transformed and the extents are multiplied by the absolute values of the 3x3 part (Arvo's method),
	/// which is exact for the transformed box's bounds and costs no corner loop.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly Aabb Transform(in Matrix4x4 matrix)
	{
		var center = (Min + Max) * 0.5f;
		var extents = (Max - Min) * 0.5f;
		var worldCenter = Vector3.Transform(center, matrix);
		var worldExtents = new Vector3(
			MathF.Abs(matrix.M11) * extents.X + MathF.Abs(matrix.M21) * extents.Y + MathF.Abs(matrix.M31) * extents.Z,
			MathF.Abs(matrix.M12) * extents.X + MathF.Abs(matrix.M22) * extents.Y + MathF.Abs(matrix.M32) * extents.Z,
			MathF.Abs(matrix.M13) * extents.X + MathF.Abs(matrix.M23) * extents.Y + MathF.Abs(matrix.M33) * extents.Z);
		return new Aabb(worldCenter - worldExtents, worldCenter + worldExtents);
	}

	/// <summary>Writes the eight corners into <paramref name="corners"/> (which needs room for 8).</summary>
	public readonly void GetCorners(Span<Vector3> corners)
	{
		if (corners.Length < 8) throw new ArgumentException("Needs room for 8 corners.", nameof(corners));
		for (var i = 0; i < 8; i++)
		{
			corners[i] = new Vector3((i & 1) != 0 ? Max.X : Min.X, (i & 2) != 0 ? Max.Y : Min.Y, (i & 4) != 0 ? Max.Z : Min.Z);
		}
	}

	/// <inheritdoc/>
	public readonly bool Equals(Aabb other) => Min == other.Min && Max == other.Max;

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is Aabb other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode() => HashCode.Combine(Min, Max);

	/// <summary>Equality.</summary>
	public static bool operator ==(Aabb left, Aabb right) => left.Equals(right);

	/// <summary>Inequality.</summary>
	public static bool operator !=(Aabb left, Aabb right) => !left.Equals(right);

	/// <inheritdoc/>
	public override readonly string ToString() => $"Aabb({Min}, {Max})";
}

/// <summary>A bounding sphere.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct BoundingSphere(Vector3 Center, float Radius)
{
	/// <summary>The sphere around <paramref name="box"/> (centered, through its corners).</summary>
	public static BoundingSphere FromAabb(in Aabb box) => box.IsValid ? new(box.Center, box.Extents.Length()) : new(Vector3.Zero, 0);

	/// <summary>
	/// The sphere around <paramref name="points"/>: centered on their box, with the distance to the farthest point as
	/// radius (tighter than <see cref="FromAabb"/> for round shapes).
	/// </summary>
	public static BoundingSphere FromPoints(ReadOnlySpan<Vector3> points)
	{
		if (points.IsEmpty) return new(Vector3.Zero, 0);
		var center = Aabb.FromPoints(points).Center;
		var radiusSquared = 0f;
		foreach (var point in points) radiusSquared = MathF.Max(radiusSquared, Vector3.DistanceSquared(center, point));
		return new(center, MathF.Sqrt(radiusSquared));
	}

	/// <summary>The sphere transformed by <paramref name="matrix"/>: the center transformed, the radius scaled by the largest axis scale.</summary>
	public BoundingSphere Transform(in Matrix4x4 matrix)
	{
		var sx = new Vector3(matrix.M11, matrix.M12, matrix.M13).LengthSquared();
		var sy = new Vector3(matrix.M21, matrix.M22, matrix.M23).LengthSquared();
		var sz = new Vector3(matrix.M31, matrix.M32, matrix.M33).LengthSquared();
		return new(Vector3.Transform(Center, matrix), Radius * MathF.Sqrt(MathF.Max(sx, MathF.Max(sy, sz))));
	}
}

/// <summary>
/// A view frustum: six planes whose normals point inside. Built from a view-projection matrix in the RHI's clip space
/// (WebGPU: x and y in [-w, w], depth z in [0, w]), it answers whether a bounding volume can be visible.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Frustum
{
	/// <summary>The left plane.</summary>
	public Plane Left;

	/// <summary>The right plane.</summary>
	public Plane Right;

	/// <summary>The bottom plane.</summary>
	public Plane Bottom;

	/// <summary>The top plane.</summary>
	public Plane Top;

	/// <summary>The near plane.</summary>
	public Plane Near;

	/// <summary>The far plane.</summary>
	public Plane Far;

	/// <summary>
	/// The frustum of <paramref name="viewProjection"/> (row-vector convention: <c>clip = Vector4.Transform(world, viewProjection)</c>),
	/// with the RHI's depth range [0, 1] (Gribb and Hartmann's plane extraction).
	/// </summary>
	public static Frustum FromMatrix(in Matrix4x4 viewProjection)
	{
		ref readonly var m = ref viewProjection;
		var c0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
		var c1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
		var c2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
		var c3 = new Vector4(m.M14, m.M24, m.M34, m.M44);
		return new Frustum
		{
			Left = Normalized(c3 + c0),
			Right = Normalized(c3 - c0),
			Bottom = Normalized(c3 + c1),
			Top = Normalized(c3 - c1),
			Near = Normalized(c2),
			Far = Normalized(c3 - c2),
		};
	}

	private static Plane Normalized(Vector4 p) => Plane.Normalize(new Plane(p.X, p.Y, p.Z, p.W));

	/// <summary>The plane at <paramref name="index"/>: left, right, bottom, top, near, far.</summary>
	public readonly Plane this[int index] => index switch
	{
		0 => Left,
		1 => Right,
		2 => Bottom,
		3 => Top,
		4 => Near,
		5 => Far,
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	};

	/// <summary>True when <paramref name="point"/> is inside or on the frustum.</summary>
	public readonly bool Contains(Vector3 point) =>
		Distance(Left, point) >= 0 && Distance(Right, point) >= 0 && Distance(Bottom, point) >= 0
		&& Distance(Top, point) >= 0 && Distance(Near, point) >= 0 && Distance(Far, point) >= 0;

	/// <summary>
	/// True when <paramref name="box"/> may be visible: false only when it lies entirely outside one plane (conservative:
	/// a box near a frustum corner can pass while being outside).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly bool Intersects(in Aabb box)
	{
		var center = (box.Min + box.Max) * 0.5f;
		var extents = (box.Max - box.Min) * 0.5f;
		return Inside(Left, center, extents) && Inside(Right, center, extents) && Inside(Bottom, center, extents)
			&& Inside(Top, center, extents) && Inside(Near, center, extents) && Inside(Far, center, extents);
	}

	/// <summary>True when <paramref name="sphere"/> may be visible (not entirely outside one plane).</summary>
	public readonly bool Intersects(in BoundingSphere sphere)
	{
		var r = -sphere.Radius;
		return Distance(Left, sphere.Center) >= r && Distance(Right, sphere.Center) >= r && Distance(Bottom, sphere.Center) >= r
			&& Distance(Top, sphere.Center) >= r && Distance(Near, sphere.Center) >= r && Distance(Far, sphere.Center) >= r;
	}

	/// <summary>
	/// The eight corners of the frustum of <paramref name="viewProjection"/> in world space (near plane first: the corners
	/// at clip x, y in {-1, 1} and depth 0, then depth 1).
	/// </summary>
	public static void GetCorners(in Matrix4x4 viewProjection, Span<Vector3> corners)
	{
		if (corners.Length < 8) throw new ArgumentException("Needs room for 8 corners.", nameof(corners));
		if (!Matrix4x4.Invert(viewProjection, out var inverse)) throw new ArgumentException("The matrix cannot be inverted.", nameof(viewProjection));
		for (var i = 0; i < 8; i++)
		{
			var clip = new Vector4((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : 0, 1);
			var world = Vector4.Transform(clip, inverse);
			corners[i] = new Vector3(world.X, world.Y, world.Z) / world.W;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Distance(in Plane plane, Vector3 point) => Vector3.Dot(plane.Normal, point) + plane.D;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool Inside(in Plane plane, Vector3 center, Vector3 extents)
	{
		var n = plane.Normal;
		var radius = extents.X * MathF.Abs(n.X) + extents.Y * MathF.Abs(n.Y) + extents.Z * MathF.Abs(n.Z);
		return Vector3.Dot(n, center) + plane.D + radius >= 0;
	}
}
