using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics;

/// <summary>How a material's alpha is used (glTF's <c>alphaMode</c>).</summary>
public enum AlphaMode
{
	/// <summary>Alpha is ignored; the surface is opaque (drawn in the opaque pass, front to back, instanced).</summary>
	Opaque,
	/// <summary>Fragments with alpha below the cutoff are discarded; the rest are opaque.</summary>
	Mask,
	/// <summary>Alpha blended (drawn in the transparent pass, back to front, after the skybox).</summary>
	Blend,
}

/// <summary>
/// An unlit material: a color times an optional texture, no lighting or shadows. Colors are sRGB like every Ion
/// <see cref="Color"/> (the renderer linearizes them); the texture is sampled as sRGB color.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct UnlitMaterial
{
	/// <summary>The color (multiplied with the texture and the vertex color).</summary>
	public Color BaseColor;

	/// <summary>The texture, or none.</summary>
	public TextureHandle Texture;

	/// <summary>How alpha is used.</summary>
	public AlphaMode AlphaMode;

	/// <summary>The alpha below which <see cref="AlphaMode.Mask"/> discards (0.5 by default).</summary>
	public float AlphaCutoff;

	/// <summary>Whether back faces are drawn too.</summary>
	public bool DoubleSided;

	/// <summary>A white, opaque, single-sided unlit material.</summary>
	public UnlitMaterial()
	{
		BaseColor = Color.White;
		AlphaCutoff = 0.5f;
	}

	/// <summary>An unlit material of <paramref name="color"/> with an optional texture.</summary>
	public UnlitMaterial(Color color, TextureHandle texture = default) : this()
	{
		BaseColor = color;
		Texture = texture;
	}
}

/// <summary>
/// A metallic-roughness physically based material (glTF 2.0's core material): Cook-Torrance GGX specular, Lambert
/// diffuse, normal, occlusion and emissive maps. Colors are sRGB like every Ion <see cref="Color"/>; the renderer
/// linearizes them (the glTF loader converts glTF's linear factors).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PbrMaterial
{
	/// <summary>The base color (albedo for dielectrics, specular color for metals); alpha is the opacity.</summary>
	public Color BaseColor;

	/// <summary>The base color texture (sRGB), multiplied with <see cref="BaseColor"/>.</summary>
	public TextureHandle BaseColorTexture;

	/// <summary>Metalness in [0, 1].</summary>
	public float Metallic;

	/// <summary>Perceptual roughness in [0, 1].</summary>
	public float Roughness;

	/// <summary>The metallic-roughness texture (glTF: roughness in green, metalness in blue), multiplied with the factors.</summary>
	public TextureHandle MetallicRoughnessTexture;

	/// <summary>The tangent-space normal map, or none.</summary>
	public TextureHandle Normal;

	/// <summary>The strength of the normal map's x and y.</summary>
	public float NormalScale;

	/// <summary>The emitted color (sRGB), multiplied with <see cref="EmissiveIntensity"/> and the emissive texture.</summary>
	public Color Emissive;

	/// <summary>A multiplier of <see cref="Emissive"/> (above 1 for glowing surfaces).</summary>
	public float EmissiveIntensity;

	/// <summary>The emissive texture (sRGB), or none.</summary>
	public TextureHandle EmissiveTexture;

	/// <summary>The ambient occlusion texture (red channel), or none.</summary>
	public TextureHandle Occlusion;

	/// <summary>How much <see cref="Occlusion"/> darkens ambient light, in [0, 1].</summary>
	public float OcclusionStrength;

	/// <summary>How alpha is used.</summary>
	public AlphaMode AlphaMode;

	/// <summary>The alpha below which <see cref="AlphaMode.Mask"/> discards (0.5 by default).</summary>
	public float AlphaCutoff;

	/// <summary>Whether back faces are drawn too (and lit with a flipped normal).</summary>
	public bool DoubleSided;

	/// <summary>A white dielectric (metallic 0, roughness 0.5), opaque, single-sided, no emission.</summary>
	public PbrMaterial()
	{
		BaseColor = Color.White;
		Roughness = 0.5f;
		NormalScale = 1f;
		Emissive = Color.Black;
		EmissiveIntensity = 1f;
		OcclusionStrength = 1f;
		AlphaCutoff = 0.5f;
	}

	/// <summary>A material of <paramref name="baseColor"/> with <paramref name="metallic"/> and <paramref name="roughness"/>.</summary>
	public PbrMaterial(Color baseColor, float metallic = 0f, float roughness = 0.5f) : this()
	{
		BaseColor = baseColor;
		Metallic = metallic;
		Roughness = roughness;
	}
}

