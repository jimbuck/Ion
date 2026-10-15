using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>How an HDR scene is mapped to the displayable range by the composite pass.</summary>
public enum ToneMapping
{
	/// <summary>No curve: the exposed color is clipped at 1.</summary>
	None,
	/// <summary>Reinhard: <c>c / (1 + c)</c>, gentle, never reaches white.</summary>
	Reinhard,
	/// <summary>A fit of the ACES filmic curve: contrast in the mid tones and a soft shoulder (the default).</summary>
	Aces,
}

/// <summary>The uniform block of every post pass (std140, 48 bytes): mirrors <c>Post</c> in <c>ion_post.glsl</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PostUniforms
{
	public const int SlotSize = 256;

	public Vector4 Texel;
	public Vector4 Params;
	public Vector4 Flags;
}

/// <summary>
/// The post-processing of the frame's cameras: their scene texture (HDR and multisampled as configured), its resolve, the
/// bloom chain and the composite into the frame. One per renderer; the passes are reused every frame.
/// </summary>
internal sealed class PostProcessing : IDisposable
{
	/// <summary>The most bloom levels.</summary>
	public const int MaxBloomLevels = 8;

	private readonly Renderer3D _renderer;
	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _prefilter;
	private readonly IShaderModule _down;
	private readonly IShaderModule _up;
	private readonly IShaderModule _composite;
	private readonly IBindGroupLayout _layout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly ISampler _sampler;
	private readonly ITexture _black;
	private readonly IBuffer _uniforms;
	private readonly List<IBindGroup>[] _groups;
	private readonly Dictionary<(int Kind, TextureFormat Format), IRenderPipeline> _pipelines = [];
	private readonly int _slotsPerFrame;
	private int _nextSlot;

	public PostProcessing(Renderer3D renderer, IGraphicsDevice device)
	{
		_renderer = renderer;
		_device = device;
		var assembly = typeof(Renderer3D).Assembly;
		IShaderModule Module(string file) => device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, file));
		_vertex = Module("post.vert");
		_prefilter = Module("bloom_prefilter.frag");
		_down = Module("bloom_down.frag");
		_up = Module("bloom_up.frag");
		_composite = Module("composite.frag");
		_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Fragment, BindingType.UniformBuffer),
			new(1, ShaderStage.Fragment, BindingType.Texture),
			new(2, ShaderStage.Fragment, BindingType.Sampler),
			new(3, ShaderStage.Fragment, BindingType.Texture),
		], "Ion 3D post"));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout], "Ion 3D post"));
		_sampler = device.CreateSampler(SamplerDescriptor.LinearClamp with { MipmapFilter = FilterMode.Nearest, Label = "Ion 3D post" });
		_black = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Ion 3D post black"));
		device.Queue.WriteTexture(_black, [0, 0, 0, 255]);
		_slotsPerFrame = 2 + 2 * MaxBloomLevels;
		_uniforms = device.CreateBuffer(new BufferDescriptor((ulong)(device.FramesInFlight * _slotsPerFrame * PostUniforms.SlotSize), BufferUsage.Uniform | BufferUsage.CopyDst, "Ion 3D post"));
		_groups = new List<IBindGroup>[device.FramesInFlight];
		for (var i = 0; i < _groups.Length; i++) _groups[i] = [];

		Resolve = new ResolvePass();
		Prefilter = new PostPass(this, "BloomPrefilter", PostKind.Prefilter);
		for (var i = 0; i < MaxBloomLevels; i++)
		{
			Down[i] = new PostPass(this, $"BloomDown.{i}", PostKind.Down);
			Up[i] = new PostPass(this, $"BloomUp.{i}", PostKind.Up);
		}

		Composite = new PostPass(this, "Composite", PostKind.Composite);
	}

	public ResolvePass Resolve { get; }

	public PostPass Prefilter { get; }

	public PostPass[] Down { get; } = new PostPass[MaxBloomLevels];

	public PostPass[] Up { get; } = new PostPass[MaxBloomLevels];

	public PostPass Composite { get; }

	/// <summary>Starts a frame: the bind groups of this frame slot's previous use are released and the uniform slots reused.</summary>
	public void BeginFrame()
	{
		var groups = _groups[_device.FrameIndex];
		foreach (var group in groups) group.Dispose();
		groups.Clear();
		_nextSlot = 0;
	}

	/// <summary>Writes a pass's uniforms into the next slot of this frame and returns its offset.</summary>
	public ulong WriteUniforms(in PostUniforms uniforms)
	{
		var offset = (ulong)((_device.FrameIndex * _slotsPerFrame + Math.Min(_nextSlot++, _slotsPerFrame - 1)) * PostUniforms.SlotSize);
		_device.Queue.WriteBuffer(_uniforms, offset, in uniforms);
		return offset;
	}

	/// <summary>A bind group for one pass this frame (released when the frame slot comes round again).</summary>
	public IBindGroup Group(ulong uniformOffset, ITexture source, ITexture? bloom)
	{
		var group = _device.CreateBindGroup(new BindGroupDescriptor(_layout,
		[
			BindGroupEntry.ForBuffer(0, _uniforms, uniformOffset, PostUniforms.SlotSize),
			BindGroupEntry.ForTexture(1, source.DefaultView),
			BindGroupEntry.ForSampler(2, _sampler),
			BindGroupEntry.ForTexture(3, (bloom ?? _black).DefaultView),
		], "Ion 3D post"));
		_groups[_device.FrameIndex].Add(group);
		return group;
	}

	/// <summary>The pipeline of a pass kind into <paramref name="format"/> (the upsample adds onto its target).</summary>
	public IRenderPipeline Pipeline(PostKind kind, TextureFormat format)
	{
		if (_pipelines.TryGetValue(((int)kind, format), out var pipeline)) return pipeline;
		var fragment = kind switch
		{
			PostKind.Prefilter => _prefilter,
			PostKind.Down => _down,
			PostKind.Up => _up,
			_ => _composite,
		};
		var additive = new BlendState(new BlendComponent(BlendFactor.One, BlendFactor.One), new BlendComponent(BlendFactor.One, BlendFactor.One));
		pipeline = _device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = $"Ion 3D post {kind}",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex, []),
			Fragment = new FragmentState(fragment, [new ColorTargetState(format, kind == PostKind.Up ? additive : null)]),
			Primitive = new PrimitiveState { CullMode = CullMode.None },
		});
		_pipelines.Add(((int)kind, format), pipeline);
		return pipeline;
	}

	public Renderer3D Renderer => _renderer;

	public void Dispose()
	{
		foreach (var groups in _groups)
		{
			foreach (var group in groups) group.Dispose();
			groups.Clear();
		}

		foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
		_pipelines.Clear();
		_uniforms.Dispose();
		_black.Dispose();
		_sampler.Dispose();
		_pipelineLayout.Dispose();
		_layout.Dispose();
		_vertex.Dispose();
		_prefilter.Dispose();
		_down.Dispose();
		_up.Dispose();
		_composite.Dispose();
	}
}

