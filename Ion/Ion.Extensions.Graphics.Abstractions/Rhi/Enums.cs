namespace Ion.Extensions.Graphics.Rhi;

/// <summary>
/// A texel format. The set is the subset of WebGPU formats that Vulkan, Metal (through MoltenVK) and OpenGL ES 3.1 all
/// support as sampled textures. The depth formats are render attachments that can also be sampled (with
/// <see cref="TextureUsage.TextureBinding"/>), through a comparison sampler (<see cref="SamplerDescriptor.Compare"/>,
/// shadow maps) or as plain values; <see cref="TextureFormat.Depth32Float"/> is the portable choice for both.
/// </summary>
public enum TextureFormat
{
	/// <summary>No format (for example an absent depth attachment).</summary>
	Undefined,
	/// <summary>One 8-bit normalized channel.</summary>
	R8Unorm,
	/// <summary>Two 8-bit normalized channels.</summary>
	Rg8Unorm,
	/// <summary>Four 8-bit normalized channels, red first. The default for textures and offscreen targets.</summary>
	Rgba8Unorm,
	/// <summary><see cref="Rgba8Unorm"/> with sRGB encoding (converted to linear when sampled, from linear when written).</summary>
	Rgba8UnormSrgb,
	/// <summary>Four 8-bit normalized channels, blue first. The usual swapchain format on desktop Vulkan.</summary>
	Bgra8Unorm,
	/// <summary><see cref="Bgra8Unorm"/> with sRGB encoding.</summary>
	Bgra8UnormSrgb,
	/// <summary>One 16-bit float channel.</summary>
	R16Float,
	/// <summary>Four 16-bit float channels.</summary>
	Rgba16Float,
	/// <summary>One 32-bit float channel.</summary>
	R32Float,
	/// <summary>Four 32-bit float channels.</summary>
	Rgba32Float,
	/// <summary>16-bit normalized depth.</summary>
	Depth16Unorm,
	/// <summary>At least 24-bit depth with 8-bit stencil (<c>D24_UNORM_S8_UINT</c> or <c>D32_SFLOAT_S8_UINT</c>, whichever the device has).</summary>
	Depth24PlusStencil8,
	/// <summary>32-bit float depth.</summary>
	Depth32Float,
}

/// <summary>
/// Helpers for <see cref="TextureFormat"/>.
/// </summary>
public static class TextureFormatExtensions
{
	/// <summary>True for the depth (and depth-stencil) formats.</summary>
	public static bool IsDepth(this TextureFormat format) => format is TextureFormat.Depth16Unorm or TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32Float;

	/// <summary>True when the format has a stencil aspect.</summary>
	public static bool HasStencil(this TextureFormat format) => format == TextureFormat.Depth24PlusStencil8;

	/// <summary>True for the sRGB-encoded formats.</summary>
	public static bool IsSrgb(this TextureFormat format) => format is TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8UnormSrgb;

	/// <summary>The size of one texel in bytes (for <see cref="TextureFormat.Depth24PlusStencil8"/>, the 4 bytes of the packed form).</summary>
	public static int BytesPerPixel(this TextureFormat format) => format switch
	{
		TextureFormat.R8Unorm => 1,
		TextureFormat.Rg8Unorm or TextureFormat.R16Float or TextureFormat.Depth16Unorm => 2,
		TextureFormat.Rgba8Unorm or TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb
			or TextureFormat.R32Float or TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32Float => 4,
		TextureFormat.Rgba16Float => 8,
		TextureFormat.Rgba32Float => 16,
		_ => 0,
	};
}

