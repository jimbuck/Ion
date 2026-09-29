using System.Numerics;

namespace Ion.Extensions.Graphics.Rhi;

/// <summary>Describes a buffer for <see cref="IGraphicsDevice.CreateBuffer"/>.</summary>
public readonly record struct BufferDescriptor(ulong Size, BufferUsage Usage, string? Label = null);

/// <summary>
/// Describes a texture for <see cref="IGraphicsDevice.CreateTexture"/>: a 2D texture, or with
/// <see cref="TextureDimension.Cube"/> a cube map of six <paramref name="Width"/> by <paramref name="Height"/> faces
/// (which must be square; write each face with <see cref="TextureRegion.ArrayLayer"/>).
/// </summary>
public readonly record struct TextureDescriptor(
	uint Width,
	uint Height,
	TextureFormat Format,
	TextureUsage Usage,
	uint MipLevelCount = 1,
	uint SampleCount = 1,
	string? Label = null,
	TextureDimension Dimension = TextureDimension.D2);

/// <summary>Describes a view of a texture for <see cref="ITexture.CreateView"/>. The default views every mip level.</summary>
public readonly record struct TextureViewDescriptor(uint BaseMipLevel = 0, uint MipLevelCount = 0, string? Label = null);

/// <summary>Describes a sampler for <see cref="IGraphicsDevice.CreateSampler"/>.</summary>
public readonly record struct SamplerDescriptor
{
	/// <summary>A nearest-neighbour sampler clamped to the edge (pixel art, UI).</summary>
	public static SamplerDescriptor PointClamp => new() { MagFilter = FilterMode.Nearest, MinFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest };

	/// <summary>A bilinear sampler clamped to the edge.</summary>
	public static SamplerDescriptor LinearClamp => new() { MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, MipmapFilter = FilterMode.Linear };

	/// <summary>Creates a nearest, clamped sampler.</summary>
	public SamplerDescriptor() { }

	/// <summary>Addressing in U.</summary>
	public AddressMode AddressModeU { get; init; } = AddressMode.ClampToEdge;
	/// <summary>Addressing in V.</summary>
	public AddressMode AddressModeV { get; init; } = AddressMode.ClampToEdge;
	/// <summary>Addressing in W.</summary>
	public AddressMode AddressModeW { get; init; } = AddressMode.ClampToEdge;
	/// <summary>Magnification filter.</summary>
	public FilterMode MagFilter { get; init; } = FilterMode.Nearest;
	/// <summary>Minification filter.</summary>
	public FilterMode MinFilter { get; init; } = FilterMode.Nearest;
	/// <summary>Filter between mip levels.</summary>
	public FilterMode MipmapFilter { get; init; } = FilterMode.Nearest;
	/// <summary>Smallest level of detail.</summary>
	public float LodMinClamp { get; init; }
	/// <summary>Largest level of detail.</summary>
	public float LodMaxClamp { get; init; } = 32f;
	/// <summary>A comparison sampler's function (shadow maps); null for an ordinary sampler.</summary>
	public CompareFunction? Compare { get; init; }
	/// <summary>A debug label.</summary>
	public string? Label { get; init; }
}

/// <summary>
/// Describes a shader module for <see cref="IGraphicsDevice.CreateShaderModule"/>: one compiled stage in the backend's
/// language (<see cref="IGraphicsDevice.ShaderLanguage"/>). Ion compiles shaders at build time and embeds them; nothing is
/// compiled at run time.
/// </summary>
/// <param name="Code">SPIR-V words (as bytes) for Vulkan, or GLSL ES 3.10 source (UTF-8) for GLES.</param>
/// <param name="Stage">The stage the module is for (<see cref="ShaderStage.Vertex"/> or <see cref="ShaderStage.Fragment"/>).</param>
/// <param name="Language">The language of <paramref name="Code"/>.</param>
/// <param name="Label">A debug label.</param>
public readonly record struct ShaderModuleDescriptor(ReadOnlyMemory<byte> Code, ShaderStage Stage, ShaderLanguage Language = ShaderLanguage.SpirV, string? Label = null);

