using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics.Rhi;

/*
 * The render hardware interface (RHI): a thin, explicit abstraction shaped like WebGPU, which is the common subset of
 * Vulkan, Metal and Direct3D 12 and maps onto OpenGL ES 3.1. Renderers (2D, 3D) are written once against it; nothing
 * backend-specific appears above it.
 *
 * Mapping onto OpenGL ES 3.1 (the GLES backend), which constrains what portable code may do:
 * - Bind groups have no GLES equivalent. The backend flattens (group, binding) pairs: uniform buffer entries become uniform
 *   block binding points and texture entries become texture units, numbered in group order. A Sampler entry is combined
 *   with the Texture entry of the same group into one texture unit (SPIRV-Cross builds combined image samplers at build
 *   time), so a group should pair each texture with one sampler. Keep to 4 bind groups and 16 texture units.
 * - Storage buffers are not guaranteed in the vertex stage (DeviceLimits.VertexStorageBuffers). Per-instance data goes
 *   through vertex buffers with VertexStepMode.Instance.
 * - Render passes become framebuffer binds; LoadOp.Clear is a glClear, StoreOp.Discard a glInvalidateFramebuffer.
 * - Shader modules are GLSL ES 3.10 source translated from the same SPIR-V at build time.
 */

/// <summary>
/// A graphics device: creates every GPU resource and owns the <see cref="Queue"/> and, when windowed, the
/// <see cref="Surface"/>. One per application, registered as a singleton by the backend.
/// </summary>
/// <remarks>
/// <para>
/// Frames: unlike WebGPU, frames in flight are explicit. The engine's graphics system calls <see cref="BeginFrame"/> at the
/// start of the Render stage (it waits until the GPU has finished the frame that last used this frame slot, then recycles
/// that frame's command buffers, staging memory and deferred deletions) and <see cref="EndFrame"/> at its end (after
/// <see cref="ISurface.Present"/>). Code that renders inside the Render stage never calls them.
/// </para>
/// <para>
/// Lifetime: every object is <see cref="IDisposable"/>. Disposing one that the GPU may still use is safe: the backend
/// defers the destruction until the frames in flight have completed.
/// </para>
/// <para>
/// Threading: the device and everything created from it are used from the main thread (the game loop's thread).
/// </para>
/// </remarks>
public interface IGraphicsDevice : IDisposable
{
	/// <summary>The graphics API behind this device.</summary>
	GraphicsBackend Backend { get; }

	/// <summary>The adapter (GPU) name, for logs.</summary>
	string AdapterName { get; }

	/// <summary>The language <see cref="CreateShaderModule"/> accepts.</summary>
	ShaderLanguage ShaderLanguage { get; }

	/// <summary>Limits that portable code must respect.</summary>
	DeviceLimits Limits { get; }

	/// <summary>The number of frames the CPU may record ahead of the GPU (2 or 3).</summary>
	int FramesInFlight { get; }

	/// <summary>The index of the current frame slot, from 0 to <see cref="FramesInFlight"/> minus one. Per-frame ring buffers index with it.</summary>
	int FrameIndex { get; }

	/// <summary>The queue commands are submitted to.</summary>
	IQueue Queue { get; }

	/// <summary>The window surface, or null for a headless (offscreen) device.</summary>
	ISurface? Surface { get; }

	/// <summary>Creates a buffer.</summary>
	IBuffer CreateBuffer(in BufferDescriptor descriptor);

	/// <summary>Creates a 2D texture.</summary>
	ITexture CreateTexture(in TextureDescriptor descriptor);

	/// <summary>Creates a sampler.</summary>
	ISampler CreateSampler(in SamplerDescriptor descriptor);

	/// <summary>Creates a shader module from code in <see cref="ShaderLanguage"/>.</summary>
	IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor);

	/// <summary>Creates a bind group layout.</summary>
	IBindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor);

	/// <summary>Creates a bind group: the resources bound to one group index.</summary>
	IBindGroup CreateBindGroup(in BindGroupDescriptor descriptor);

	/// <summary>Creates a pipeline layout.</summary>
	IPipelineLayout CreatePipelineLayout(in PipelineLayoutDescriptor descriptor);

	/// <summary>Creates a render pipeline. Pipelines are expensive: create them at load time, not per frame.</summary>
	IRenderPipeline CreateRenderPipeline(RenderPipelineDescriptor descriptor);

	/// <summary>Creates a command encoder for this frame. Record into it, <see cref="ICommandEncoder.Finish"/> it and submit the result.</summary>
	ICommandEncoder CreateCommandEncoder(string? label = null);

	/// <summary>Starts a frame: waits for the frame slot to be free and recycles its per-frame resources.</summary>
	void BeginFrame();

	/// <summary>Ends a frame: submits pending work and advances <see cref="FrameIndex"/>.</summary>
	void EndFrame();

	/// <summary>Submits pending work and runs completed deferred work without blocking (<paramref name="wait"/> false), or blocks until the GPU is idle.</summary>
	void Poll(bool wait = false);
}

