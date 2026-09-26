using System.Reflection;

using Ion.Testing;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// The texture parts of the RHI contract that the 3D renderer relies on: cube maps (six faces written with
/// <see cref="TextureRegion.ArrayLayer"/> and sampled by direction) and depth textures rendered by a depth-only pipeline
/// and sampled through a comparison sampler (shadow maps). Run on every backend like <see cref="HeadlessContractTests"/>.
/// </summary>
public abstract class TextureContractTests
{
	private static readonly Vector4 Black = new(0, 0, 0, 1);

	/// <summary>The backend of the concrete test class (its <see cref="RhiBackendAttribute"/>).</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	/// <summary>Creates a validated headless device on <see cref="Backend"/>.</summary>
	protected virtual IGraphicsDevice CreateDevice(ValidationLog log) => log.CreateDevice(Backend, 2);

	private static readonly Rgba8[] FaceColors =
	[
		new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255),
		new(255, 255, 0, 255), new(0, 255, 255, 255), new(255, 0, 255, 255),
	];

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void CubeMapFacesAreWrittenByLayerAndSampledByDirection()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var cube = device.CreateTexture(new TextureDescriptor(2, 2, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, MipLevelCount: 2, Dimension: TextureDimension.Cube));
			Assert.Equal(TextureDimension.Cube, cube.Dimension);
			for (uint face = 0; face < 6; face++)
			{
				var c = FaceColors[face];
				var level0 = new byte[2 * 2 * 4];
				for (var i = 0; i < level0.Length; i += 4) (level0[i], level0[i + 1], level0[i + 2], level0[i + 3]) = (c.R, c.G, c.B, c.A);
				device.Queue.WriteTexture(cube, level0, 8, new TextureRegion(0, 0, 2, 2, 0, face));
				device.Queue.WriteTexture(cube, level0.AsSpan(0, 4), 4, new TextureRegion(0, 0, 1, 1, 1, face));
			}

			using var sampler = device.CreateSampler(SamplerDescriptor.LinearClamp);
			using var target = device.CreateTexture(new TextureDescriptor(12, 2, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var draw = new FullscreenDraw(device, "cube_faces.frag", [new(0, ShaderStage.Fragment, BindingType.Texture), new(1, ShaderStage.Fragment, BindingType.Sampler)],
				[BindGroupEntry.ForTexture(0, cube.DefaultView), BindGroupEntry.ForSampler(1, sampler)]);
			draw.Draw(target);
			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		for (var face = 0; face < 6; face++) QuadAssert.AssertPixel(shot, face * 2 + 1, 1, FaceColors[face], tolerance: 1);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void CubeMapsCannotBeRenderAttachments()
	{
		var log = new ValidationLog();
		using (var device = CreateDevice(log))
		{
			Assert.Throws<NotSupportedException>(() => device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment, Dimension: TextureDimension.Cube)));
			Assert.Throws<ArgumentException>(() => device.CreateTexture(new TextureDescriptor(4, 2, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding, Dimension: TextureDimension.Cube)));
		}

		log.AssertClean();
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ADepthTextureFromADepthOnlyPassIsSampledWithAComparisonSampler()
	{
		// Pass 1: a depth-only pipeline writes 0.25 on the left half and 0.75 on the right half. Pass 2 compares 0.5 with
		// the stored depth through a Less comparison sampler: passes (1) where 0.5 < stored, the right half.
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var depth = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Depth32Float, TextureUsage.RenderAttachment | TextureUsage.TextureBinding));
			using var vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, typeof(TextureContractTests).Assembly, "depth_quad.vert"));
			using var emptyLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([]));
			using var depthOnly = device.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Label = "Depth only",
				Layout = emptyLayout,
				Vertex = new VertexState(vertex, [new VertexBufferLayout(12, VertexStepMode.Vertex, [new VertexAttribute(VertexFormat.Float32x3, 0, 0)])]),
				DepthStencil = new DepthStencilState(TextureFormat.Depth32Float, true, CompareFunction.Less),
			});
			ReadOnlySpan<Vector3> positions =
			[
				new(-1, -1, 0.25f), new(0, -1, 0.25f), new(0, 1, 0.25f), new(-1, -1, 0.25f), new(0, 1, 0.25f), new(-1, 1, 0.25f),
				new(0, -1, 0.75f), new(1, -1, 0.75f), new(1, 1, 0.75f), new(0, -1, 0.75f), new(1, 1, 0.75f), new(0, 1, 0.75f),
			];
			using var vertices = device.CreateBuffer(new BufferDescriptor((ulong)(positions.Length * 12), BufferUsage.Vertex | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(vertices, 0, positions);

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginRenderPass(new RenderPassDescriptor([], new RenderPassDepthStencilAttachment(depth.DefaultView, LoadOp.Clear, StoreOp.Store, 1f)));
			pass.SetPipeline(depthOnly);
			pass.SetVertexBuffer(0, vertices);
			pass.Draw(12);
			pass.End();
			device.Queue.Submit(encoder.Finish());

			using var compare = device.CreateSampler(new SamplerDescriptor { MagFilter = FilterMode.Nearest, MinFilter = FilterMode.Nearest, Compare = CompareFunction.Less });
			using var target = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var draw = new FullscreenDraw(device, "depth_compare.frag", [new(0, ShaderStage.Fragment, BindingType.Texture), new(1, ShaderStage.Fragment, BindingType.Sampler)],
				[BindGroupEntry.ForTexture(0, depth.DefaultView), BindGroupEntry.ForSampler(1, compare)]);
			draw.Draw(target);
			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.AssertPixel(shot, 1, 4, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 6, 4, new Rgba8(255, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 1, 0, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 6, 7, new Rgba8(255, 0, 0, 255));
	}

	/// <summary>A full-target triangle with <c>fullscreen.vert</c> and a fragment shader reading one bind group.</summary>
	private sealed class FullscreenDraw : IDisposable
	{
		private readonly IGraphicsDevice _device;
		private readonly IShaderModule _vertex;
		private readonly IShaderModule _fragment;
		private readonly IBindGroupLayout _layout;
		private readonly IPipelineLayout _pipelineLayout;
		private readonly IRenderPipeline _pipeline;
		private readonly IBindGroup _group;

		public FullscreenDraw(IGraphicsDevice device, string fragment, BindGroupLayoutEntry[] layout, BindGroupEntry[] entries)
		{
			_device = device;
			var assembly = typeof(TextureContractTests).Assembly;
			_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "fullscreen.vert"));
			_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, fragment));
			_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(layout));
			_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));
			_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Label = fragment,
				Layout = _pipelineLayout,
				Vertex = new VertexState(_vertex, []),
				Fragment = new FragmentState(_fragment, [new ColorTargetState(TextureFormat.Rgba8Unorm)]),
			});
			_group = device.CreateBindGroup(new BindGroupDescriptor(_layout, entries));
		}

		public void Draw(ITexture target)
		{
			var encoder = _device.CreateCommandEncoder();
			var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, Black)]));
			pass.SetPipeline(_pipeline);
			pass.SetBindGroup(0, _group);
			pass.Draw(3);
			pass.End();
			_device.Queue.Submit(encoder.Finish());
		}

		public void Dispose()
		{
			_group.Dispose();
			_pipeline.Dispose();
			_pipelineLayout.Dispose();
			_layout.Dispose();
			_fragment.Dispose();
			_vertex.Dispose();
		}
	}
}
