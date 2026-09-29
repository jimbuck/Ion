using System.Numerics;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Rendering2D;

namespace Ion.Extensions.Rendering3D;

/// <summary>
/// The 3D renderer (<see cref="IRenderer3D"/>) on the RHI. Resources (meshes, materials, textures, render targets) are
/// created at load time and addressed by handles; every frame the game (or the ECS extraction systems) submits cameras,
/// lights and mesh renderers during the Render stage, and when the stage ends the renderer runs its pipeline:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Extract</b>: the submissions, copied into flat arrays as they are made (<see cref="Submit"/>).</item>
/// <item><b>Prepare</b>: world bounds per object, per-view matrices and frusta, the light lists, the directional shadow
/// fit; per-view uniforms and per-object instance data written into per-frame rings.</item>
/// <item><b>Queue and sort</b>: per camera, frustum and layer culling, then opaque and masked objects binned by
/// (pipeline, material, mesh) and front to back, blended objects back to front, and runs of one mesh and material merged
/// into instanced batches; the shadow casters culled against the light's frustum and batched by mesh.</item>
/// <item><b>Render graph</b>: shadow map, optional depth prepass, opaque, skybox and transparent passes per camera,
/// custom passes, then the 2D overlay (the sprite batch), executed in dependency order with transient textures pooled.</item>
/// </list>
/// <para>Without a device (headless without rendering) steps 1 to 3 run and nothing is drawn.</para>
/// </remarks>
public sealed partial class Renderer3D : IRenderer3D, IDisposable
{
	/// <summary>The id of the built-in unlit shader.</summary>
	internal const int UnlitShader = 1;

	/// <summary>The id of the built-in PBR shader.</summary>
	internal const int PbrShader = 2;

	private readonly IGraphicsFrame? _frame;
	private readonly ILogger<Renderer3D>? _logger;
	private readonly List<MeshSlot?> _meshes = [null];
	private readonly List<MaterialSlot?> _materials = [null];
	private readonly List<TextureSlot?> _textures = [null];
	private readonly List<ShaderSlot?> _shaders = [null];
	private readonly List<RenderTargetSlot?> _renderTargets = [null];
	private readonly List<RenderGraphPass> _customPasses = [];
	private SceneEnvironment _environment = new();
	private bool _initialized;
	private bool _disposed;

	/// <summary>
	/// Creates a renderer drawing into <paramref name="frame"/>, or a CPU-only renderer (culling, sorting and batching
	/// without drawing) when <paramref name="frame"/> is null. GPU resources are created by <see cref="Initialize"/>.
	/// </summary>
	public Renderer3D(IGraphicsFrame? frame, Rendering3DOptions? options = null, SpriteBatch? overlay = null, ILogger<Renderer3D>? logger = null)
	{
		_frame = frame;
		Options = options ?? new Rendering3DOptions();
		Overlay = overlay;
		_logger = logger;
		_shaders.Add(new ShaderSlot { Name = "unlit", UniformSize = MaterialUniforms.Size, TextureCount = 5, SamplerDescriptor = DefaultMaterialSampler });
		_shaders.Add(new ShaderSlot { Name = "pbr", UniformSize = MaterialUniforms.Size, TextureCount = 5, SamplerDescriptor = DefaultMaterialSampler });
		_views = new ViewData[Math.Max(1, Options.MaxCameras)];
		for (var i = 0; i < _views.Length; i++) _views[i] = new ViewData();
		_graph = new RenderGraph((IGraphicsDevice?)null);
		_passes = new PassSet(this, _views.Length);
		// The material of mesh renderers without one: a white dielectric.
		_defaultMaterial = CreateMaterial(new PbrMaterial()).Id;
	}

	/// <summary>The material drawn for mesh renderers without a material (or with a destroyed one).</summary>
	public MaterialHandle DefaultMaterial => new(_defaultMaterial);

	/// <summary>The options.</summary>
	public Rendering3DOptions Options { get; }

	/// <summary>The sprite batch drawn as the 2D overlay pass (its submission is deferred to the render graph), or null.</summary>
	public SpriteBatch? Overlay { get; }

	/// <summary>True when the renderer draws (it has a device); false for the CPU-only renderer.</summary>
	public bool HasDevice => Device is not null;

	/// <summary>The device, once initialized with a frame.</summary>
	public IGraphicsDevice? Device { get; private set; }

	/// <summary>The render graph of the last frame (its execution order and lifetimes are inspectable).</summary>
	public RenderGraph Graph => _graph;

