using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>A mesh the renderer owns: GPU buffers (none without a device), sub-meshes and bounds.</summary>
internal sealed class MeshSlot
{
	public required string Name;
	public IBuffer? Vertices;
	public IBuffer? Indices;
	public required SubMesh[] SubMeshes;
	public Aabb Bounds;
	public BoundingSphere Sphere;
	public int VertexCount;
	public int IndexCount;
	public int Triangles;
	public VertexAttributes Attributes;

	/// <summary>The data of a mesh created before the device existed, uploaded at initialization.</summary>
	public MeshData? Pending;

	public void Release()
	{
		Vertices?.Dispose();
		Indices?.Dispose();
		Vertices = Indices = null;
	}
}

/// <summary>A registered texture (2D or cube).</summary>
internal sealed class TextureSlot
{
	public ITexture? Texture;
	public bool Owns;

	public void Release()
	{
		if (Owns) Texture?.Dispose();
		Texture = null;
	}
}

/// <summary>A material shader: the built-in unlit (id 1) and PBR (id 2), or a custom one.</summary>
internal sealed class ShaderSlot
{
	public required string Name;
	public IShaderModule? Vertex;
	public IShaderModule? Fragment;
	public int UniformSize;
	public int TextureCount;
	public SamplerDescriptor SamplerDescriptor;
	public ISampler? Sampler;
	public IBindGroupLayout? MaterialLayout;
	public IPipelineLayout? PipelineLayout;
	public bool OwnsModules;
	public MaterialShaderDescriptor? Descriptor;

	public void Release()
	{
		PipelineLayout?.Dispose();
		MaterialLayout?.Dispose();
		Sampler?.Dispose();
		if (OwnsModules)
		{
			Vertex?.Dispose();
			Fragment?.Dispose();
		}

		PipelineLayout = null;
		MaterialLayout = null;
		Sampler = null;
		Vertex = Fragment = null;
	}
}

/// <summary>A material: its shader, alpha mode, uniform data and textures, and (with a device) its GPU bind group.</summary>
internal sealed class MaterialSlot
{
	public int Shader;
	public AlphaMode AlphaMode;
	public bool DoubleSided;
	public MaterialUniforms Data;
	public byte[]? CustomData;
	public TextureHandle[] Textures = [];
	public IBuffer? Uniforms;
	public IBindGroup? BindGroup;
	public bool Dirty = true;

	// The last pipeline resolved for the main pass, cached per color format.
	public IRenderPipeline? CachedPipeline;
	public TextureFormat CachedFormat;
	public bool CachedAfterPrepass;

	/// <summary>The pipeline part of the sort key: shader, alpha mode and culling.</summary>
	public int PipelineSortId => ((Shader & 0x1F) << 3) | ((int)AlphaMode << 1) | (DoubleSided ? 1 : 0);

	public void Release()
	{
		BindGroup?.Dispose();
		Uniforms?.Dispose();
		BindGroup = null;
		Uniforms = null;
		CachedPipeline = null;
	}
}

/// <summary>A camera render target.</summary>
internal sealed class RenderTargetSlot
{
	public required ITexture? Color;
	public required TextureHandle Texture;
	public required string GraphName;
	public uint Width;
	public uint Height;
}

/// <summary>One submitted mesh renderer with its world matrix (the render world's object table).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RenderObject
{
	public Matrix4x4 World;
	public int Mesh;
	public int Material;
	public uint Layers;
	public bool CastShadows;
	public bool ReceiveShadows;
}

/// <summary>A submitted camera.</summary>
internal struct CameraEntry
{
	public Camera Camera;
	public Matrix4x4 World;
	public int Order;
}

/// <summary>A submitted directional light.</summary>
internal struct DirectionalEntry
{
	public DirectionalLight Light;
	public Vector3 Direction;
}

/// <summary>A submitted point or spot light, ready for the view uniforms.</summary>
internal struct LocalLightEntry
{
	public LightUniform Uniform;
	public BoundingSphere Bounds;
}

/// <summary>A run of instances drawn with one mesh and material.</summary>
internal struct Batch
{
	public int Mesh;
	public int Material;
	public int FirstInstance;
	public int InstanceCount;
}

/// <summary>A view of the frame: a camera resolved against its target, with its culled, sorted and batched draws.</summary>
internal sealed class ViewData
{
	public int CameraIndex;
	public Camera Camera;
	public Matrix4x4 View;
	public Matrix4x4 Projection;
	public Matrix4x4 ViewProjection;
	public Frustum Frustum;
	public Vector3 Position;
	public Vector3 Forward;
	public float Far;
	public int Target;
	public uint TargetWidth;
	public uint TargetHeight;
	public float ViewportX, ViewportY, ViewportWidth, ViewportHeight;
	public bool FirstOnTarget;
	public bool DrawSkybox;
	public bool DrawClearQuad;
	public int LightCount;
	public LightUniforms Lights;

	public GrowableArray<Batch> Opaque;
	public GrowableArray<Batch> Transparent;
	public int OpaqueObjects;
	public int TransparentObjects;
	public int Culled;
}

/// <summary>An array that grows by doubling and is reset without clearing: allocation-free once warm.</summary>
internal struct GrowableArray<T>
{
	public T[] Items;
	public int Count;

	public GrowableArray(int capacity)
	{
		Items = new T[capacity];
		Count = 0;
	}

	public readonly Span<T> Span => Items.AsSpan(0, Count);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T Add()
	{
		if (Items is null || Count == Items.Length) Grow(Count + 1);
		return ref Items[Count++];
	}

	public void EnsureCapacity(int capacity)
	{
		if (Items is null || Items.Length < capacity) Grow(capacity);
	}

	[MethodImpl(MethodImplOptions.NoInlining), MemberNotNull(nameof(Items))]
	private void Grow(int minimum) => Array.Resize(ref Items, Math.Max(minimum, Math.Max(16, (Items?.Length ?? 0) * 2)));

	public void Clear() => Count = 0;
}
