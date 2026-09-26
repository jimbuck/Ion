using System.Numerics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>The GPU half: resources, pipelines, per-frame uploads and the render graph.</summary>
public sealed partial class Renderer3D
{
	/// <summary>The format of every depth buffer and of the shadow map.</summary>
	internal const TextureFormat DepthFormat = TextureFormat.Depth32Float;

	private readonly Dictionary<PipelineKey, IRenderPipeline> _pipelines = [];
	private IShaderModule? _meshVertex;
	private IShaderModule? _depthVertex;
	private IShaderModule? _skyboxVertex;
	private IShaderModule? _skyboxFragment;
	private IShaderModule? _unlitFragment;
	private IShaderModule? _pbrFragment;
	private IBindGroupLayout? _viewLayout;
	private IBindGroupLayout? _shadowViewLayout;
	private IPipelineLayout? _viewOnlyPipelineLayout;
	private IPipelineLayout? _shadowPipelineLayout;
	private ISampler? _shadowSampler;
	private ISampler? _environmentSampler;
	private ITexture? _white;
	private ITexture? _flatNormal;
	private ITexture? _blackCube;
	private ITexture? _shadowMap;
	private IBuffer? _viewBuffer;
	private IBindGroup?[] _viewGroups = [];
	private IBindGroup?[] _shadowGroups = [];
	private IBuffer?[] _instanceBuffers = [];
	private int _boundEnvironment = -1;
	private ViewUniforms _uniforms;

	/// <summary>Seconds since start, written into the view uniforms (<c>uCameraPosition.w</c>) for animated materials.</summary>
	public float Time { get; set; }

	private int ViewSlots => _views.Length + 1;

	/// <summary>
	/// Creates the GPU resources on the frame's device: shaders, layouts, samplers, default textures, the shadow map, the
	/// uniform ring, and the GPU buffers of meshes created earlier. Called by the 3D renderer system at Init, after the
	/// device exists. Without a frame this does nothing (the renderer stays CPU-only).
	/// </summary>
	public void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		if (_frame is null) return;