	/// <inheritdoc/>
	public Rendering3DStatistics LastFrameStatistics { get; private set; } = new(-1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

	/// <summary>The environment set with <see cref="SetEnvironment"/>.</summary>
	public SceneEnvironment Environment => _environment;

	private static SamplerDescriptor DefaultMaterialSampler => new()
	{
		MagFilter = FilterMode.Linear,
		MinFilter = FilterMode.Linear,
		MipmapFilter = FilterMode.Linear,
		AddressModeU = AddressMode.Repeat,
		AddressModeV = AddressMode.Repeat,
		AddressModeW = AddressMode.Repeat,
	};

	/// <summary>
	/// Adds a custom pass to every frame's render graph (a post effect, a debug view). It is set up after the built-in
	/// passes and before the 2D overlay, and ordered by its <see cref="RenderGraphPass.Order"/> and declared textures.
	/// </summary>
	public void AddPass(RenderGraphPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		_customPasses.Add(pass);
	}

	/// <summary>Removes a custom pass.</summary>
	public bool RemovePass(RenderGraphPass pass) => _customPasses.Remove(pass);

	// Meshes.

	/// <inheritdoc/>
	public MeshHandle CreateMesh(MeshData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		data.Validate();
		if (data.Normals is null) data.ComputeNormals();
		if (data.Tangents is null) data.ComputeTangents();

		var slot = new MeshSlot
		{
			Name = data.Name,
			SubMeshes = [.. data.SubMeshes],
			Bounds = data.ComputeBounds(),
			Sphere = data.ComputeSphere(),
			VertexCount = data.VertexCount,
			IndexCount = data.Indices.Length,
			Triangles = data.TriangleCount,
			Attributes = data.Attributes,
		};

		if (Device is { } device) UploadMesh(device, slot, data);
		else if (_frame is not null) slot.Pending = data;
		_meshes.Add(slot);
		return new MeshHandle(_meshes.Count - 1);
	}

	/// <summary>Interleaves <paramref name="data"/> into <see cref="MeshVertex"/>es and uploads the vertex and index buffers.</summary>
	internal static void UploadMesh(IGraphicsDevice device, MeshSlot slot, MeshData data)
	{
		var vertices = Interleave(data);
		slot.Vertices = device.CreateBuffer(new BufferDescriptor((ulong)Math.Max(1, vertices.Length) * MeshVertex.Size, BufferUsage.Vertex | BufferUsage.CopyDst, data.Name + " vertices"));
		slot.Indices = device.CreateBuffer(new BufferDescriptor((ulong)Math.Max(1, data.Indices.Length) * 4, BufferUsage.Index | BufferUsage.CopyDst, data.Name + " indices"));
		device.Queue.WriteBuffer(slot.Vertices, 0, (ReadOnlySpan<MeshVertex>)vertices);
		device.Queue.WriteBuffer(slot.Indices, 0, (ReadOnlySpan<uint>)data.Indices);
	}

	/// <summary>The GPU vertices of <paramref name="data"/>, with defaults for missing attributes.</summary>
	internal static MeshVertex[] Interleave(MeshData data)
	{
		var vertices = new MeshVertex[data.VertexCount];
		for (var i = 0; i < vertices.Length; i++)
		{
			ref var v = ref vertices[i];
			v.Position = data.Positions[i];
			v.Normal = data.Normals is { } n ? n[i] : Vector3.UnitY;
			v.Tangent = data.Tangents is { } t ? t[i] : new Vector4(1, 0, 0, 1);
			v.Uv0 = data.Uv0 is { } uv0 ? uv0[i] : default;
			v.Uv1 = data.Uv1 is { } uv1 ? uv1[i] : default;
			v.Color = data.Colors is { } colors ? PackColor(colors[i]) : 0xFFFF_FFFFu;
		}

		return vertices;
	}

	private static uint PackColor(Color color)
	{
		static uint B(float c) => (uint)MathF.Round(Math.Clamp(c, 0f, 1f) * 255f);
		return B(color.R) | (B(color.G) << 8) | (B(color.B) << 16) | (B(color.A) << 24);
	}

	/// <inheritdoc/>
	public MeshInfo GetMeshInfo(MeshHandle mesh)
	{
		var slot = GetMesh(mesh);
		return new MeshInfo(slot.Bounds, slot.Sphere, slot.VertexCount, slot.IndexCount, slot.SubMeshes.Length, slot.Attributes);
	}

	/// <inheritdoc/>
	public void DestroyMesh(MeshHandle mesh)
	{
		if (!mesh.IsValid || mesh.Id >= _meshes.Count || _meshes[mesh.Id] is not { } slot) return;
		slot.Release();
		_meshes[mesh.Id] = null;
	}

	private MeshSlot GetMesh(MeshHandle mesh) =>
		mesh.IsValid && mesh.Id < _meshes.Count && _meshes[mesh.Id] is { } slot ? slot : throw new ArgumentException($"Mesh {mesh.Id} does not exist.", nameof(mesh));

	// Materials.

	/// <inheritdoc/>
	public MaterialHandle CreateMaterial(in UnlitMaterial material)
	{
		var slot = new MaterialSlot();
		Apply(slot, material);
		return AddMaterial(slot);
	}

	/// <inheritdoc/>
	public MaterialHandle CreateMaterial(in PbrMaterial material)
	{
		var slot = new MaterialSlot();
		Apply(slot, material);
		return AddMaterial(slot);
	}

	/// <inheritdoc/>
	public MaterialHandle CreateMaterial(MaterialShaderHandle shader, ReadOnlySpan<byte> uniforms, ReadOnlySpan<TextureHandle> textures, AlphaMode alphaMode = AlphaMode.Opaque, bool doubleSided = false)
	{
		var shaderSlot = shader.IsValid && shader.Id < _shaders.Count && shader.Id > PbrShader ? _shaders[shader.Id] : null;
		if (shaderSlot is null) throw new ArgumentException($"Material shader {shader.Id} does not exist.", nameof(shader));
		if (textures.Length > shaderSlot.TextureCount) throw new ArgumentException($"Shader '{shaderSlot.Name}' takes {shaderSlot.TextureCount} textures, got {textures.Length}.", nameof(textures));
		if (uniforms.Length > shaderSlot.UniformSize) throw new ArgumentException($"Shader '{shaderSlot.Name}' has a {shaderSlot.UniformSize}-byte uniform block, got {uniforms.Length} bytes.", nameof(uniforms));

		var slot = new MaterialSlot
		{
			Shader = shader.Id,
			AlphaMode = alphaMode,
			DoubleSided = doubleSided,
			CustomData = new byte[Math.Max(16, shaderSlot.UniformSize)],
			Textures = new TextureHandle[shaderSlot.TextureCount],
		};
		uniforms.CopyTo(slot.CustomData);
		textures.CopyTo(slot.Textures);
		return AddMaterial(slot);
	}

	/// <summary>Replaces a custom material's uniform bytes.</summary>
	public void UpdateMaterial(MaterialHandle handle, ReadOnlySpan<byte> uniforms)
	{
		var slot = GetMaterial(handle);
		if (slot.CustomData is null) throw new ArgumentException("Not a custom material.", nameof(handle));
		if (uniforms.Length > slot.CustomData.Length) throw new ArgumentException("Too many uniform bytes.", nameof(uniforms));
		uniforms.CopyTo(slot.CustomData);
		slot.Dirty = true;
	}

	/// <inheritdoc/>
	public void UpdateMaterial(MaterialHandle handle, in UnlitMaterial material) => Apply(GetMaterial(handle), material);

	/// <inheritdoc/>
	public void UpdateMaterial(MaterialHandle handle, in PbrMaterial material) => Apply(GetMaterial(handle), material);

	/// <inheritdoc/>
	public void DestroyMaterial(MaterialHandle material)
	{
		if (!material.IsValid || material.Id >= _materials.Count || _materials[material.Id] is not { } slot) return;
		slot.Release();
		_materials[material.Id] = null;
	}

	private MaterialHandle AddMaterial(MaterialSlot slot)
	{
		_materials.Add(slot);
		return new MaterialHandle(_materials.Count - 1);
	}

	private MaterialSlot GetMaterial(MaterialHandle material) =>
		material.IsValid && material.Id < _materials.Count && _materials[material.Id] is { } slot ? slot : throw new ArgumentException($"Material {material.Id} does not exist.", nameof(material));

	private static void Apply(MaterialSlot slot, in UnlitMaterial material)
	{
		var textures = slot.Textures.Length == 5 ? slot.Textures : new TextureHandle[5];
		Array.Clear(textures);
		textures[0] = material.Texture;
		SetBuiltIn(slot, UnlitShader, material.AlphaMode, material.DoubleSided, textures, new MaterialUniforms
		{
			BaseColor = ColorSpace.ToLinear(material.BaseColor),
			Params = new Vector4(0, 1, 0, material.AlphaCutoff),
			Flags = new Vector4((int)material.AlphaMode, material.DoubleSided ? 1 : 0, 0, 0),
		});
	}

	private static void Apply(MaterialSlot slot, in PbrMaterial material)
	{
		var textures = slot.Textures.Length == 5 ? slot.Textures : new TextureHandle[5];
		textures[0] = material.BaseColorTexture;
		textures[1] = material.MetallicRoughnessTexture;
		textures[2] = material.Normal;
		textures[3] = material.Occlusion;
		textures[4] = material.EmissiveTexture;
		var emissive = ColorSpace.ToLinear(material.Emissive) * material.EmissiveIntensity;
		SetBuiltIn(slot, PbrShader, material.AlphaMode, material.DoubleSided, textures, new MaterialUniforms
		{
			BaseColor = ColorSpace.ToLinear(material.BaseColor),
			Emissive = new Vector4(emissive.X, emissive.Y, emissive.Z, material.NormalScale),
			Params = new Vector4(Math.Clamp(material.Metallic, 0, 1), Math.Clamp(material.Roughness, 0, 1), Math.Clamp(material.OcclusionStrength, 0, 1), material.AlphaCutoff),
			Flags = new Vector4((int)material.AlphaMode, material.DoubleSided ? 1 : 0, 0, 0),
		});
	}

	private static void SetBuiltIn(MaterialSlot slot, int shader, AlphaMode alphaMode, bool doubleSided, TextureHandle[] textures, in MaterialUniforms data)
	{
		var texturesChanged = slot.Shader != shader || !slot.Textures.AsSpan().SequenceEqual(textures);
		slot.Shader = shader;
		slot.AlphaMode = alphaMode;
		slot.DoubleSided = doubleSided;
		slot.Textures = textures;
		slot.Data = data;
		slot.Dirty = true;
		slot.CachedPipeline = null;
		if (texturesChanged)
		{
			// The bind group names the textures: build a new one on next use.
			slot.BindGroup?.Dispose();
			slot.BindGroup = null;
		}
	}

	/// <inheritdoc/>
	public MaterialShaderHandle CreateMaterialShader(MaterialShaderDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		if (descriptor.TextureCount is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(descriptor), "A material shader takes 0 to 6 textures (bindings 1 to 6).");
		var slot = new ShaderSlot
		{
			Name = descriptor.Name,
			UniformSize = (Math.Max(0, descriptor.UniformSize) + 15) / 16 * 16,
			TextureCount = descriptor.TextureCount,
			SamplerDescriptor = descriptor.Sampler,
			OwnsModules = true,
		};

		slot.Descriptor = descriptor;
		if (Device is { } device)
		{
			CreateCustomShader(device, slot);
		}
		else
		{
			// Without a device yet, check that the shaders exist so a typo fails at load, not at the first GPU frame.
			EmbeddedShaders.Load(descriptor.Assembly, descriptor.FragmentShader, ShaderLanguage.SpirV);
			if (descriptor.VertexShader is { } vertex) EmbeddedShaders.Load(descriptor.Assembly, vertex, ShaderLanguage.SpirV);
		}

		_shaders.Add(slot);
		return new MaterialShaderHandle(_shaders.Count - 1);
	}

	// Textures and render targets.

	/// <inheritdoc/>
	public TextureHandle CreateTexture(ITexture2D texture)
	{
		ArgumentNullException.ThrowIfNull(texture);
		var rhi = texture is SpriteTexture sprite ? sprite.Texture : null;
		_textures.Add(new TextureSlot { Texture = rhi, Owns = false });
		return new TextureHandle(_textures.Count - 1);
	}

	/// <inheritdoc/>
	public TextureHandle CreateTexture(ITexture texture, bool ownsTexture = false)
	{
		ArgumentNullException.ThrowIfNull(texture);
		_textures.Add(new TextureSlot { Texture = texture, Owns = ownsTexture });
		return new TextureHandle(_textures.Count - 1);
	}

	/// <summary>Registers a handle without a GPU texture (CPU-only renderers, headless loaders).</summary>
	internal TextureHandle CreateTexturePlaceholder()
	{
		_textures.Add(new TextureSlot());
		return new TextureHandle(_textures.Count - 1);
	}

	/// <summary>The RHI texture behind a handle, or null (none, or no GPU texture).</summary>
	public ITexture? GetTexture(TextureHandle texture) =>
		texture.IsValid && texture.Id < _textures.Count ? _textures[texture.Id]?.Texture : null;

	/// <summary>Releases a texture handle (and the texture when the renderer owns it).</summary>
	public void DestroyTexture(TextureHandle texture)
	{
		if (!texture.IsValid || texture.Id >= _textures.Count || _textures[texture.Id] is not { } slot) return;
		slot.Release();
		_textures[texture.Id] = null;
		// Materials and views that bound it rebuild their bind groups.
		foreach (var material in _materials)
		{
			if (material is null || Array.IndexOf(material.Textures, texture) < 0) continue;
			material.BindGroup?.Dispose();
			material.BindGroup = null;
		}

		if (_boundEnvironment == texture.Id) InvalidateViewGroups();
	}

	/// <inheritdoc/>
	public RenderTargetHandle CreateRenderTarget(uint width, uint height, string? name = null)
	{
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);
		if (_frame is not null && Device is null) throw new InvalidOperationException("Create render targets once the device exists (in Init steps with the default order, or later).");
		var label = name ?? $"RenderTarget{_renderTargets.Count}";
		var color = Device?.CreateTexture(new TextureDescriptor(width, height, TextureFormat.Rgba8Unorm,
			TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc, Label: label));
		var texture = color is null ? CreateTexturePlaceholder() : CreateTexture(color, ownsTexture: true);
		_renderTargets.Add(new RenderTargetSlot { Color = color, Texture = texture, GraphName = label, Width = width, Height = height });
		return new RenderTargetHandle(_renderTargets.Count - 1);
	}