/// <summary>
/// One entry of a bind group layout.
/// </summary>
/// <param name="Binding">The binding number inside the group (<c>layout(set = group, binding = N)</c> in GLSL).</param>
/// <param name="Visibility">The stages that read it.</param>
/// <param name="Type">What it holds.</param>
public readonly record struct BindGroupLayoutEntry(uint Binding, ShaderStage Visibility, BindingType Type);

/// <summary>Describes a bind group layout for <see cref="IGraphicsDevice.CreateBindGroupLayout"/>.</summary>
public readonly record struct BindGroupLayoutDescriptor(BindGroupLayoutEntry[] Entries, string? Label = null);

/// <summary>
/// One resource of a bind group: a buffer range, a sampler or a texture view, matching the layout entry with the same
/// <see cref="Binding"/>.
/// </summary>
public readonly record struct BindGroupEntry
{
	/// <summary>The binding number.</summary>
	public uint Binding { get; init; }
	/// <summary>The buffer, for buffer bindings.</summary>
	public IBuffer? Buffer { get; init; }
	/// <summary>The byte offset into <see cref="Buffer"/>.</summary>
	public ulong Offset { get; init; }
	/// <summary>The byte size of the bound range; 0 binds to the end of the buffer.</summary>
	public ulong Size { get; init; }
	/// <summary>The sampler, for sampler bindings.</summary>
	public ISampler? Sampler { get; init; }
	/// <summary>The texture view, for texture bindings.</summary>
	public ITextureView? TextureView { get; init; }

	/// <summary>A buffer binding.</summary>
	public static BindGroupEntry ForBuffer(uint binding, IBuffer buffer, ulong offset = 0, ulong size = 0) => new() { Binding = binding, Buffer = buffer, Offset = offset, Size = size };

	/// <summary>A sampler binding.</summary>
	public static BindGroupEntry ForSampler(uint binding, ISampler sampler) => new() { Binding = binding, Sampler = sampler };

	/// <summary>A texture binding.</summary>
	public static BindGroupEntry ForTexture(uint binding, ITextureView view) => new() { Binding = binding, TextureView = view };
}

/// <summary>Describes a bind group for <see cref="IGraphicsDevice.CreateBindGroup"/>.</summary>
public readonly record struct BindGroupDescriptor(IBindGroupLayout Layout, BindGroupEntry[] Entries, string? Label = null);

/// <summary>Describes a pipeline layout (the bind group layouts of groups 0, 1, ...) for <see cref="IGraphicsDevice.CreatePipelineLayout"/>.</summary>
public readonly record struct PipelineLayoutDescriptor(IBindGroupLayout[] BindGroupLayouts, string? Label = null);

/// <summary>One attribute of a vertex buffer layout.</summary>
/// <param name="Format">The attribute format.</param>
/// <param name="Offset">The byte offset inside one element.</param>
/// <param name="ShaderLocation">The <c>layout(location = N)</c> of the vertex shader input.</param>
public readonly record struct VertexAttribute(VertexFormat Format, uint Offset, uint ShaderLocation);

/// <summary>The layout of one vertex buffer slot.</summary>
/// <param name="ArrayStride">The byte size of one element.</param>
/// <param name="StepMode">Per vertex or per instance.</param>
/// <param name="Attributes">The attributes read from it.</param>
public readonly record struct VertexBufferLayout(uint ArrayStride, VertexStepMode StepMode, VertexAttribute[] Attributes);

/// <summary>The vertex stage of a render pipeline.</summary>
public readonly record struct VertexState(IShaderModule Module, VertexBufferLayout[] Buffers, string EntryPoint = "main");

