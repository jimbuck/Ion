using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// The GPU half of the sprite batch: pipelines, samplers, the white texture, and per frame slot one instance buffer and
/// one uniform buffer (a ring of <see cref="IGraphicsDevice.FramesInFlight"/>, so a frame never writes a buffer the GPU
/// may still read and nothing waits for the device). Submits a frame recorded by a <see cref="SpriteBatcher"/> as one
/// command buffer: a render pass per run of segments with the same target, a draw call per <see cref="SpriteDraw"/>
/// (the instance range is bound as a vertex buffer offset, which GLES 3.1 supports without base-instance draws).
/// </summary>
internal sealed class SpriteRenderer : IDisposable
{
	private const int UniformSize = 80; // mat4 + vec4 (std140)
	private const int SamplerCount = 4;

	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _fragment;
	private readonly IBindGroupLayout _cameraLayout;
	private readonly IBindGroupLayout _textureLayout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly ISampler[] _samplers = new ISampler[SamplerCount];
	private readonly Dictionary<(SpriteBlendMode, TextureFormat), IRenderPipeline> _pipelines = [];
	private readonly FrameResources[] _frames;
	private readonly uint _uniformStride;
	private byte[] _uniformScratch = [];

	private sealed class FrameResources
	{
		public IBuffer? Instances;
		public IBuffer? Uniforms;
		public IBindGroup[] UniformGroups = [];
	}