/// <summary>
/// How a buffer may be used. Combine the flags a buffer needs; a backend may place it in faster or slower memory accordingly.
/// </summary>
/// <summary>
/// The dimension of a texture (WebGPU's view dimension, fixed per texture here): a 2D texture or a cube map.
/// </summary>
public enum TextureDimension
{
	/// <summary>A 2D texture (one layer).</summary>
	D2,
	/// <summary>
	/// A cube map: six square 2D faces as array layers 0 to 5 in the order +X, -X, +Y, -Y, +Z, -Z (Vulkan, GL and WebGPU
	/// order). Its views are cube views (<c>textureCube</c> in GLSL, <c>samplerCube</c> in GLSL ES). Sampled only: cube
	/// maps cannot be render attachments or copy sources.
	/// </summary>
	Cube,
}

/// <summary>Extension methods for <see cref="TextureDimension"/>.</summary>
public static class TextureDimensionExtensions
{
	/// <summary>The number of array layers of a texture of this dimension (1, or 6 for a cube map).</summary>
	public static uint ArrayLayerCount(this TextureDimension dimension) => dimension == TextureDimension.Cube ? 6u : 1u;
}

[Flags]
public enum BufferUsage
{
	/// <summary>No usage.</summary>
	None = 0,
	/// <summary>The CPU reads it back with <see cref="IBuffer.Read"/> (a readback buffer; it lives in host-visible memory).</summary>
	MapRead = 1 << 0,
	/// <summary>The CPU writes it directly (reserved; use <see cref="IQueue.WriteBuffer"/>).</summary>
	MapWrite = 1 << 1,
	/// <summary>Source of a copy.</summary>
	CopySrc = 1 << 2,
	/// <summary>Destination of a copy or of <see cref="IQueue.WriteBuffer"/>.</summary>
	CopyDst = 1 << 3,
	/// <summary>Index buffer.</summary>
	Index = 1 << 4,
	/// <summary>Vertex (or per-instance) buffer.</summary>
	Vertex = 1 << 5,
	/// <summary>Uniform buffer.</summary>
	Uniform = 1 << 6,
	/// <summary>
	/// Storage buffer. OpenGL ES 3.1 guarantees storage buffers only in the fragment and compute stages (the Mali-G31 of the
	/// R36S has none in the vertex stage), so portable code must not read one from a vertex shader; use per-instance vertex
	/// buffers instead.
	/// </summary>
	Storage = 1 << 7,
}

/// <summary>
/// How a texture may be used.
/// </summary>
[Flags]
public enum TextureUsage
{
	/// <summary>No usage.</summary>
	None = 0,
	/// <summary>Source of a copy (for example a readback or a screenshot).</summary>
	CopySrc = 1 << 0,
	/// <summary>Destination of a copy or of <see cref="IQueue.WriteTexture"/>.</summary>
	CopyDst = 1 << 1,
	/// <summary>Sampled from shaders through a bind group.</summary>
	TextureBinding = 1 << 2,
	/// <summary>Color or depth attachment of a render pass.</summary>
	RenderAttachment = 1 << 4,
}

/// <summary>
/// Shader stages, for bind group layout visibility.
/// </summary>
[Flags]
public enum ShaderStage
{
	/// <summary>No stage.</summary>
	None = 0,
	/// <summary>The vertex stage.</summary>
	Vertex = 1 << 0,
	/// <summary>The fragment stage.</summary>
	Fragment = 1 << 1,
	/// <summary>Both stages.</summary>
	VertexFragment = Vertex | Fragment,
}

/// <summary>
/// The kind of resource a bind group layout entry holds.
/// </summary>
public enum BindingType
{
	/// <summary>A uniform buffer (GLES: a uniform block binding point).</summary>
	UniformBuffer,
	/// <summary>A read-only storage buffer (see the GLES restriction on <see cref="BufferUsage.Storage"/>).</summary>
	StorageBuffer,
	/// <summary>A sampler (GLES: combined with the texture of the same group into one texture unit).</summary>
	Sampler,
	/// <summary>A sampled 2D texture (GLES: a texture unit).</summary>
	Texture,
}

