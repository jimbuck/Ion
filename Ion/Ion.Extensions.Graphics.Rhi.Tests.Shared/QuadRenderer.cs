using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Examples.Quad;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// A configurable textured quad for contract tests, on the quad sample's shaders (<c>textured_quad.vert/.frag</c>: a
/// transform uniform at group 0 binding 0, a texture at binding 1, a sampler at binding 2): any texture view and sampler,
/// any cull mode, and a vertex buffer holding two quads (left half and right half of clip space, four vertices each) so
/// base vertex draws can pick one.
/// </summary>
public sealed class QuadRenderer : IDisposable
{
	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _fragment;
	private readonly IBindGroupLayout _layout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly IRenderPipeline _pipeline;
	private readonly IBuffer _vertices;
	private readonly IBuffer _indices;
	private readonly IBuffer _uniforms;
	private readonly IBindGroup _bindGroup;

	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct Vertex(Vector2 Position, Vector2 Uv);

	/// <summary>
	/// Creates the quad. Vertices 0 to 3 span the whole clip space (UV 0 to <paramref name="uvScale"/>), vertices 4 to 7 the
	/// right half only; indices 0-1-2, 0-2-3 wind clockwise in y-up clip space (the checkerboard sample's winding).
	/// </summary>
	public QuadRenderer(IGraphicsDevice device, TextureFormat colorFormat, ITextureView texture, ISampler sampler, CullMode cullMode = CullMode.None, float uvScale = 1f)
	{
		_device = device;
		var assembly = typeof(TexturedQuad).Assembly;
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.frag"));
		_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Vertex, BindingType.UniformBuffer),
			new(1, ShaderStage.Fragment, BindingType.Texture),
			new(2, ShaderStage.Fragment, BindingType.Sampler),
		]));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));
		_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = "Contract quad",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout(16, VertexStepMode.Vertex, [new VertexAttribute(VertexFormat.Float32x2, 0, 0), new VertexAttribute(VertexFormat.Float32x2, 8, 1)]),
			]),
			Fragment = new FragmentState(_fragment, [new ColorTargetState(colorFormat)]),
			Primitive = new PrimitiveState { CullMode = cullMode },
		});

		ReadOnlySpan<Vertex> vertices =
		[
			new(new(-1, 1), new(0, 0)), new(new(1, 1), new(uvScale, 0)), new(new(1, -1), new(uvScale, uvScale)), new(new(-1, -1), new(0, uvScale)),
			new(new(0, 1), new(0, 0)), new(new(1, 1), new(uvScale, 0)), new(new(1, -1), new(uvScale, uvScale)), new(new(0, -1), new(0, uvScale)),
		];
		ReadOnlySpan<ushort> indices = [0, 1, 2, 0, 2, 3];
		_vertices = device.CreateBuffer(new BufferDescriptor((ulong)(vertices.Length * 16), BufferUsage.Vertex | BufferUsage.CopyDst));
		_indices = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Index | BufferUsage.CopyDst));
		_uniforms = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.Uniform | BufferUsage.CopyDst));
		device.Queue.WriteBuffer(_vertices, 0, vertices);
		device.Queue.WriteBuffer(_indices, 0, indices);
		device.Queue.WriteBuffer(_uniforms, 0, Matrix4x4.Identity);
		_bindGroup = device.CreateBindGroup(new BindGroupDescriptor(_layout,
		[
			BindGroupEntry.ForBuffer(0, _uniforms),
			BindGroupEntry.ForTexture(1, texture),
			BindGroupEntry.ForSampler(2, sampler),
		]));
	}

	/// <summary>Sets the clip-space transform.</summary>
	public void SetTransform(in Matrix4x4 transform) => _device.Queue.WriteBuffer(_uniforms, 0, transform);

	/// <summary>
	/// Records and submits one pass into <paramref name="target"/>: clears to <paramref name="clear"/>, applies the optional
	/// viewport and scissor, and draws the quad at <paramref name="baseVertex"/> (0: the full quad, 4: the right half).
	/// </summary>
	public void Draw(ITexture target, Vector4 clear, int baseVertex = 0, (float X, float Y, float W, float H)? viewport = null, (uint X, uint Y, uint W, uint H)? scissor = null)
	{
		var encoder = _device.CreateCommandEncoder("Contract quad");
		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, clear)]));
		// Bind groups and buffers before the pipeline: the RHI applies them at draw time, as WebGPU does.
		pass.SetBindGroup(0, _bindGroup);
		pass.SetVertexBuffer(0, _vertices);
		pass.SetIndexBuffer(_indices, IndexFormat.Uint16);
		pass.SetPipeline(_pipeline);
		if (viewport is { } v) pass.SetViewport(v.X, v.Y, v.W, v.H);
		if (scissor is { } s) pass.SetScissorRect(s.X, s.Y, s.W, s.H);
		pass.DrawIndexed(6, 1, 0, baseVertex);
		pass.End();
		_device.Queue.Submit(encoder.Finish());
	}

	/// <summary>Releases the GPU resources.</summary>
	public void Dispose()
	{
		_bindGroup.Dispose();
		_uniforms.Dispose();
		_indices.Dispose();
		_vertices.Dispose();
		_pipeline.Dispose();
		_pipelineLayout.Dispose();
		_layout.Dispose();
		_fragment.Dispose();
		_vertex.Dispose();
	}
}