/// <summary>
/// The device queue: submits command buffers and uploads data.
/// </summary>
/// <remarks>
/// Writes are ordered with submissions as in WebGPU: a <see cref="WriteBuffer"/> or <see cref="WriteTexture"/> is seen by
/// every command buffer submitted after it and by none submitted before it. Data is copied into per-frame staging memory at
/// the call, so the source span can be reused immediately.
/// </remarks>
public interface IQueue
{
	/// <summary>Submits one command buffer.</summary>
	void Submit(ICommandBuffer commandBuffer);

	/// <summary>Submits command buffers in order.</summary>
	void Submit(ReadOnlySpan<ICommandBuffer> commandBuffers);

	/// <summary>Writes <paramref name="data"/> into <paramref name="buffer"/> (which needs <see cref="BufferUsage.CopyDst"/>) at <paramref name="offset"/>.</summary>
	void WriteBuffer(IBuffer buffer, ulong offset, ReadOnlySpan<byte> data);

	/// <summary>
	/// Writes rows of texels into a region of <paramref name="texture"/> (which needs <see cref="TextureUsage.CopyDst"/>).
	/// <paramref name="data"/> holds <paramref name="region"/>.Height rows of <paramref name="bytesPerRow"/> bytes, top row first.
	/// </summary>
	void WriteTexture(ITexture texture, ReadOnlySpan<byte> data, uint bytesPerRow, TextureRegion region);

	/// <summary>Blocks until every submitted command buffer has completed on the GPU.</summary>
	void WaitIdle();
}