/// <summary>Texture coordinate addressing outside [0, 1].</summary>
public enum AddressMode
{
	/// <summary>Clamp to the edge texel.</summary>
	ClampToEdge,
	/// <summary>Repeat.</summary>
	Repeat,
	/// <summary>Repeat mirrored.</summary>
	MirrorRepeat,
}

/// <summary>Texel filtering.</summary>
public enum FilterMode
{
	/// <summary>Nearest texel (point sampling).</summary>
	Nearest,
	/// <summary>Linear interpolation.</summary>
	Linear,
}

/// <summary>A comparison, for depth tests and comparison samplers.</summary>
public enum CompareFunction
{
	/// <summary>Never passes.</summary>
	Never,
	/// <summary>Passes when the new value is less.</summary>
	Less,
	/// <summary>Passes when equal.</summary>
	Equal,
	/// <summary>Passes when less or equal.</summary>
	LessEqual,
	/// <summary>Passes when greater.</summary>
	Greater,
	/// <summary>Passes when not equal.</summary>
	NotEqual,
	/// <summary>Passes when greater or equal.</summary>
	GreaterEqual,
	/// <summary>Always passes.</summary>
	Always,
}

/// <summary>The format of one vertex attribute.</summary>
public enum VertexFormat
{
	/// <summary>One float.</summary>
	Float32,
	/// <summary>Two floats.</summary>
	Float32x2,
	/// <summary>Three floats.</summary>
	Float32x3,
	/// <summary>Four floats.</summary>
	Float32x4,
	/// <summary>Four normalized bytes (a packed RGBA8 color reads as a vec4 in [0, 1]).</summary>
	Unorm8x4,
	/// <summary>Four unsigned bytes.</summary>
	Uint8x4,
	/// <summary>One unsigned 32-bit integer.</summary>
	Uint32,
	/// <summary>One signed 32-bit integer.</summary>
	Sint32,
	/// <summary>Two normalized unsigned 16-bit integers (a vec2 in [0, 1]; GLES: <c>GL_UNSIGNED_SHORT</c>, normalized).</summary>
	Unorm16x2,
	/// <summary>Four normalized unsigned 16-bit integers (a vec4 in [0, 1]; the sprite batch's UV rectangle).</summary>
	Unorm16x4,
}

/// <summary>
/// Helpers for <see cref="VertexFormat"/>.
/// </summary>
public static class VertexFormatExtensions
{
	/// <summary>The size of the attribute in bytes.</summary>
	public static uint Size(this VertexFormat format) => format switch
	{
		VertexFormat.Float32 or VertexFormat.Unorm8x4 or VertexFormat.Uint8x4 or VertexFormat.Uint32 or VertexFormat.Sint32 or VertexFormat.Unorm16x2 => 4,
		VertexFormat.Float32x2 or VertexFormat.Unorm16x4 => 8,
		VertexFormat.Float32x3 => 12,
		VertexFormat.Float32x4 => 16,
		_ => 0,
	};
}

/// <summary>Whether a vertex buffer advances per vertex or per instance.</summary>
public enum VertexStepMode
{
	/// <summary>Per vertex.</summary>
	Vertex,
	/// <summary>Per instance (GLES: an attribute divisor of 1).</summary>
	Instance,
}

/// <summary>Primitive topology.</summary>
public enum PrimitiveTopology
{
	/// <summary>Points.</summary>
	PointList,
	/// <summary>Separate lines.</summary>
	LineList,
	/// <summary>A connected line strip.</summary>
	LineStrip,
	/// <summary>Separate triangles.</summary>
	TriangleList,
	/// <summary>A triangle strip.</summary>
	TriangleStrip,
}

/// <summary>Which winding is front facing.</summary>
public enum FrontFace
{
	/// <summary>Counter-clockwise.</summary>
	Ccw,
	/// <summary>Clockwise.</summary>
	Cw,
}

