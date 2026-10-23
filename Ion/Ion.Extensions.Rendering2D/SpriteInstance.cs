using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// One sprite as the GPU reads it: 40 bytes in a per-instance vertex buffer (sprite.vert). Position, scale and rotation
/// are folded into the quad's origin corner and its two edge vectors, so the vertex shader needs no trigonometry and an
/// unrotated sprite costs the CPU no sine or cosine either.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct SpriteInstance
{
	/// <summary>The size in bytes (the vertex buffer stride).</summary>
	public const int SizeInBytes = 40;

	/// <summary>The packed UV rectangle of the whole texture: (0, 0)-(1, 1).</summary>
	public const ulong FullUv = 0xFFFF_FFFF_0000_0000ul;

	/// <summary>The world position of the quad's local (0, 0) corner.</summary>
	public Vector2 Position;

	/// <summary>The quad's top edge: local (1, 0) minus local (0, 0), in world units.</summary>
	public Vector2 AxisX;

	/// <summary>The quad's left edge: local (0, 1) minus local (0, 0), in world units.</summary>
	public Vector2 AxisY;

	/// <summary>The UV rectangle as four normalized 16-bit values: u0 | v0 &lt;&lt; 16 | u1 &lt;&lt; 32 | v1 &lt;&lt; 48.</summary>
	public ulong Uv;

	/// <summary>The tint as RGBA8 (R in the lowest byte), straight alpha.</summary>
	public uint Color;

	/// <summary>The depth (sort key; clamped to [0, 1] on the GPU).</summary>
	public float Depth;

	/// <summary>Packs a UV rectangle (already flipped) into <see cref="Uv"/>. Values are clamped to [0, 1].</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ulong PackUv(float u0, float v0, float u1, float v1)
	{
		var v = Vector128.Create(u0, v0, u1, v1);
		v = Vector128.Min(Vector128.Max(v, Vector128<float>.Zero), Vector128<float>.One) * 65535f + Vector128.Create(0.5f);
		var i = Vector128.ConvertToUInt32(v);
		var s = Vector128.Narrow(i, i);
		return s.AsUInt64().ToScalar();
	}

	/// <summary>Packs a straight-alpha float color into RGBA8 (R in the lowest byte).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint PackColor(Vector4 color)
	{
		var v = Vector128.Min(Vector128.Max(color.AsVector128(), Vector128<float>.Zero), Vector128<float>.One) * 255f + Vector128.Create(0.5f);
		var i = Vector128.ConvertToUInt32(v);
		var s = Vector128.Narrow(i, i);
		var b = Vector128.Narrow(s, s);
		return b.AsUInt32().ToScalar();
	}
}