/// <summary>One blend equation (color or alpha).</summary>
public readonly record struct BlendComponent(BlendFactor SrcFactor, BlendFactor DstFactor, BlendOperation Operation = BlendOperation.Add)
{
	/// <summary>Source replaces destination.</summary>
	public static BlendComponent Replace => new(BlendFactor.One, BlendFactor.Zero);
}

/// <summary>
/// The blend state of a color target.
/// </summary>
public readonly record struct BlendState(BlendComponent Color, BlendComponent Alpha)
{
	/// <summary>Straight (non-premultiplied) alpha blending.</summary>
	public static BlendState AlphaBlend => new(new(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha), new(BlendFactor.One, BlendFactor.OneMinusSrcAlpha));

	/// <summary>Premultiplied alpha blending.</summary>
	public static BlendState PremultipliedAlpha => new(new(BlendFactor.One, BlendFactor.OneMinusSrcAlpha), new(BlendFactor.One, BlendFactor.OneMinusSrcAlpha));

	/// <summary>Additive blending.</summary>
	public static BlendState Additive => new(new(BlendFactor.SrcAlpha, BlendFactor.One), new(BlendFactor.SrcAlpha, BlendFactor.One));
}

/// <summary>One color target of a render pipeline.</summary>
/// <param name="Format">The format of the attachment the pipeline renders into.</param>
/// <param name="Blend">The blend state, or null for no blending (opaque).</param>
/// <param name="WriteMask">The channels written.</param>
public readonly record struct ColorTargetState(TextureFormat Format, BlendState? Blend = null, ColorWriteMask WriteMask = ColorWriteMask.All);

/// <summary>The fragment stage of a render pipeline.</summary>
public readonly record struct FragmentState(IShaderModule Module, ColorTargetState[] Targets, string EntryPoint = "main");

/// <summary>Primitive assembly and rasterization.</summary>
public readonly record struct PrimitiveState
{
	/// <summary>Creates the default state: triangle list, counter-clockwise front faces, no culling.</summary>
	public PrimitiveState() { }

	/// <summary>The topology.</summary>
	public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
	/// <summary>The front face winding.</summary>
	public FrontFace FrontFace { get; init; } = FrontFace.Ccw;
	/// <summary>Face culling.</summary>
	public CullMode CullMode { get; init; } = CullMode.None;
}

/// <summary>Depth test and write state (stencil is not exposed yet).</summary>
/// <param name="Format">The depth attachment format.</param>
/// <param name="DepthWriteEnabled">Whether passing fragments write depth.</param>
/// <param name="DepthCompare">The depth test.</param>
public readonly record struct DepthStencilState(TextureFormat Format, bool DepthWriteEnabled = true, CompareFunction DepthCompare = CompareFunction.Less);

/// <summary>Describes a render pipeline for <see cref="IGraphicsDevice.CreateRenderPipeline"/>.</summary>
public sealed record RenderPipelineDescriptor
{
	/// <summary>The pipeline layout (the bind group layouts it uses).</summary>
	public required IPipelineLayout Layout { get; init; }
	/// <summary>The vertex stage and vertex buffer layouts.</summary>
	public required VertexState Vertex { get; init; }
	/// <summary>The fragment stage and color targets; null for a depth-only pipeline.</summary>
	public FragmentState? Fragment { get; init; }
	/// <summary>Primitive assembly.</summary>
	public PrimitiveState Primitive { get; init; } = new();
	/// <summary>Depth state, or null when the pass has no depth attachment.</summary>
	public DepthStencilState? DepthStencil { get; init; }
	/// <summary>The sample count of the attachments (1: no multisampling).</summary>
	public uint SampleCount { get; init; } = 1;
	/// <summary>A debug label.</summary>
	public string? Label { get; init; }
}

/// <summary>One color attachment of a render pass.</summary>
/// <param name="View">The view rendered into.</param>
/// <param name="LoadOp">Clear or keep the previous contents.</param>
/// <param name="StoreOp">Keep or discard the result.</param>
/// <param name="ClearValue">The clear color (linear RGBA in [0, 1]) for <see cref="LoadOp.Clear"/>.</param>
public readonly record struct RenderPassColorAttachment(ITextureView View, LoadOp LoadOp, StoreOp StoreOp, Vector4 ClearValue = default);