/// <summary>
/// Extension methods for <see cref="IQueue"/>.
/// </summary>
public static class QueueExtensions
{
	/// <summary>Writes a span of unmanaged values into <paramref name="buffer"/> at <paramref name="offset"/>.</summary>
	public static void WriteBuffer<T>(this IQueue queue, IBuffer buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged =>
		queue.WriteBuffer(buffer, offset, MemoryMarshal.AsBytes(data));

	/// <summary>Writes one unmanaged value into <paramref name="buffer"/> at <paramref name="offset"/>.</summary>
	public static void WriteBuffer<T>(this IQueue queue, IBuffer buffer, ulong offset, in T value) where T : unmanaged =>
		queue.WriteBuffer(buffer, offset, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

	/// <summary>Writes tightly packed texels to the whole of mip level 0 of <paramref name="texture"/>.</summary>
	public static void WriteTexture(this IQueue queue, ITexture texture, ReadOnlySpan<byte> data) =>
		queue.WriteTexture(texture, data, texture.Width * (uint)texture.Format.BytesPerPixel(), TextureRegion.Whole(texture));
}

/// <summary>A GPU buffer.</summary>
public interface IBuffer : IDisposable
{
	/// <summary>The size in bytes.</summary>
	ulong Size { get; }

	/// <summary>The usages it was created with.</summary>
	BufferUsage Usage { get; }

	/// <summary>
	/// Copies bytes out of a <see cref="BufferUsage.MapRead"/> buffer. Blocks until every submitted command buffer has
	/// completed (so the copy that filled it is visible). For readbacks and tests, not for per-frame use.
	/// </summary>
	void Read(ulong offset, Span<byte> destination);
}

/// <summary>A 2D texture (optionally mipmapped or multisampled).</summary>
public interface ITexture : IDisposable
{
	/// <summary>The width of mip level 0 in texels.</summary>
	uint Width { get; }

	/// <summary>The height of mip level 0 in texels.</summary>
	uint Height { get; }

	/// <summary>The texel format.</summary>
	TextureFormat Format { get; }

	/// <summary>The usages it was created with.</summary>
	TextureUsage Usage { get; }

	/// <summary>The number of mip levels.</summary>
	uint MipLevelCount { get; }

	/// <summary>The sample count (1 unless multisampled).</summary>
	uint SampleCount { get; }

	/// <summary>A view of every mip level, created on first use and owned by the texture.</summary>
	ITextureView DefaultView { get; }

	/// <summary>Creates a view of some mip levels. The caller owns it.</summary>
	ITextureView CreateView(in TextureViewDescriptor descriptor);
}

/// <summary>A view of a texture, bound in bind groups and used as a render pass attachment.</summary>
public interface ITextureView : IDisposable
{
	/// <summary>The viewed texture.</summary>
	ITexture Texture { get; }

	/// <summary>The view format (the texture's format).</summary>
	TextureFormat Format { get; }
}

/// <summary>A sampler.</summary>
public interface ISampler : IDisposable;

/// <summary>A compiled shader stage.</summary>
public interface IShaderModule : IDisposable
{
	/// <summary>The stage.</summary>
	ShaderStage Stage { get; }
}

/// <summary>
/// The shape of one bind group: which binding numbers hold which kind of resource, visible to which stages. GLES: see the
/// mapping notes at the top of this file (uniform block binding points and texture units).
/// </summary>
public interface IBindGroupLayout : IDisposable
{
	/// <summary>The entries.</summary>
	IReadOnlyList<BindGroupLayoutEntry> Entries { get; }
}

/// <summary>
/// A set of resources bound together at one group index with <see cref="IRenderPassEncoder.SetBindGroup"/>. Bind groups
/// are immutable; create them at load time (one per material or texture) and reuse them every frame.
/// </summary>
public interface IBindGroup : IDisposable
{
	/// <summary>The layout it was created with.</summary>
	IBindGroupLayout Layout { get; }
}

/// <summary>The bind group layouts a pipeline uses, by group index.</summary>
public interface IPipelineLayout : IDisposable;

/// <summary>A compiled render pipeline: shaders, vertex layout, primitive, depth and blend state.</summary>
public interface IRenderPipeline : IDisposable;

/// <summary>A finished, submittable command buffer. Submit it once; it is recycled after its frame completes.</summary>
public interface ICommandBuffer;

/// <summary>
/// Records commands: render passes and copies. Created per frame by <see cref="IGraphicsDevice.CreateCommandEncoder"/>,
/// finished with <see cref="Finish"/>.
/// </summary>
public interface ICommandEncoder
{
	/// <summary>
	/// Begins a render pass. Record draws into the returned encoder and call <see cref="IRenderPassEncoder.End"/> before any
	/// other command on this encoder.
	/// </summary>
	IRenderPassEncoder BeginRenderPass(in RenderPassDescriptor descriptor);

	/// <summary>Copies <paramref name="size"/> bytes between buffers (<see cref="BufferUsage.CopySrc"/> to <see cref="BufferUsage.CopyDst"/>).</summary>
	void CopyBufferToBuffer(IBuffer source, ulong sourceOffset, IBuffer destination, ulong destinationOffset, ulong size);

	/// <summary>
	/// Copies a region of <paramref name="source"/> (which needs <see cref="TextureUsage.CopySrc"/>) into
	/// <paramref name="destination"/> as rows of <paramref name="bytesPerRow"/> bytes, top row first.
	/// </summary>
	void CopyTextureToBuffer(ITexture source, TextureRegion region, IBuffer destination, ulong destinationOffset, uint bytesPerRow);

	/// <summary>Finishes recording. The encoder cannot be used afterwards.</summary>
	ICommandBuffer Finish();
}

/// <summary>
/// Records the draws of one render pass. Viewport and scissor are dynamic state and start as the full attachment.
/// </summary>
public interface IRenderPassEncoder
{
	/// <summary>Sets the pipeline for the following draws.</summary>
	void SetPipeline(IRenderPipeline pipeline);

	/// <summary>Binds <paramref name="group"/> at group index <paramref name="index"/> (<c>layout(set = index)</c>).</summary>
	void SetBindGroup(uint index, IBindGroup group);

	/// <summary>Binds a vertex buffer to vertex buffer slot <paramref name="slot"/>.</summary>
	void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0);

	/// <summary>Binds the index buffer.</summary>
	void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0);

	/// <summary>Sets the viewport in pixels (origin top-left).</summary>
	void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f);

	/// <summary>Sets the scissor rectangle in pixels (origin top-left).</summary>
	void SetScissorRect(uint x, uint y, uint width, uint height);

	/// <summary>Draws non-indexed primitives.</summary>
	void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);

	/// <summary>Draws indexed primitives.</summary>
	void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);

	/// <summary>Ends the pass.</summary>
	void End();
}

/// <summary>
/// A window's presentable surface (a Vulkan swapchain, the GLES default framebuffer).
/// </summary>
/// <remarks>
/// Per frame: <see cref="GetCurrentTexture"/>, render into its view, submit, then <see cref="Present"/>. The engine's
/// graphics system does this and reconfigures the surface when the window's framebuffer is resized; games render through
/// <see cref="IGraphicsFrame"/> instead of using the surface directly.
/// </remarks>
public interface ISurface
{
	/// <summary>The format of the surface textures (for pipeline color targets).</summary>
	TextureFormat Format { get; }

	/// <summary>The width in pixels.</summary>
	uint Width { get; }

	/// <summary>The height in pixels.</summary>
	uint Height { get; }

	/// <summary>(Re)configures the surface: size in pixels, present mode, format.</summary>
	void Configure(in SurfaceConfiguration configuration);

	/// <summary>Acquires the texture to render this frame into.</summary>
	SurfaceTexture GetCurrentTexture();

	/// <summary>Presents the acquired texture. Submits pending work first.</summary>
	void Present();
}