		var device = _frame.Device;
		Device = device;
		_graph.Dispose();
		_graph = new RenderGraph(device);
		var assembly = typeof(Renderer3D).Assembly;
		IShaderModule Module(string file) => device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, file));
		_meshVertex = Module("mesh.vert");
		_depthVertex = Module("depth.vert");
		_skyboxVertex = Module("skybox.vert");
		_skyboxFragment = Module("skybox.frag");
		_unlitFragment = Module("unlit.frag");
		_pbrFragment = Module("pbr.frag");

		_viewLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.VertexFragment, BindingType.UniformBuffer),
			new(1, ShaderStage.Fragment, BindingType.Texture),
			new(2, ShaderStage.Fragment, BindingType.Sampler),
			new(3, ShaderStage.Fragment, BindingType.Texture),
			new(4, ShaderStage.Fragment, BindingType.Sampler),
		], "Ion 3D view"));
		_shadowViewLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Vertex, BindingType.UniformBuffer)], "Ion 3D shadow view"));
		_viewOnlyPipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_viewLayout], "Ion 3D view only"));
		_shadowPipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_shadowViewLayout], "Ion 3D shadow"));

		var unlit = _shaders[UnlitShader]!;
		unlit.Vertex = _meshVertex;
		unlit.Fragment = _unlitFragment;
		CreateShaderLayouts(device, unlit);
		var pbr = _shaders[PbrShader]!;
		pbr.Vertex = _meshVertex;
		pbr.Fragment = _pbrFragment;
		CreateShaderLayouts(device, pbr);
		for (var i = PbrShader + 1; i < _shaders.Count; i++)
		{
			if (_shaders[i] is { Fragment: null } custom) CreateCustomShader(device, custom);
		}

		_shadowSampler = device.CreateSampler(new SamplerDescriptor
		{
			MagFilter = FilterMode.Linear,
			MinFilter = FilterMode.Linear,
			MipmapFilter = FilterMode.Nearest,
			Compare = CompareFunction.LessEqual,
			Label = "Ion 3D shadow",
		});
		_environmentSampler = device.CreateSampler(new SamplerDescriptor
		{
			MagFilter = FilterMode.Linear,
			MinFilter = FilterMode.Linear,
			MipmapFilter = FilterMode.Linear,
			Label = "Ion 3D environment",
		});

		_white = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Ion 3D white"));
		device.Queue.WriteTexture(_white, [255, 255, 255, 255]);
		_flatNormal = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Ion 3D flat normal"));
		device.Queue.WriteTexture(_flatNormal, [128, 128, 255, 255]);
		_blackCube = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Ion 3D black cube", Dimension: TextureDimension.Cube));
		for (uint face = 0; face < 6; face++) device.Queue.WriteTexture(_blackCube, [0, 0, 0, 255], 4, new TextureRegion(0, 0, 1, 1, 0, face));

		// The shadow map starts cleared (so it can be sampled before any shadow pass).
		var size = (uint)Math.Clamp(Options.ShadowMapSize, 16, 8192);
		_shadowMap = device.CreateTexture(new TextureDescriptor(size, size, DepthFormat, TextureUsage.RenderAttachment | TextureUsage.TextureBinding, Label: "Ion 3D shadow map"));
		var clear = device.CreateCommandEncoder("Ion 3D shadow map clear");
		clear.BeginRenderPass(new RenderPassDescriptor([], new RenderPassDepthStencilAttachment(_shadowMap.DefaultView, LoadOp.Clear, StoreOp.Store, 1f))).End();
		device.Queue.Submit(clear.Finish());

		_viewBuffer = device.CreateBuffer(new BufferDescriptor((ulong)(device.FramesInFlight * ViewSlots * ViewUniforms.SlotSize), BufferUsage.Uniform | BufferUsage.CopyDst, "Ion 3D views"));
		_viewGroups = new IBindGroup?[device.FramesInFlight * _views.Length];
		_shadowGroups = new IBindGroup?[device.FramesInFlight];
		_instanceBuffers = new IBuffer?[device.FramesInFlight];

		foreach (var mesh in _meshes)
		{
			if (mesh?.Pending is not { } pending) continue;
			UploadMesh(device, mesh, pending);
			mesh.Pending = null;
		}

		if (Overlay is { } overlay) overlay.DeferSubmission = true;
		_logger?.LogInformation("3D renderer ready on {Adapter} ({Backend}): shadow map {Size}x{Size}, depth prepass {Prepass}, up to {Cameras} cameras.",
			device.AdapterName, device.Backend, size, size, Options.DepthPrepass ? "on" : "off", _views.Length);
	}

	private void CreateShaderLayouts(IGraphicsDevice device, ShaderSlot slot)
	{
		var entries = new List<BindGroupLayoutEntry>();
		if (slot.UniformSize > 0) entries.Add(new(0, ShaderStage.VertexFragment, BindingType.UniformBuffer));
		for (var t = 1; t <= slot.TextureCount; t++) entries.Add(new((uint)t, ShaderStage.Fragment, BindingType.Texture));
		entries.Add(new(7, ShaderStage.Fragment, BindingType.Sampler));
		slot.MaterialLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([.. entries], $"Ion 3D material {slot.Name}"));
		slot.PipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_viewLayout!, slot.MaterialLayout], $"Ion 3D {slot.Name}"));
		slot.Sampler = device.CreateSampler(slot.SamplerDescriptor with { Label = $"Ion 3D {slot.Name}" });
	}

	private void CreateCustomShader(IGraphicsDevice device, ShaderSlot slot)
	{
		var descriptor = slot.Descriptor!;
		slot.Fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, descriptor.Assembly, descriptor.FragmentShader));
		slot.Vertex = descriptor.VertexShader is { } vertex ? device.CreateShaderModule(EmbeddedShaders.Descriptor(device, descriptor.Assembly, vertex)) : null;
		if (_viewLayout is not null) CreateShaderLayouts(device, slot);
	}

	// Per-frame GPU work.

	/// <summary>Uploads the frame's data, builds and executes the render graph. Returns whether the 2D overlay was submitted by it.</summary>
	private bool RenderGpu()
	{
		var device = Device!;
		var frame = _frame!;
		var slot = device.FrameIndex;

		// Instances: one upload into this frame slot's ring buffer.
		var instances = _instances.Span;
		var instanceBuffer = _instanceBuffers[slot];
		var needed = (ulong)Math.Max(1, instances.Length) * InstanceData.Size;
		if (instanceBuffer is null || instanceBuffer.Size < needed)
		{
			instanceBuffer?.Dispose();
			var capacity = Math.Max(needed, (ulong)InstanceData.Size * 1024);
			capacity = Math.Max(capacity, (instanceBuffer?.Size ?? 0) * 2);
			instanceBuffer = device.CreateBuffer(new BufferDescriptor(capacity, BufferUsage.Vertex | BufferUsage.CopyDst, "Ion 3D instances"));
			_instanceBuffers[slot] = instanceBuffer;
		}

		if (instances.Length > 0) device.Queue.WriteBuffer(instanceBuffer, 0, (ReadOnlySpan<InstanceData>)instances);
		_passes.Instances = instanceBuffer;

		// The environment cube map is part of the view bind groups.
		var environment = _environment.Skybox.IsValid && GetTexture(_environment.Skybox) is { Dimension: TextureDimension.Cube } ? _environment.Skybox.Id : 0;
		if (environment != _boundEnvironment)
		{
			InvalidateViewGroups();
			_boundEnvironment = environment;
		}

		// View uniforms.
		for (var v = 0; v < _viewCount; v++)
		{
			WriteViewUniforms(_views[v], frame);
			device.Queue.WriteBuffer(_viewBuffer!, ViewOffset(slot, v), in _uniforms);
		}

		if (_shadowActive)
		{
			_uniforms = default;
			_uniforms.ViewProjection = _shadow.ViewProjection;
			device.Queue.WriteBuffer(_viewBuffer!, ViewOffset(slot, _views.Length), in _uniforms);
		}

		// Materials drawn this frame: uniforms and bind groups.
		for (var v = 0; v < _viewCount; v++)
		{
			foreach (ref readonly var batch in _views[v].Opaque.Span) EnsureMaterial(device, _materials[batch.Material]!);
			foreach (ref readonly var batch in _views[v].Transparent.Span) EnsureMaterial(device, _materials[batch.Material]!);
		}

		// The render graph.
		_graph.Reset();
		var backbuffer = _graph.Import(RenderGraphResources.Backbuffer, frame.ColorTarget!, output: true);
		var shadowMap = _graph.Import(RenderGraphResources.ShadowMap, _shadowMap!);
		if (_shadowActive)
		{
			_passes.Shadow.Target = shadowMap;
			_graph.AddPass(_passes.Shadow);
		}

		for (var v = 0; v < _viewCount; v++)
		{
			var view = _views[v];
			var color = view.Target == 0 ? backbuffer : _graph.Import(_renderTargets[view.Target]!.GraphName, _renderTargets[view.Target]!.Color!, output: true);
			var depth = _graph.CreateTexture(v == _frameViewIndex ? RenderGraphResources.Depth : _passes.DepthNames[v], new RenderGraphTextureDescriptor(view.TargetWidth, view.TargetHeight, DepthFormat));
			var prepass = Options.DepthPrepass && view.Opaque.Count > 0;
			if (prepass)
			{
				_passes.Prepass[v].Configure(depth, color);
				_graph.AddPass(_passes.Prepass[v]);
			}

			_passes.Opaque[v].Configure(color, depth, shadowMap, prepass);
			_graph.AddPass(_passes.Opaque[v]);
			if (view.DrawSkybox)
			{
				_passes.Skybox[v].Configure(color, depth);
				_graph.AddPass(_passes.Skybox[v]);
			}

			if (view.Transparent.Count > 0)
			{
				_passes.Transparent[v].Configure(color, depth, shadowMap);
				_graph.AddPass(_passes.Transparent[v]);
			}
		}

		foreach (var pass in _customPasses) _graph.AddPass(pass);
		var overlay = Overlay is not null;
		if (overlay)
		{
			_passes.Overlay.Target = backbuffer;
			_graph.AddPass(_passes.Overlay);
		}

		_graph.Compile();
		_graph.Execute(device);
		return overlay;
	}

	private static ulong ViewOffset(int frameSlot, int view, int viewSlots) => (ulong)((frameSlot * viewSlots + view) * ViewUniforms.SlotSize);

	private ulong ViewOffset(int frameSlot, int view) => ViewOffset(frameSlot, view, ViewSlots);

	private void WriteViewUniforms(ViewData view, IGraphicsFrame frame)
	{
		ref var u = ref _uniforms;
		u.ViewProjection = view.ViewProjection;
		u.View = view.View;
		u.Projection = view.Projection;
		Matrix4x4.Invert(view.ViewProjection, out u.InverseViewProjection);
		u.ShadowMatrix = _shadow.ShadowMatrix;
		u.CameraPosition = new Vector4(view.Position, Time);

		var environmentTexture = _boundEnvironment > 0 ? GetTexture(new TextureHandle(_boundEnvironment)) : null;
		var ambient = ColorSpace.ToLinear(_environment.AmbientColor) * _environment.AmbientIntensity;
		u.Ambient = new Vector4(ambient.X, ambient.Y, ambient.Z, environmentTexture is null ? 0 : MathF.Max(0, _environment.SkyboxIntensity));

		if (_shadowActive)
		{
			var light = _directional.Items[_mainLight].Light;
			var texels = light.ShadowBias > 0 ? light.ShadowBias : 1.5f;
			var depthBias = texels * _shadow.TexelSize / MathF.Max(_shadow.DepthRange, 1e-3f);
			u.ShadowParams = new Vector4(1, depthBias, _shadow.TexelSize, 1f / _shadowMap!.Width);
		}
		else
		{
			u.ShadowParams = default;
		}

		if (_mainLight >= 0)
		{
			ref readonly var main = ref _directional.Items[_mainLight];
			var color = ColorSpace.ToLinear(main.Light.Color) * main.Light.Intensity;
			u.LightDirection = new Vector4(main.Direction, 1);
			u.LightColor = new Vector4(color.X, color.Y, color.Z, 1);
		}
		else
		{
			u.LightDirection = default;
			u.LightColor = default;
		}

		var format = view.Target == 0 ? frame.ColorFormat : TextureFormat.Rgba8Unorm;
		u.ClearColor = ClearValue(view.Camera.ClearColor, format);
		u.Counts = new Vector4(view.LightCount, environmentTexture is null ? 0 : environmentTexture.MipLevelCount - 1, format.IsSrgb() ? 0 : 1,
			view.DrawSkybox ? 0 : 1);
		u.Lights = view.Lights;
	}

	/// <summary>The value a clear writes for <paramref name="color"/> (sRGB) into a target of <paramref name="format"/>.</summary>
	internal static Vector4 ClearValue(Color color, TextureFormat format) =>
		format.IsSrgb() ? ColorSpace.ToLinear(color) : color.ToVector4();

	private void EnsureMaterial(IGraphicsDevice device, MaterialSlot material)
	{
		var shader = _shaders[material.Shader]!;
		if (material.Uniforms is null && shader.UniformSize > 0)
		{
			material.Uniforms = device.CreateBuffer(new BufferDescriptor((ulong)Math.Max(MaterialUniforms.Size, shader.UniformSize), BufferUsage.Uniform | BufferUsage.CopyDst, $"Ion 3D material {shader.Name}"));
			material.Dirty = true;
		}

		if (material.Dirty && material.Uniforms is not null)
		{
			if (material.CustomData is { } custom) device.Queue.WriteBuffer(material.Uniforms, 0, custom);
			else device.Queue.WriteBuffer(material.Uniforms, 0, in material.Data);
			material.Dirty = false;
		}

		if (material.BindGroup is not null) return;
		var entries = new List<BindGroupEntry>(8);
		if (material.Uniforms is not null) entries.Add(BindGroupEntry.ForBuffer(0, material.Uniforms));
		for (var t = 0; t < shader.TextureCount; t++)
		{
			var texture = t < material.Textures.Length ? GetTexture(material.Textures[t]) : null;
			if (texture is not null && texture.Dimension != TextureDimension.D2) texture = null;
			// The flat normal for a missing normal map (built-in materials' binding 3), white for everything else.
			var fallback = material.Shader <= PbrShader && t == 2 ? _flatNormal! : _white!;
			entries.Add(BindGroupEntry.ForTexture((uint)(t + 1), (texture ?? fallback).DefaultView));
		}

		entries.Add(BindGroupEntry.ForSampler(7, shader.Sampler!));
		material.BindGroup = device.CreateBindGroup(new BindGroupDescriptor(shader.MaterialLayout!, [.. entries], $"Ion 3D material {shader.Name}"));
	}

	internal IBindGroup ViewGroup(int view)
	{
		var device = Device!;
		var index = device.FrameIndex * _views.Length + view;
		if (_viewGroups[index] is { } group) return group;
		var environment = _boundEnvironment > 0 ? GetTexture(new TextureHandle(_boundEnvironment)) : null;
		group = device.CreateBindGroup(new BindGroupDescriptor(_viewLayout!,
		[
			BindGroupEntry.ForBuffer(0, _viewBuffer!, ViewOffset(device.FrameIndex, view), ViewUniforms.SlotSize),
			BindGroupEntry.ForTexture(1, _shadowMap!.DefaultView),
			BindGroupEntry.ForSampler(2, _shadowSampler!),
			BindGroupEntry.ForTexture(3, (environment ?? _blackCube!).DefaultView),
			BindGroupEntry.ForSampler(4, _environmentSampler!),
		], "Ion 3D view"));
		_viewGroups[index] = group;
		return group;
	}

	internal IBindGroup ShadowGroup()
	{
		var device = Device!;
		var index = device.FrameIndex;
		if (_shadowGroups[index] is { } group) return group;
		group = device.CreateBindGroup(new BindGroupDescriptor(_shadowViewLayout!,
			[BindGroupEntry.ForBuffer(0, _viewBuffer!, ViewOffset(device.FrameIndex, _views.Length), ViewUniforms.SlotSize)], "Ion 3D shadow view"));
		_shadowGroups[index] = group;
		return group;
	}

	private void InvalidateViewGroups()
	{
		for (var i = 0; i < _viewGroups.Length; i++)
		{
			_viewGroups[i]?.Dispose();
			_viewGroups[i] = null;
		}
	}

	// Pipelines.

	internal IRenderPipeline MainPipeline(MaterialSlot material, TextureFormat format, bool afterPrepass)
	{
		afterPrepass &= material.AlphaMode == AlphaMode.Opaque;
		if (material.CachedPipeline is { } cached && material.CachedFormat == format && material.CachedAfterPrepass == afterPrepass) return cached;
		var key = new PipelineKey(material.Shader, material.AlphaMode, material.DoubleSided, afterPrepass ? PipelineKind.MainAfterPrepass : PipelineKind.Main, format);
		if (!_pipelines.TryGetValue(key, out var pipeline))
		{
			var shader = _shaders[material.Shader]!;
			var blend = material.AlphaMode == AlphaMode.Blend;
			pipeline = Device!.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Label = $"Ion 3D {shader.Name} {material.AlphaMode}{(material.DoubleSided ? " double-sided" : "")}{(afterPrepass ? " after prepass" : "")}",
				Layout = shader.PipelineLayout!,
				Vertex = new VertexState(shader.Vertex ?? _meshVertex!, MeshBuffers),
				Fragment = new FragmentState(shader.Fragment!, [new ColorTargetState(format, blend ? BlendState.AlphaBlend : null)]),
				Primitive = new PrimitiveState { CullMode = material.DoubleSided ? CullMode.None : CullMode.Back },
				DepthStencil = new DepthStencilState(DepthFormat, DepthWriteEnabled: !blend && !afterPrepass, afterPrepass ? CompareFunction.LessEqual : CompareFunction.Less),
			});
			_pipelines.Add(key, pipeline);
		}

		material.CachedPipeline = pipeline;
		material.CachedFormat = format;
		material.CachedAfterPrepass = afterPrepass;
		return pipeline;
	}

	internal IRenderPipeline DepthPipeline(bool shadow)
	{
		var key = new PipelineKey(0, AlphaMode.Opaque, false, shadow ? PipelineKind.ShadowDepth : PipelineKind.PrepassDepth, TextureFormat.Undefined);
		if (_pipelines.TryGetValue(key, out var pipeline)) return pipeline;
		pipeline = Device!.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = shadow ? "Ion 3D shadow depth" : "Ion 3D depth prepass",
			Layout = shadow ? _shadowPipelineLayout! : _viewOnlyPipelineLayout!,
			Vertex = new VertexState(_depthVertex!, DepthBuffers),
			// Shadows: no culling (thin and open meshes cast too; the bias handles acne). Prepass: back faces culled.
			Primitive = new PrimitiveState { CullMode = shadow ? CullMode.None : CullMode.Back },
			DepthStencil = new DepthStencilState(DepthFormat, true, CompareFunction.Less),
		});
		_pipelines.Add(key, pipeline);
		return pipeline;
	}

	internal IRenderPipeline BackgroundPipeline(TextureFormat format, bool clearQuad)
	{
		var key = new PipelineKey(0, AlphaMode.Opaque, false, clearQuad ? PipelineKind.ClearQuad : PipelineKind.Skybox, format);
		if (_pipelines.TryGetValue(key, out var pipeline)) return pipeline;
		pipeline = Device!.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = clearQuad ? "Ion 3D clear" : "Ion 3D skybox",
			Layout = _viewOnlyPipelineLayout!,
			Vertex = new VertexState(_skyboxVertex!, []),
			Fragment = new FragmentState(_skyboxFragment!, [new ColorTargetState(format)]),
			DepthStencil = new DepthStencilState(DepthFormat, false, clearQuad ? CompareFunction.Always : CompareFunction.LessEqual),
		});
		_pipelines.Add(key, pipeline);
		return pipeline;
	}

	/// <summary>The vertex buffer layouts of every mesh draw: the mesh's vertices (slot 0) and the instance ring (slot 1).</summary>
	internal static readonly VertexBufferLayout[] MeshBuffers =
	[
		new VertexBufferLayout(MeshVertex.Size, VertexStepMode.Vertex,
		[
			new VertexAttribute(VertexFormat.Float32x3, 0, 0),
			new VertexAttribute(VertexFormat.Float32x3, 12, 1),
			new VertexAttribute(VertexFormat.Float32x4, 24, 2),
			new VertexAttribute(VertexFormat.Float32x2, 40, 3),
			new VertexAttribute(VertexFormat.Float32x2, 48, 4),
			new VertexAttribute(VertexFormat.Unorm8x4, 56, 5),
		]),
		new VertexBufferLayout(InstanceData.Size, VertexStepMode.Instance,
		[
			new VertexAttribute(VertexFormat.Float32x4, 0, 6),
			new VertexAttribute(VertexFormat.Float32x4, 16, 7),
			new VertexAttribute(VertexFormat.Float32x4, 32, 8),
			new VertexAttribute(VertexFormat.Float32x4, 48, 9),
			new VertexAttribute(VertexFormat.Float32x4, 64, 10),
			new VertexAttribute(VertexFormat.Float32x4, 80, 11),
		]),
	];

	/// <summary>The vertex buffer layouts of depth-only draws: the same buffers, only the position and the world matrix read.</summary>
	internal static readonly VertexBufferLayout[] DepthBuffers =
	[
		new VertexBufferLayout(MeshVertex.Size, VertexStepMode.Vertex, [new VertexAttribute(VertexFormat.Float32x3, 0, 0)]),
		new VertexBufferLayout(InstanceData.Size, VertexStepMode.Instance,
		[
			new VertexAttribute(VertexFormat.Float32x4, 0, 6),
			new VertexAttribute(VertexFormat.Float32x4, 16, 7),
			new VertexAttribute(VertexFormat.Float32x4, 32, 8),
		]),
	];

	// Drawing (called by the passes).

	internal MeshSlot? Mesh(int id) => (uint)id < (uint)_meshes.Count ? _meshes[id] : null;

	internal MaterialSlot Material(int id) => _materials[id]!;

	internal ViewData View(int index) => _views[index];

	internal ITexture ShadowMapTexture => _shadowMap!;

	internal IBuffer PassInstances => _passes.Instances!;

	/// <summary>The texture handle of the render target a view draws into (none for the frame).</summary>
	internal TextureHandle TargetTexture(ViewData view) => view.Target == 0 ? default : _renderTargets[view.Target]!.Texture;

	internal IGraphicsFrame Frame => _frame!;

	/// <summary>Releases the GPU resources (meshes, materials, textures the renderer owns, pipelines). Call before the device is destroyed.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (Overlay is { } overlay) overlay.DeferSubmission = false;
		foreach (var mesh in _meshes) mesh?.Release();
		foreach (var material in _materials) material?.Release();
		foreach (var texture in _textures) texture?.Release();
		foreach (var shader in _shaders) shader?.Release();
		foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
		_pipelines.Clear();
		InvalidateViewGroups();
		foreach (var group in _shadowGroups) group?.Dispose();
		foreach (var buffer in _instanceBuffers) buffer?.Dispose();
		_viewBuffer?.Dispose();
		_shadowMap?.Dispose();
		_white?.Dispose();
		_flatNormal?.Dispose();
		_blackCube?.Dispose();
		_shadowSampler?.Dispose();
		_environmentSampler?.Dispose();
		_viewOnlyPipelineLayout?.Dispose();
		_shadowPipelineLayout?.Dispose();
		_viewLayout?.Dispose();
		_shadowViewLayout?.Dispose();
		_meshVertex?.Dispose();
		_depthVertex?.Dispose();
		_skyboxVertex?.Dispose();
		_skyboxFragment?.Dispose();
		_unlitFragment?.Dispose();
		_pbrFragment?.Dispose();
		_graph.Dispose();
		Device = null;
	}

	private enum PipelineKind
	{
		Main,
		MainAfterPrepass,
		ShadowDepth,
		PrepassDepth,
		Skybox,
		ClearQuad,
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct PipelineKey(int Shader, AlphaMode Alpha, bool DoubleSided, PipelineKind Kind, TextureFormat Color);
}
