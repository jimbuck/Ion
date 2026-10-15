using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Examples.Quad;

/// <summary>
/// A textured quad drawn through the RHI only: one pipeline, one bind group (uniform buffer, texture, sampler), a vertex
/// and an index buffer. Backend-independent; the tests use it to prove the Vulkan and headless backends.
/// </summary>
public sealed class TexturedQuad : IDisposable
{
	/// <summary>The checkerboard's first color (texel (0, 0)).</summary>
	public static readonly Rgba8 ColorA = new(255, 64, 0, 255);

	/// <summary>The checkerboard's second color (texel (1, 0)).</summary>
	public static readonly Rgba8 ColorB = new(0, 96, 255, 255);

	/// <summary>The checkerboard size in texels.</summary>
	public const int CheckerSize = 4;

	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _fragment;
	private readonly IBindGroupLayout _layout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly IRenderPipeline _pipeline;
	private readonly IBuffer _vertices;
	private readonly IBuffer _indices;
	private readonly IBuffer _uniforms;
	private readonly ITexture _texture;
	private readonly ISampler _sampler;
	private readonly IBindGroup _bindGroup;

	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct Vertex(Vector2 Position, Vector2 Uv);

	/// <summary>
	/// Creates the GPU resources for rendering into targets of <paramref name="colorFormat"/> (and
	/// <paramref name="depthFormat"/>, or <see cref="TextureFormat.Undefined"/> for none). The quad spans
	/// [-<paramref name="halfSize"/>, <paramref name="halfSize"/>] in clip space.
	/// </summary>
	public TexturedQuad(IGraphicsDevice device, TextureFormat colorFormat, TextureFormat depthFormat = TextureFormat.Undefined, float halfSize = 0.5f)
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
		], "Quad"));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));

		_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = "Textured quad",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout((uint)Marshal.SizeOf<Vertex>(), VertexStepMode.Vertex,
				[
					new VertexAttribute(VertexFormat.Float32x2, 0, 0),
					new VertexAttribute(VertexFormat.Float32x2, 8, 1),
				]),
			]),
			Fragment = new FragmentState(_fragment, [new ColorTargetState(colorFormat, BlendState.AlphaBlend)]),
			DepthStencil = depthFormat == TextureFormat.Undefined ? null : new DepthStencilState(depthFormat, DepthWriteEnabled: false, DepthCompare: CompareFunction.Always),
		});

		// Top-left of the quad has UV (0, 0), so texel row 0 is at the top of the screen (clip space is y up).
		ReadOnlySpan<Vertex> vertices =
		[
			new(new(-halfSize, halfSize), new(0, 0)),
			new(new(halfSize, halfSize), new(1, 0)),
			new(new(halfSize, -halfSize), new(1, 1)),
			new(new(-halfSize, -halfSize), new(0, 1)),
		];
		ReadOnlySpan<ushort> indices = [0, 1, 2, 0, 2, 3];

		_vertices = device.CreateBuffer(new BufferDescriptor((ulong)(vertices.Length * Marshal.SizeOf<Vertex>()), BufferUsage.Vertex | BufferUsage.CopyDst, "Quad vertices"));
		_indices = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Index | BufferUsage.CopyDst, "Quad indices"));
		_uniforms = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.Uniform | BufferUsage.CopyDst, "Quad transform"));
		device.Queue.WriteBuffer(_vertices, 0, vertices);
		device.Queue.WriteBuffer(_indices, 0, indices);
		device.Queue.WriteBuffer(_uniforms, 0, Matrix4x4.Identity);

		_texture = device.CreateTexture(new TextureDescriptor(CheckerSize, CheckerSize, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Checkerboard"));
		device.Queue.WriteTexture(_texture, Checkerboard());
		_sampler = device.CreateSampler(SamplerDescriptor.PointClamp);

		_bindGroup = device.CreateBindGroup(new BindGroupDescriptor(_layout,
		[
			BindGroupEntry.ForBuffer(0, _uniforms),
			BindGroupEntry.ForTexture(1, _texture.DefaultView),
			BindGroupEntry.ForSampler(2, _sampler),
		]));
	}

	/// <summary>The RGBA8 texels of the checkerboard: <see cref="ColorA"/> where x + y is even, <see cref="ColorB"/> elsewhere.</summary>
	public static byte[] Checkerboard()
	{
		var pixels = new byte[CheckerSize * CheckerSize * 4];
		for (var y = 0; y < CheckerSize; y++)
		{
			for (var x = 0; x < CheckerSize; x++)
			{
				var c = (x + y) % 2 == 0 ? ColorA : ColorB;
				var i = (y * CheckerSize + x) * 4;
				pixels[i] = c.R;
				pixels[i + 1] = c.G;
				pixels[i + 2] = c.B;
				pixels[i + 3] = c.A;
			}
		}

		return pixels;
	}

	/// <summary>Sets the clip-space transform applied to the quad (identity by default).</summary>
	public void SetTransform(in Matrix4x4 transform) => _device.Queue.WriteBuffer(_uniforms, 0, transform);

	/// <summary>Records the quad into a pass on <paramref name="encoder"/> with the given attachments.</summary>
	public void Draw(ICommandEncoder encoder, RenderPassColorAttachment color, RenderPassDepthStencilAttachment? depth = null)
	{
		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([color], depth, "Quad"));
		pass.SetPipeline(_pipeline);
		pass.SetBindGroup(0, _bindGroup);
		pass.SetVertexBuffer(0, _vertices);
		pass.SetIndexBuffer(_indices, IndexFormat.Uint16);
		pass.DrawIndexed(6);
		pass.End();
	}

	/// <summary>Draws the quad into the current frame of <paramref name="frame"/> and submits it.</summary>
	public void Draw(IGraphicsFrame frame)
	{
		if (!frame.IsRendering) return;
		var encoder = _device.CreateCommandEncoder("Quad");
		Draw(encoder, frame.ColorAttachment(), frame.DepthAttachment());
		_device.Queue.Submit(encoder.Finish());
	}

	/// <summary>Releases the GPU resources.</summary>
	public void Dispose()
	{
		_bindGroup.Dispose();
		_sampler.Dispose();
		_texture.Dispose();
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