/// <summary>
/// A custom material shader: the extension point for materials the built-in unlit and PBR shaders do not cover (toon,
/// water, stylized). The renderer draws it through the same passes, sorting and instancing as the built-in materials.
/// </summary>
/// <remarks>
/// <para>
/// Shaders are GLSL 4.5 compiled at build time by <c>Ion.Shaders</c> (add them as <c>IonShader</c> items); the renderer
/// loads the compiled form for its device from <see cref="Assembly"/>. They follow the built-in bind group conventions
/// (see <c>docs/design/ion-rendering3d.md</c>):
/// </para>
/// <list type="bullet">
/// <item>Group 0, the view (shared by every material): binding 0 the <c>View</c> uniform block (matrices, camera, lights,
/// shadow and ambient parameters), 1 the shadow map (<c>texture2D</c>), 2 its comparison sampler (<c>samplerShadow</c>),
/// 3 the environment cube map (<c>textureCube</c>), 4 its sampler.</item>
/// <item>Group 1, the material: binding 0 a <c>Material</c> uniform block of <see cref="UniformSize"/> bytes (std140),
/// bindings 1 to <see cref="TextureCount"/> textures (<c>texture2D</c>), binding 7 the material's sampler.</item>
/// <item>Vertex inputs (when <see cref="VertexShader"/> is set): the standard mesh vertex at locations 0 to 5 and the
/// per-instance world and normal matrices at 6 to 11 (<see cref="MeshVertex"/>). Without a vertex shader the built-in
/// <c>mesh.vert</c> is used, whose outputs are: location 0 world position, 1 world normal, 2 world tangent (w: sign),
/// 3 uv0, 4 uv1, 5 vertex color, 6 flags (x: 1 when the object receives shadows).</item>
/// </list>
/// </remarks>
public sealed class MaterialShaderDescriptor
{
	/// <summary>A name for logs and pipeline labels.</summary>
	public required string Name { get; init; }

	/// <summary>The assembly that embeds the compiled shaders.</summary>
	public required Assembly Assembly { get; init; }

	/// <summary>The fragment shader's file name (for example <c>"toon.frag"</c>).</summary>
	public required string FragmentShader { get; init; }

	/// <summary>The vertex shader's file name, or null for the built-in <c>mesh.vert</c>.</summary>
	public string? VertexShader { get; init; }

	/// <summary>The size of the material's uniform block in bytes (0 for none; rounded up to 16).</summary>
	public int UniformSize { get; init; }

	/// <summary>The number of textures (bindings 1 to this, at most 6).</summary>
	public int TextureCount { get; init; }

	/// <summary>The material sampler (binding 7).</summary>
	public SamplerDescriptor Sampler { get; init; } = new()
	{
		MagFilter = FilterMode.Linear,
		MinFilter = FilterMode.Linear,
		MipmapFilter = FilterMode.Linear,
		AddressModeU = AddressMode.Repeat,
		AddressModeV = AddressMode.Repeat,
	};
}

/// <summary>
/// The vertex every mesh is stored with on the GPU (interleaved, 60 bytes): the vertex layout of <see cref="MeshData"/>.
/// Attributes a mesh lacks get defaults (normal +Y, tangent +X, zero UVs, white).
/// </summary>
/// <remarks>Shader locations: 0 position, 1 normal, 2 tangent (w: bitangent sign), 3 uv0, 4 uv1, 5 color (unorm8x4).</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MeshVertex
{
	/// <summary>The size in bytes.</summary>
	public const int Size = 60;

	/// <summary>The position.</summary>
	public Vector3 Position;

	/// <summary>The normal.</summary>
	public Vector3 Normal;

	/// <summary>The tangent (xyz) and the bitangent sign (w).</summary>
	public Vector4 Tangent;

	/// <summary>The first texture coordinates.</summary>
	public Vector2 Uv0;

	/// <summary>The second texture coordinates.</summary>
	public Vector2 Uv1;

	/// <summary>The vertex color, RGBA8 (R in the lowest byte).</summary>
	public uint Color;
}