/// <summary>Face culling.</summary>
public enum CullMode
{
	/// <summary>No culling.</summary>
	None,
	/// <summary>Cull front faces.</summary>
	Front,
	/// <summary>Cull back faces.</summary>
	Back,
}

/// <summary>The type of index buffer elements.</summary>
public enum IndexFormat
{
	/// <summary>16-bit indices.</summary>
	Uint16,
	/// <summary>32-bit indices.</summary>
	Uint32,
}

/// <summary>A blend factor.</summary>
public enum BlendFactor
{
	/// <summary>0.</summary>
	Zero,
	/// <summary>1.</summary>
	One,
	/// <summary>Source color.</summary>
	Src,
	/// <summary>1 - source color.</summary>
	OneMinusSrc,
	/// <summary>Source alpha.</summary>
	SrcAlpha,
	/// <summary>1 - source alpha.</summary>
	OneMinusSrcAlpha,
	/// <summary>Destination color.</summary>
	Dst,
	/// <summary>1 - destination color.</summary>
	OneMinusDst,
	/// <summary>Destination alpha.</summary>
	DstAlpha,
	/// <summary>1 - destination alpha.</summary>
	OneMinusDstAlpha,
	/// <summary>min(source alpha, 1 - destination alpha).</summary>
	SrcAlphaSaturated,
}

/// <summary>A blend operation.</summary>
public enum BlendOperation
{
	/// <summary>src + dst.</summary>
	Add,
	/// <summary>src - dst.</summary>
	Subtract,
	/// <summary>dst - src.</summary>
	ReverseSubtract,
	/// <summary>min(src, dst).</summary>
	Min,
	/// <summary>max(src, dst).</summary>
	Max,
}

/// <summary>Which color channels a render target writes.</summary>
[Flags]
public enum ColorWriteMask
{
	/// <summary>None.</summary>
	None = 0,
	/// <summary>Red.</summary>
	Red = 1,
	/// <summary>Green.</summary>
	Green = 2,
	/// <summary>Blue.</summary>
	Blue = 4,
	/// <summary>Alpha.</summary>
	Alpha = 8,
	/// <summary>All four channels.</summary>
	All = Red | Green | Blue | Alpha,
}

/// <summary>What a render pass does with an attachment's contents when it begins.</summary>
public enum LoadOp
{
	/// <summary>Clear to the attachment's clear value.</summary>
	Clear,
	/// <summary>Keep the previous contents.</summary>
	Load,
}

/// <summary>What a render pass does with an attachment's contents when it ends.</summary>
public enum StoreOp
{
	/// <summary>Keep the rendered contents.</summary>
	Store,
	/// <summary>Discard them (depth buffers that are not read later; saves bandwidth on tilers).</summary>
	Discard,
}

/// <summary>How a surface presents frames.</summary>
public enum PresentMode
{
	/// <summary>Wait for vertical blank (vsync). Always supported.</summary>
	Fifo,
	/// <summary>Replace the queued frame, no tearing and no waiting (triple buffering), where supported.</summary>
	Mailbox,
	/// <summary>Present immediately, may tear, where supported.</summary>
	Immediate,
}

/// <summary>The language of a shader module's code.</summary>
public enum ShaderLanguage
{
	/// <summary>SPIR-V binary (Vulkan).</summary>
	SpirV,
	/// <summary>GLSL ES 3.10 source (the GLES backend; produced from SPIR-V by SPIRV-Cross at build time).</summary>
	GlslEs,
}

/// <summary>The result of <see cref="ISurface.GetCurrentTexture"/>.</summary>
public enum SurfaceTextureStatus
{
	/// <summary>A texture was acquired.</summary>
	Success,
	/// <summary>A texture was acquired but the surface should be reconfigured (for example after a resize).</summary>
	Suboptimal,
	/// <summary>No texture: the surface is out of date (or zero-sized, for example minimized) and must be reconfigured.</summary>
	Outdated,
}