/// <summary>The kinds of post pass.</summary>
internal enum PostKind
{
	Prefilter,
	Down,
	Up,
	Composite,
}

/// <summary>Resolves the multisampled scene texture into the single-sampled one the post passes read.</summary>
internal sealed class ResolvePass() : RenderGraphPass("Resolve", Orders.Post - 50)
{
	private RenderGraphTexture _source;
	private RenderGraphTexture _destination;

	public void Configure(RenderGraphTexture source, RenderGraphTexture destination)
	{
		_source = source;
		_destination = destination;
	}

	public override void Setup(RenderGraphBuilder builder)
	{
		builder.Read(_source);
		builder.Write(_destination);
	}

	public override void Execute(in RenderGraphContext context) => context.Encoder.ResolveTexture(context.GetTexture(_source), context.GetTexture(_destination));
}

/// <summary>
/// One full-target post pass: reads its source (and, for the composite, the bloom) and writes, or for an upsample adds
/// onto, its target. The composite writes the frame through its first-use attachment.
/// </summary>
internal sealed class PostPass(PostProcessing post, string name, PostKind kind) : RenderGraphPass(name, RenderGraphPass.Orders.Post - 40 + (int)kind)
{
	private RenderGraphTexture _source;
	private RenderGraphTexture _bloom;
	private RenderGraphTexture _target;
	private PostUniforms _uniforms;

	public void Configure(RenderGraphTexture source, RenderGraphTexture target, in PostUniforms uniforms, RenderGraphTexture bloom = default)
	{
		_source = source;
		_target = target;
		_bloom = bloom;
		_uniforms = uniforms;
	}

	public override void Setup(RenderGraphBuilder builder)
	{
		builder.Read(_source);
		if (_bloom.IsValid) builder.Read(_bloom);
		if (kind == PostKind.Up) builder.ReadWrite(_target);
		else builder.Write(_target);
	}

	public override void Execute(in RenderGraphContext context)
	{
		var source = context.GetTexture(_source);
		var bloom = _bloom.IsValid ? context.GetTexture(_bloom) : null;
		var target = context.GetTexture(_target);
		var uniforms = _uniforms;
		uniforms.Texel = new Vector4(1f / source.Width, 1f / source.Height, 0, 0);
		var offset = post.WriteUniforms(uniforms);
		var group = post.Group(offset, source, bloom);

		RenderPassColorAttachment attachment;
		if (kind == PostKind.Composite) attachment = post.Renderer.Frame.ColorAttachment() with { LoadOp = LoadOp.Clear, ClearValue = new Vector4(0, 0, 0, 1) };
		else attachment = new RenderPassColorAttachment(target.DefaultView, kind == PostKind.Up ? LoadOp.Load : LoadOp.Clear, StoreOp.Store, new Vector4(0, 0, 0, 1));

		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor([attachment], label: $"Ion 3D {Name}"));
		pass.SetViewport(0, 0, target.Width, target.Height);
		pass.SetScissorRect(0, 0, target.Width, target.Height);
		pass.SetPipeline(post.Pipeline(kind, target.Format));
		pass.SetBindGroup(0, group);
		pass.Draw(3);
		pass.End();
	}
}
