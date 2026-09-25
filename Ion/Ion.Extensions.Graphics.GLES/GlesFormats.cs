using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>A texture format's GL storage and transfer formats.</summary>
/// <param name="Internal">The sized internal format for <c>glTexStorage2D</c>.</param>
/// <param name="Format">The pixel transfer format.</param>
/// <param name="Type">The pixel transfer type.</param>
/// <param name="SwapRedBlue">True for the BGRA formats, stored as RGBA with red and blue swapped on the CPU at upload and readback (GLES has no core BGRA8 storage).</param>
internal readonly record struct GlesTextureFormat(GLEnum Internal, GLEnum Format, GLEnum Type, bool SwapRedBlue = false);

/// <summary>A vertex format's GL attribute layout.</summary>
internal readonly record struct GlesVertexFormat(int Components, GLEnum Type, bool Normalized, bool Integer);

/// <summary>Conversions from RHI enums to GL enums.</summary>
internal static class GlesFormats
{
	public static GlesTextureFormat ToGles(this TextureFormat format) => format switch
	{
		TextureFormat.R8Unorm => new(GLEnum.R8, GLEnum.Red, GLEnum.UnsignedByte),
		TextureFormat.Rg8Unorm => new(GLEnum.RG8, GLEnum.RG, GLEnum.UnsignedByte),
		TextureFormat.Rgba8Unorm => new(GLEnum.Rgba8, GLEnum.Rgba, GLEnum.UnsignedByte),
		TextureFormat.Rgba8UnormSrgb => new(GLEnum.Srgb8Alpha8, GLEnum.Rgba, GLEnum.UnsignedByte),
		TextureFormat.Bgra8Unorm => new(GLEnum.Rgba8, GLEnum.Rgba, GLEnum.UnsignedByte, SwapRedBlue: true),
		TextureFormat.Bgra8UnormSrgb => new(GLEnum.Srgb8Alpha8, GLEnum.Rgba, GLEnum.UnsignedByte, SwapRedBlue: true),
		TextureFormat.R16Float => new(GLEnum.R16f, GLEnum.Red, GLEnum.HalfFloat),
		TextureFormat.Rgba16Float => new(GLEnum.Rgba16f, GLEnum.Rgba, GLEnum.HalfFloat),
		TextureFormat.R32Float => new(GLEnum.R32f, GLEnum.Red, GLEnum.Float),
		TextureFormat.Rgba32Float => new(GLEnum.Rgba32f, GLEnum.Rgba, GLEnum.Float),
		TextureFormat.Depth16Unorm => new(GLEnum.DepthComponent16, GLEnum.DepthComponent, GLEnum.UnsignedShort),
		TextureFormat.Depth24PlusStencil8 => new(GLEnum.Depth24Stencil8, GLEnum.DepthStencil, GLEnum.UnsignedInt248),
		TextureFormat.Depth32Float => new(GLEnum.DepthComponent32f, GLEnum.DepthComponent, GLEnum.Float),
		_ => throw new NotSupportedException($"Texture format {format} is not supported by the GLES backend."),
	};

	/// <summary>True for the float formats, which GLES renders into only with <c>EXT_color_buffer_(half_)float</c>.</summary>
	public static bool IsFloat(this TextureFormat format) =>
		format is TextureFormat.R16Float or TextureFormat.Rgba16Float or TextureFormat.R32Float or TextureFormat.Rgba32Float;

	/// <summary>The number of color channels of a color format.</summary>
	public static int Channels(this TextureFormat format) => format switch
	{
		TextureFormat.R8Unorm or TextureFormat.R16Float or TextureFormat.R32Float => 1,
		TextureFormat.Rg8Unorm => 2,
		_ => 4,
	};

