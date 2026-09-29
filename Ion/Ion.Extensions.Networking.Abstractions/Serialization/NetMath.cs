using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ion.Extensions.Networking;

/// <summary>Exact comparisons used by the generated serializers: floating point values are compared by their bits, so a change of sign of zero or a NaN payload is a change.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NetCompare
{
	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(Vector2 a, Vector2 b) => Same(a.X, b.X) && Same(a.Y, b.Y);

	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(Vector3 a, Vector3 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(Vector4 a, Vector4 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z) && Same(a.W, b.W);

	/// <summary>Whether the bits are equal.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Same(Quaternion a, Quaternion b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z) && Same(a.W, b.W);
}

/// <summary>Blends used by the generated <see cref="NetSerializer{T}.Interpolate"/> overrides.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NetLerp
{
	/// <summary>Linear blend.</summary>
	public static float Lerp(float a, float b, float t) => a + (b - a) * t;

	/// <summary>Linear blend.</summary>
	public static double Lerp(double a, double b, float t) => a + (b - a) * t;

	/// <summary>Linear blend.</summary>
	public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

	/// <summary>Linear blend.</summary>
	public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

	/// <summary>Linear blend.</summary>
	public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

	/// <summary>Spherical blend (clamped to [0, 1]: rotations are not extrapolated).</summary>
	public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => Quaternion.Slerp(a, b, Math.Clamp(t, 0f, 1f));
}
