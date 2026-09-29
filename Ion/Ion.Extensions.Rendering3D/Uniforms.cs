using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Rendering3D;

/// <summary>One local light of <see cref="ViewUniforms"/> (std140, 64 bytes): mirrors <c>IonLight</c> in <c>ion_view.glsl</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LightUniform
{
	public const float TypePoint = 0f;
	public const float TypeSpot = 1f;
	public const float TypeDirectional = 2f;

	/// <summary>xyz: position, w: range.</summary>
	public Vector4 PositionRange;

	/// <summary>rgb: linear color times intensity, w: type.</summary>
	public Vector4 ColorType;

	/// <summary>xyz: the direction the light travels.</summary>
	public Vector4 Direction;

	/// <summary>x: cos outer, y: 1 / (cos inner - cos outer).</summary>
	public Vector4 Spot;
}

/// <summary>The 8 local lights of a view.</summary>
[InlineArray(ViewUniforms.MaxLights)]
internal struct LightUniforms
{
	private LightUniform _element;
}

/// <summary>
/// The view uniform block (group 0, binding 0; std140, 944 bytes, slots of <see cref="SlotSize"/>): mirrors <c>View</c>
/// in <c>ion_view.glsl</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ViewUniforms
{
	/// <summary>Local lights per view.</summary>
	public const int MaxLights = 8;

	/// <summary>The stride of one view's block in the uniform ring (a multiple of every device's offset alignment).</summary>
	public const int SlotSize = 1024;

	public Matrix4x4 ViewProjection;
	public Matrix4x4 View;
	public Matrix4x4 Projection;
	public Matrix4x4 InverseViewProjection;
	public Matrix4x4 ShadowMatrix;
	public Vector4 CameraPosition;
	public Vector4 Ambient;
	public Vector4 ShadowParams;
	public Vector4 LightDirection;
	public Vector4 LightColor;
	public Vector4 ClearColor;
	public Vector4 Counts;
	public LightUniforms Lights;
}

/// <summary>
/// A material's uniform block (group 1, binding 0; std140, 64 bytes): mirrors <c>Material</c> in <c>unlit.frag</c> and
/// <c>pbr.frag</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MaterialUniforms
{
	public const int Size = 64;

	/// <summary>Linear RGBA.</summary>
	public Vector4 BaseColor;

	/// <summary>rgb: linear emissive times intensity, w: normal scale.</summary>
	public Vector4 Emissive;

	/// <summary>x: metallic, y: roughness, z: occlusion strength, w: alpha cutoff.</summary>
	public Vector4 Params;

	/// <summary>x: alpha mode, y: double sided.</summary>
	public Vector4 Flags;
}

/// <summary>
/// The per-instance data of one drawn object (vertex buffer slot 1, instance rate, 96 bytes): the world matrix's first
/// three columns and the normal matrix's rows (the adjugate of the 3x3 part, so no division; the shader normalizes). The
/// first normal row's w holds the flags.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct InstanceData
{
	public const int Size = 96;

	/// <summary>Flag: the object receives shadows.</summary>
	public const float ReceiveShadows = 1f;

	public Vector4 World0;
	public Vector4 World1;
	public Vector4 World2;
	public Vector4 Normal0;
	public Vector4 Normal1;
	public Vector4 Normal2;

	/// <summary>Fills the instance from a world matrix (row-vector convention).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write(ref InstanceData instance, in Matrix4x4 m, float flags)
	{
		instance.World0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
		instance.World1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
		instance.World2 = new Vector4(m.M13, m.M23, m.M33, m.M43);

		// Rows r0..r2 of the 3x3 part; n' is proportional to n.x (r1 x r2) + n.y (r2 x r0) + n.z (r0 x r1), times the
		// sign of the determinant (a mirrored object keeps its normals pointing out).
		var r0 = new Vector3(m.M11, m.M12, m.M13);
		var r1 = new Vector3(m.M21, m.M22, m.M23);
		var r2 = new Vector3(m.M31, m.M32, m.M33);
		var a = Vector3.Cross(r1, r2);
		var b = Vector3.Cross(r2, r0);
		var c = Vector3.Cross(r0, r1);
		if (Vector3.Dot(r0, a) < 0)
		{
			a = -a;
			b = -b;
			c = -c;
		}

		instance.Normal0 = new Vector4(a.X, b.X, c.X, flags);
		instance.Normal1 = new Vector4(a.Y, b.Y, c.Y, 0);
		instance.Normal2 = new Vector4(a.Z, b.Z, c.Z, 0);
	}
}

/// <summary>Color conversions matching the shaders.</summary>
internal static class ColorSpace
{
	/// <summary>One sRGB channel to linear.</summary>
	public static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

	/// <summary>One linear channel to sRGB.</summary>
	public static float LinearToSrgb(float c)
	{
		c = Math.Clamp(c, 0f, 1f);
		return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
	}

	/// <summary>An sRGB color's RGB in linear space (alpha unchanged).</summary>
	public static Vector4 ToLinear(Color color) => new(SrgbToLinear(color.R), SrgbToLinear(color.G), SrgbToLinear(color.B), color.A);

	/// <summary>A linear RGBA value as an sRGB <see cref="Color"/> (alpha unchanged).</summary>
	public static Color FromLinear(Vector4 linear) => new(LinearToSrgb(linear.X), LinearToSrgb(linear.Y), LinearToSrgb(linear.Z), linear.W);
}