	public SpriteRenderer(IGraphicsDevice device)
	{
		_device = device;
		var assembly = typeof(SpriteRenderer).Assembly;
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "sprite.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "sprite.frag"));

		_cameraLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Vertex, BindingType.UniformBuffer)], "Sprite camera"));
		_textureLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Fragment, BindingType.Texture),
			new(1, ShaderStage.Fragment, BindingType.Sampler),
		], "Sprite texture"));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_cameraLayout, _textureLayout], "Sprite"));

		_samplers[(int)SpriteSamplerMode.LinearClamp] = device.CreateSampler(SamplerDescriptor.LinearClamp with { Label = "Sprite linear clamp" });
		_samplers[(int)SpriteSamplerMode.PointClamp] = device.CreateSampler(SamplerDescriptor.PointClamp with { Label = "Sprite point clamp" });
		_samplers[(int)SpriteSamplerMode.LinearWrap] = device.CreateSampler(SamplerDescriptor.LinearClamp with { AddressModeU = AddressMode.Repeat, AddressModeV = AddressMode.Repeat, Label = "Sprite linear wrap" });
		_samplers[(int)SpriteSamplerMode.PointWrap] = device.CreateSampler(SamplerDescriptor.PointClamp with { AddressModeU = AddressMode.Repeat, AddressModeV = AddressMode.Repeat, Label = "Sprite point wrap" });

		var alignment = (uint)Math.Max(16ul, device.Limits.MinUniformBufferOffsetAlignment);
		_uniformStride = (UniformSize + alignment - 1) / alignment * alignment;

		_frames = new FrameResources[Math.Max(1, device.FramesInFlight)];
		for (var i = 0; i < _frames.Length; i++) _frames[i] = new FrameResources();

		var white = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Sprite white"));
		device.Queue.WriteTexture(white, [255, 255, 255, 255]);
		White = new Texture2D("White", white, 1, 1, 1, tracker: null);
	}

	/// <summary>The 1x1 white texture that rectangles, lines and points are drawn with.</summary>
	public Texture2D White { get; }

	/// <summary>Creates the pipeline for <paramref name="blend"/> into <paramref name="format"/> ahead of its first use.</summary>
	public IRenderPipeline GetPipeline(SpriteBlendMode blend, TextureFormat format)
	{
		if (_pipelines.TryGetValue((blend, format), out var pipeline)) return pipeline;

		BlendState? state = blend switch
		{
			SpriteBlendMode.AlphaBlend => BlendState.PremultipliedAlpha,
			SpriteBlendMode.Additive => new BlendState(new(BlendFactor.One, BlendFactor.One), new(BlendFactor.One, BlendFactor.One)),
			SpriteBlendMode.NonPremultiplied => BlendState.AlphaBlend,
			_ => null,
		};

		pipeline = _device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = $"Sprites {blend} {format}",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout(SpriteInstance.SizeInBytes, VertexStepMode.Instance,
				[
					new VertexAttribute(VertexFormat.Float32x2, 0, 0),
					new VertexAttribute(VertexFormat.Float32x2, 8, 1),
					new VertexAttribute(VertexFormat.Float32x2, 16, 2),
					new VertexAttribute(VertexFormat.Unorm16x4, 24, 3),
					new VertexAttribute(VertexFormat.Unorm8x4, 32, 4),
					new VertexAttribute(VertexFormat.Float32, 36, 5),
				]),
			]),
			Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleStrip },
			Fragment = new FragmentState(_fragment, [new ColorTargetState(format, state)]),
		});
		_pipelines[(blend, format)] = pipeline;
		return pipeline;
	}

	/// <summary>
	/// Uploads and draws everything <paramref name="batcher"/> recorded. Segments that target the frame are skipped when
	/// <paramref name="frame"/> is not rendering (a minimized window). Returns the number of draw calls issued.
	/// </summary>
	public int Submit(SpriteBatcher batcher, IGraphicsFrame frame)
	{
		var segments = batcher.Segments;
		if (segments.Length == 0) return 0;

		var frameReady = frame.IsRendering && frame.ColorTarget is not null;
		var any = false;
		foreach (ref readonly var segment in segments)
		{
			if (segment.Target is not null || frameReady)
			{
				any = true;
				break;
			}
		}

		if (!any) return 0;

		var resources = _frames[_device.FrameIndex % _frames.Length];
		var queue = _device.Queue;

		// One upload of every instance of the frame into this frame slot's buffer.
		var instances = batcher.Instances;
		if (instances.Length > 0)
		{
			var bytes = (ulong)instances.Length * SpriteInstance.SizeInBytes;
			if (resources.Instances is null || resources.Instances.Size < bytes)
			{
				resources.Instances?.Dispose();
				var capacity = Math.Max(bytes, 64ul * 1024);
				capacity = System.Numerics.BitOperations.RoundUpToPowerOf2(capacity);
				resources.Instances = _device.CreateBuffer(new BufferDescriptor(capacity, BufferUsage.Vertex | BufferUsage.CopyDst, "Sprite instances"));
			}

			queue.WriteBuffer(resources.Instances, 0, MemoryMarshal.AsBytes(instances));
		}

		// One uniform slot per segment (camera and blend parameters).
		_ensureUniforms(resources, segments.Length);
		var scratch = _uniformScratch.AsSpan(0, (int)(_uniformStride * (uint)segments.Length));
		for (var s = 0; s < segments.Length; s++)
		{
			ref readonly var segment = ref segments[s];
			var target = segment.Target ?? frame.ColorTarget;
			var size = target is null ? new Vector2(frame.Width, frame.Height) : new Vector2(target.Width, target.Height);
			var projection = Matrix4x4.CreateOrthographicOffCenter(0, Math.Max(1, size.X), Math.Max(1, size.Y), 0, 0, 1);
			var transform = segment.Options.Transform is { } view ? new Matrix4x4(view) * projection : projection;
			var slot = scratch.Slice((int)(_uniformStride * (uint)s), UniformSize);
			MemoryMarshal.Write(slot, in transform);
			var parameters = new Vector4(segment.Options.BlendMode == SpriteBlendMode.NonPremultiplied ? 0f : 1f, 0, 0, 0);
			MemoryMarshal.Write(slot[64..], in parameters);
		}

		queue.WriteBuffer(resources.Uniforms!, 0, scratch);

		var textures = batcher.Textures;
		var draws = batcher.Draws;
		var encoder = _device.CreateCommandEncoder("Ion sprites");
		IRenderPassEncoder? pass = null;
		ITexture? passTarget = null;
		IRenderPipeline? pipeline = null;
		var drawCalls = 0;

		for (var s = 0; s < segments.Length; s++)
		{
			ref readonly var segment = ref segments[s];
			var toFrame = segment.Target is null;
			if (toFrame && !frameReady) continue;
			var target = segment.Target ?? frame.ColorTarget!;

			if (pass is null || !ReferenceEquals(target, passTarget) || segment.Clear is not null)
			{
				pass?.End();
				RenderPassColorAttachment attachment;
				if (toFrame)
				{
					attachment = frame.ColorAttachment();
					if (segment.Clear is { } clear) attachment = attachment with { LoadOp = LoadOp.Clear, ClearValue = clear.ToVector4() };
				}
				else
				{
					attachment = segment.Clear is { } clear
						? new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, clear.ToVector4())
						: new RenderPassColorAttachment(target.DefaultView, LoadOp.Load, StoreOp.Store);
				}

				pass = encoder.BeginRenderPass(new RenderPassDescriptor([attachment], label: "Sprites"));
				passTarget = target;
				pipeline = null;
			}

			var next = GetPipeline(segment.Options.BlendMode, target.Format);
			if (!ReferenceEquals(next, pipeline))
			{
				pass.SetPipeline(next);
				pipeline = next;
			}

			pass.SetBindGroup(0, resources.UniformGroups[s]);
			if (segment.Options.Scissor is { } scissor)
			{
				var x0 = (uint)Math.Clamp(scissor.X, 0, (int)target.Width);
				var y0 = (uint)Math.Clamp(scissor.Y, 0, (int)target.Height);
				var x1 = (uint)Math.Clamp(scissor.Right, (int)x0, (int)target.Width);
				var y1 = (uint)Math.Clamp(scissor.Bottom, (int)y0, (int)target.Height);
				pass.SetScissorRect(x0, y0, x1 - x0, y1 - y0);
			}
			else
			{
				pass.SetScissorRect(0, 0, target.Width, target.Height);
			}

			var sampler = (int)segment.Options.SamplerMode;
			for (var d = segment.FirstDraw; d < segment.FirstDraw + segment.DrawCount; d++)
			{
				var draw = draws[d];
				var texture = textures[draw.Slot];
				if (texture.IsDisposed) continue;
				pass.SetBindGroup(1, _bindGroup(texture, sampler));
				pass.SetVertexBuffer(0, resources.Instances!, (ulong)draw.First * SpriteInstance.SizeInBytes);
				pass.Draw(4, (uint)draw.Count);
				drawCalls++;
			}
		}

		pass?.End();
		queue.Submit(encoder.Finish());
		return drawCalls;
	}

	private IBindGroup _bindGroup(SpriteTexture texture, int sampler)
	{
		if (!ReferenceEquals(texture.BindGroupOwner, this)) texture.ReleaseBindGroups();
		var group = texture.BindGroups[sampler];
		if (group is not null) return group;

		group = _device.CreateBindGroup(new BindGroupDescriptor(_textureLayout,
		[
			BindGroupEntry.ForTexture(0, texture.Texture.DefaultView),
			BindGroupEntry.ForSampler(1, _samplers[sampler]),
		], texture.Name));
		texture.BindGroups[sampler] = group;
		texture.BindGroupOwner = this;
		return group;
	}

	private void _ensureUniforms(FrameResources resources, int segments)
	{
		var needed = (int)(_uniformStride * (uint)segments);
		if (_uniformScratch.Length < needed) _uniformScratch = new byte[Math.Max(needed, (int)_uniformStride * 8)];
		if (resources.UniformGroups.Length >= segments && resources.Uniforms is not null) return;

		foreach (var group in resources.UniformGroups) group.Dispose();
		resources.Uniforms?.Dispose();

		var count = Math.Max(8, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)segments));
		resources.Uniforms = _device.CreateBuffer(new BufferDescriptor(_uniformStride * (ulong)count, BufferUsage.Uniform | BufferUsage.CopyDst, "Sprite cameras"));
		resources.UniformGroups = new IBindGroup[count];
		for (var i = 0; i < count; i++)
		{
			resources.UniformGroups[i] = _device.CreateBindGroup(new BindGroupDescriptor(_cameraLayout,
				[BindGroupEntry.ForBuffer(0, resources.Uniforms, _uniformStride * (ulong)i, UniformSize)]));
		}
	}

	/// <summary>Releases every GPU resource of the renderer (not the textures it drew).</summary>
	public void Dispose()
	{
		White.Dispose();
		foreach (var frame in _frames)
		{
			foreach (var group in frame.UniformGroups) group.Dispose();
			frame.UniformGroups = [];
			frame.Uniforms?.Dispose();
			frame.Instances?.Dispose();
			frame.Uniforms = frame.Instances = null;
		}

		foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
		_pipelines.Clear();
		foreach (var sampler in _samplers) sampler?.Dispose();
		_pipelineLayout.Dispose();
		_textureLayout.Dispose();
		_cameraLayout.Dispose();
		_fragment.Dispose();
		_vertex.Dispose();
	}
}