/// <summary>The depth attachment of a render pass.</summary>
public readonly record struct RenderPassDepthStencilAttachment(ITextureView View, LoadOp DepthLoadOp = LoadOp.Clear, StoreOp DepthStoreOp = StoreOp.Discard, float DepthClearValue = 1f);

/// <summary>
/// Describes a render pass for <see cref="ICommandEncoder.BeginRenderPass"/>. A <c>ref struct</c> so the attachment list
/// can be a stack-allocated span: <c>new RenderPassDescriptor([attachment])</c> allocates nothing.
/// </summary>
public readonly ref struct RenderPassDescriptor
{
	/// <summary>Creates a pass.</summary>
	public RenderPassDescriptor(ReadOnlySpan<RenderPassColorAttachment> colorAttachments, RenderPassDepthStencilAttachment? depthStencilAttachment = null, string? label = null)
	{
		ColorAttachments = colorAttachments;
		DepthStencilAttachment = depthStencilAttachment;
		Label = label;
	}

	/// <summary>The color attachments (at most 4 for portability to GLES 3.1).</summary>
	public ReadOnlySpan<RenderPassColorAttachment> ColorAttachments { get; }
	/// <summary>The depth attachment, if any.</summary>
	public RenderPassDepthStencilAttachment? DepthStencilAttachment { get; }
	/// <summary>A debug label.</summary>
	public string? Label { get; }
}

/// <summary>A rectangle of a texture's mip level (and, for a cube map, of one face: <paramref name="ArrayLayer"/> 0 to 5), for copies.</summary>
public readonly record struct TextureRegion(uint X, uint Y, uint Width, uint Height, uint MipLevel = 0, uint ArrayLayer = 0)
{
	/// <summary>The whole of mip level 0 of <paramref name="texture"/>.</summary>
	public static TextureRegion Whole(ITexture texture) => new(0, 0, texture.Width, texture.Height);
}

/// <summary>Configures a surface for <see cref="ISurface.Configure"/>.</summary>
/// <param name="Width">The width in pixels (the framebuffer size, not the window size).</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="PresentMode">How frames are presented; unsupported modes fall back to <see cref="PresentMode.Fifo"/>.</param>
/// <param name="Format">The preferred format, or <see cref="TextureFormat.Undefined"/> for the backend's choice (see <see cref="ISurface.Format"/>).</param>
public readonly record struct SurfaceConfiguration(uint Width, uint Height, PresentMode PresentMode = PresentMode.Fifo, TextureFormat Format = TextureFormat.Undefined);

/// <summary>The result of <see cref="ISurface.GetCurrentTexture"/>.</summary>
/// <param name="Texture">The acquired texture, or null when <paramref name="Status"/> is <see cref="SurfaceTextureStatus.Outdated"/>.</param>
/// <param name="Status">Whether a texture was acquired.</param>
public readonly record struct SurfaceTexture(ITexture? Texture, SurfaceTextureStatus Status);

/// <summary>Limits of a device that portable code must respect.</summary>
public readonly record struct DeviceLimits
{
	/// <summary>The largest texture width or height.</summary>
	public uint MaxTextureDimension2D { get; init; }
	/// <summary>The alignment of uniform buffer binding offsets.</summary>
	public ulong MinUniformBufferOffsetAlignment { get; init; }
	/// <summary>The largest uniform buffer binding.</summary>
	public ulong MaxUniformBufferBindingSize { get; init; }
	/// <summary>The number of bind groups a pipeline may use (Ion guarantees 4).</summary>
	public uint MaxBindGroups { get; init; }
	/// <summary>Whether storage buffers may be read in the vertex stage (false on some GLES 3.1 devices).</summary>
	public bool VertexStorageBuffers { get; init; }
}
