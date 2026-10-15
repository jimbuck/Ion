using System.Numerics;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Draws a mesh with a material: what the renderer needs per object besides its world matrix. Unmanaged; an ECS
/// component as is (the extraction system submits it with the entity's world matrix).
/// </summary>
/// <remarks>Every sub-mesh of <see cref="Mesh"/> is drawn with <see cref="Material"/>.</remarks>
[StructLayout(LayoutKind.Sequential)]
public struct MeshRenderer
{
	/// <summary>The mesh.</summary>
	public MeshHandle Mesh;

	/// <summary>The material (none: the renderer's default white PBR material).</summary>
	public MaterialHandle Material;

	/// <summary>Whether the object is drawn into the shadow map.</summary>
	public bool CastShadows;

	/// <summary>Whether shadows darken the object.</summary>
	public bool ReceiveShadows;

	/// <summary>
	/// The layers the object is on (bit mask; a camera draws it when this shares a bit with its
	/// <see cref="Camera.CullingMask"/>). 0 is treated as layer 1, the default.
	/// </summary>
	public uint LayerMask;

	/// <summary>A renderer that casts and receives shadows on layer 1.</summary>
	public MeshRenderer()
	{
		CastShadows = true;
		ReceiveShadows = true;
		LayerMask = 1;
	}

	/// <summary>A renderer of <paramref name="mesh"/> with <paramref name="material"/> that casts and receives shadows on layer 1.</summary>
	public MeshRenderer(MeshHandle mesh, MaterialHandle material) : this()
	{
		Mesh = mesh;
		Material = material;
	}
}

/// <summary>
/// A directional light (the sun): parallel rays along the -Z axis of its transform. The first directional light of a
/// frame that casts shadows gets the shadow map.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DirectionalLight
{
	/// <summary>The light color (sRGB).</summary>
	public Color Color;

	/// <summary>The intensity (1: the color at full strength on a surface facing the light).</summary>
	public float Intensity;

	/// <summary>Whether the light casts shadows.</summary>
	public bool CastShadows;

	/// <summary>The shadow depth bias in shadow map texels (0: the renderer's default).</summary>
	public float ShadowBias;

	/// <summary>A white light of intensity 1 that casts shadows.</summary>
	public DirectionalLight()
	{
		Color = Color.White;
		Intensity = 1f;
		CastShadows = true;
	}

	/// <summary>A light of <paramref name="color"/> and <paramref name="intensity"/>.</summary>
	public DirectionalLight(Color color, float intensity = 1f, bool castShadows = true) : this()
	{
		Color = color;
		Intensity = intensity;
		CastShadows = castShadows;
	}
}

/// <summary>A point light: radiates from its position, fading to zero at <see cref="Range"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PointLight
{
	/// <summary>The light color (sRGB).</summary>
	public Color Color;

	/// <summary>The intensity.</summary>
	public float Intensity;

	/// <summary>The distance at which the light has faded out.</summary>
	public float Range;

	/// <summary>A white light of intensity 1 and range 10.</summary>
	public PointLight()
	{
		Color = Color.White;
		Intensity = 1f;
		Range = 10f;
	}

	/// <summary>A light of <paramref name="color"/>, <paramref name="intensity"/> and <paramref name="range"/>.</summary>
	public PointLight(Color color, float intensity = 1f, float range = 10f)
	{
		Color = color;
		Intensity = intensity;
		Range = range;
	}
}

/// <summary>A spot light: a cone along the -Z axis of its transform, fading between the inner and outer angles and to zero at <see cref="Range"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpotLight
{
	/// <summary>The light color (sRGB).</summary>
	public Color Color;

	/// <summary>The intensity.</summary>
	public float Intensity;

	/// <summary>The distance at which the light has faded out.</summary>
	public float Range;

	/// <summary>The half angle, in radians, inside which the light is at full strength.</summary>
	public float InnerConeAngle;

	/// <summary>The half angle, in radians, outside which there is no light.</summary>
	public float OuterConeAngle;

	/// <summary>A white spot of intensity 1, range 10, cone 20 to 30 degrees.</summary>
	public SpotLight()
	{
		Color = Color.White;
		Intensity = 1f;
		Range = 10f;
		InnerConeAngle = MathF.PI / 9f;
		OuterConeAngle = MathF.PI / 6f;
	}
}

/// <summary>
/// The frame's environment: ambient light and an optional skybox cube map, which also gives the PBR shader its image
/// based ambient (diffuse from a blurred mip, specular by roughness). Named <c>SceneEnvironment</c> rather than
/// <c>Environment</c> to stay clear of <see cref="System.Environment"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceneEnvironment
{
	/// <summary>The ambient light color (sRGB), multiplied with <see cref="AmbientIntensity"/>.</summary>
	public Color AmbientColor;

	/// <summary>The ambient light strength.</summary>
	public float AmbientIntensity;

	/// <summary>The skybox cube map (a <see cref="TextureHandle"/> of a cube texture, see <c>ICubemap</c>), or none.</summary>
	public TextureHandle Skybox;

	/// <summary>A multiplier of the skybox, as drawn and as ambient light.</summary>
	public float SkyboxIntensity;

	/// <summary>A dim grey ambient, no skybox.</summary>
	public SceneEnvironment()
	{
		AmbientColor = new Color(0.25f, 0.25f, 0.25f, 1f);
		AmbientIntensity = 1f;
		SkyboxIntensity = 1f;
	}
}

/// <summary>What the renderer knows about a mesh.</summary>
/// <param name="Bounds">The bounding box in the mesh's local space.</param>
/// <param name="Sphere">The bounding sphere in the mesh's local space.</param>
/// <param name="VertexCount">Vertices.</param>
/// <param name="IndexCount">Indices (three per triangle).</param>
/// <param name="SubMeshCount">Sub-meshes (index ranges drawn one after the other).</param>
/// <param name="Attributes">The attributes the source data had (the GPU vertex always has all, see <see cref="MeshVertex"/>).</param>
public readonly record struct MeshInfo(Aabb Bounds, BoundingSphere Sphere, int VertexCount, int IndexCount, int SubMeshCount, VertexAttributes Attributes);

/// <summary>What the 3D renderer did in one frame.</summary>
/// <param name="Frame">The frame number (-1 before the first).</param>
/// <param name="Views">Cameras rendered.</param>
/// <param name="Submitted">Mesh renderers submitted.</param>
/// <param name="Visible">Objects drawn, summed over the cameras (after frustum and layer culling).</param>
/// <param name="Culled">Objects culled, summed over the cameras.</param>
/// <param name="Batches">Instanced batches (runs of one mesh and material) over every pass.</param>
/// <param name="DrawCalls">GPU draw calls over every pass (shadow, prepass, opaque, skybox, transparent).</param>
/// <param name="Triangles">Triangles drawn over every pass.</param>
/// <param name="ShadowCasters">Objects drawn into the shadow map.</param>
/// <param name="Lights">Lights used (the directional light and the point and spot lights of the frame).</param>
public readonly record struct Rendering3DStatistics(long Frame, int Views, int Submitted, int Visible, int Culled, int Batches, int DrawCalls, int Triangles, int ShadowCasters, int Lights);