	public static GlesVertexFormat ToGles(this VertexFormat format) => format switch
	{
		VertexFormat.Float32 => new(1, GLEnum.Float, false, false),
		VertexFormat.Float32x2 => new(2, GLEnum.Float, false, false),
		VertexFormat.Float32x3 => new(3, GLEnum.Float, false, false),
		VertexFormat.Float32x4 => new(4, GLEnum.Float, false, false),
		VertexFormat.Unorm8x4 => new(4, GLEnum.UnsignedByte, true, false),
		VertexFormat.Uint8x4 => new(4, GLEnum.UnsignedByte, false, true),
		VertexFormat.Uint32 => new(1, GLEnum.UnsignedInt, false, true),
		VertexFormat.Sint32 => new(1, GLEnum.Int, false, true),
		_ => throw new NotSupportedException($"Vertex format {format} is not supported by the GLES backend."),
	};

	public static GLEnum ToGles(this PrimitiveTopology topology) => topology switch
	{
		PrimitiveTopology.PointList => GLEnum.Points,
		PrimitiveTopology.LineList => GLEnum.Lines,
		PrimitiveTopology.LineStrip => GLEnum.LineStrip,
		PrimitiveTopology.TriangleStrip => GLEnum.TriangleStrip,
		_ => GLEnum.Triangles,
	};

	public static GLEnum ToGles(this BlendFactor factor) => factor switch
	{
		BlendFactor.Zero => GLEnum.Zero,
		BlendFactor.One => GLEnum.One,
		BlendFactor.Src => GLEnum.SrcColor,
		BlendFactor.OneMinusSrc => GLEnum.OneMinusSrcColor,
		BlendFactor.SrcAlpha => GLEnum.SrcAlpha,
		BlendFactor.OneMinusSrcAlpha => GLEnum.OneMinusSrcAlpha,
		BlendFactor.Dst => GLEnum.DstColor,
		BlendFactor.OneMinusDst => GLEnum.OneMinusDstColor,
		BlendFactor.DstAlpha => GLEnum.DstAlpha,
		BlendFactor.OneMinusDstAlpha => GLEnum.OneMinusDstAlpha,
		BlendFactor.SrcAlphaSaturated => GLEnum.SrcAlphaSaturate,
		_ => GLEnum.One,
	};

	public static GLEnum ToGles(this BlendOperation operation) => operation switch
	{
		BlendOperation.Subtract => GLEnum.FuncSubtract,
		BlendOperation.ReverseSubtract => GLEnum.FuncReverseSubtract,
		BlendOperation.Min => GLEnum.Min,
		BlendOperation.Max => GLEnum.Max,
		_ => GLEnum.FuncAdd,
	};

	public static GLEnum ToGles(this CompareFunction function) => function switch
	{
		CompareFunction.Never => GLEnum.Never,
		CompareFunction.Less => GLEnum.Less,
		CompareFunction.Equal => GLEnum.Equal,
		CompareFunction.LessEqual => GLEnum.Lequal,
		CompareFunction.Greater => GLEnum.Greater,
		CompareFunction.NotEqual => GLEnum.Notequal,
		CompareFunction.GreaterEqual => GLEnum.Gequal,
		_ => GLEnum.Always,
	};

	public static GLEnum ToGles(this AddressMode mode) => mode switch
	{
		AddressMode.Repeat => GLEnum.Repeat,
		AddressMode.MirrorRepeat => GLEnum.MirroredRepeat,
		_ => GLEnum.ClampToEdge,
	};

	public static GLEnum ToGlesMag(this FilterMode filter) => filter == FilterMode.Linear ? GLEnum.Linear : GLEnum.Nearest;

	public static GLEnum ToGlesMin(FilterMode min, FilterMode mipmap) => (min, mipmap) switch
	{
		(FilterMode.Nearest, FilterMode.Nearest) => GLEnum.NearestMipmapNearest,
		(FilterMode.Nearest, FilterMode.Linear) => GLEnum.NearestMipmapLinear,
		(FilterMode.Linear, FilterMode.Nearest) => GLEnum.LinearMipmapNearest,
		_ => GLEnum.LinearMipmapLinear,
	};

	public static uint IndexSize(this IndexFormat format) => format == IndexFormat.Uint32 ? 4u : 2u;

	public static GLEnum ToGles(this IndexFormat format) => format == IndexFormat.Uint32 ? GLEnum.UnsignedInt : GLEnum.UnsignedShort;
}