	/// <inheritdoc/>
	public TextureHandle GetRenderTargetTexture(RenderTargetHandle target) =>
		target.IsValid && target.Id < _renderTargets.Count && _renderTargets[target.Id] is { } slot ? slot.Texture : default;

	// Submission (IMeshBatch).

	/// <inheritdoc/>
	public void AddCamera(in Camera camera, in Matrix4x4 world)
	{
		ref var entry = ref _cameras.Add();
		entry.Camera = camera;
		entry.World = world;
		entry.Order = _cameras.Count;
	}

	/// <inheritdoc/>
	public void SetCamera(in Camera camera, in Transform transform)
	{
		_cameras.Clear();
		AddCamera(camera, transform.ToMatrix());
	}

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Submit(in MeshRenderer renderer, in Matrix4x4 world)
	{
		ref var o = ref _objects.Add();
		o.World = world;
		o.Mesh = renderer.Mesh.Id;
		o.Material = renderer.Material.Id;
		o.Layers = renderer.LayerMask == 0 ? 1u : renderer.LayerMask;
		o.CastShadows = renderer.CastShadows;
		o.ReceiveShadows = renderer.ReceiveShadows;
	}

	/// <inheritdoc/>
	public void Draw(MeshHandle mesh, MaterialHandle material, in Matrix4x4 world)
	{
		ref var o = ref _objects.Add();
		o.World = world;
		o.Mesh = mesh.Id;
		o.Material = material.Id;
		o.Layers = 1;
		o.CastShadows = true;
		o.ReceiveShadows = true;
	}

	/// <inheritdoc/>
	public void AddLight(in DirectionalLight light, in Matrix4x4 world) => AddLight(light, -new Vector3(world.M31, world.M32, world.M33));

	/// <inheritdoc/>
	public void AddLight(in DirectionalLight light, Vector3 direction)
	{
		ref var entry = ref _directional.Add();
		entry.Light = light;
		entry.Direction = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : -Vector3.UnitY;
	}

	/// <inheritdoc/>
	public void AddLight(in PointLight light, Vector3 position)
	{
		var color = ColorSpace.ToLinear(light.Color) * light.Intensity;
		var range = light.Range > 0 ? light.Range : 10f;
		ref var entry = ref _local.Add();
		entry.Uniform = new LightUniform
		{
			PositionRange = new Vector4(position, range),
			ColorType = new Vector4(color.X, color.Y, color.Z, LightUniform.TypePoint),
		};
		entry.Bounds = new BoundingSphere(position, range);
	}

	/// <inheritdoc/>
	public void AddLight(in SpotLight light, in Matrix4x4 world)
	{
		var color = ColorSpace.ToLinear(light.Color) * light.Intensity;
		var range = light.Range > 0 ? light.Range : 10f;
		var direction = -new Vector3(world.M31, world.M32, world.M33);
		direction = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
		var outer = MathF.Cos(Math.Clamp(light.OuterConeAngle, 0.001f, MathF.PI / 2));
		var inner = MathF.Cos(Math.Clamp(light.InnerConeAngle, 0f, MathF.Acos(outer)));
		var position = world.Translation;
		ref var entry = ref _local.Add();
		entry.Uniform = new LightUniform
		{
			PositionRange = new Vector4(position, range),
			ColorType = new Vector4(color.X, color.Y, color.Z, LightUniform.TypeSpot),
			Direction = new Vector4(direction, 0),
			Spot = new Vector4(outer, 1f / MathF.Max(inner - outer, 1e-4f), 0, 0),
		};
		entry.Bounds = new BoundingSphere(position, range);
	}

	/// <inheritdoc/>
	public void SetEnvironment(in SceneEnvironment environment) => _environment = environment;
}
