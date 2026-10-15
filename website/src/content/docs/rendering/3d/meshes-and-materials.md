---
title: Meshes and materials
description: Create meshes from primitives or your own vertex data, place them with transforms, and shade them with unlit, PBR or custom materials.
sidebar:
  order: 2
---

A 3D object in Ion is a **mesh** (geometry) drawn with a **material** (how its surface looks) at a **world matrix**
(where it is). Meshes and materials are renderer resources you create once and refer to by handle; the world matrix is
passed with every submission.

```csharp
var sphere = renderer.CreateMesh(MeshPrimitives.Sphere(radius: 0.5f));
var gold = renderer.CreateMaterial(new PbrMaterial(new Color(0xE6, 0xB8, 0x5C), metallic: 1f, roughness: 0.3f));

// Every frame, in a Render step:
renderer.Draw(sphere, gold, Matrix4x4.CreateTranslation(0, 1, 0));
```

## Primitive meshes

`MeshPrimitives` builds common shapes as `MeshData`, centered on the origin, with normals, tangents and texture
coordinates, and counter-clockwise front faces (the RHI's default):

| Builder | Defaults | Shape |
|---|---|---|
| `Cube(size)` | `size = 1` | Axis-aligned cube, 24 vertices (flat faces), each face's UVs covering the whole texture |
| `Sphere(radius, segments, rings)` | `0.5`, `32`, `16` | UV sphere: `segments` around the equator, `rings` from pole to pole |
| `Plane(size, subdivisions)` | `1`, `1` | Square in the XZ plane facing +Y, split into `subdivisions` quads per side; UVs 0 to 1, v growing towards +Z |
| `Cylinder(radius, height, segments)` | `0.5`, `1`, `32` | Along the y axis, closed by flat caps (smooth side normals, flat cap normals) |

```csharp
var ground = renderer.CreateMesh(MeshPrimitives.Plane(80f, 8));
var pillar = renderer.CreateMesh(MeshPrimitives.Cylinder(0.3f, 4f, 24));
```

## Your own geometry: MeshData

`MeshData` holds geometry on the CPU as separate attribute arrays, 32-bit triangle-list indices and sub-meshes. Only
positions and indices are required:

```csharp
using System.Numerics;

using Ion.Extensions.Graphics;

// A single triangle with vertex colors.
var data = new MeshData("triangle",
	positions: [new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1.5f, 0)],
	indices: [0, 1, 2])
{
	Colors = [Color.Red, Color.Green, Color.Blue],
	Uv0 = [new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0)],
};

MeshHandle triangle = renderer.CreateMesh(data);
```

| Member | Meaning |
|---|---|
| `Name` | For logs |
| `Positions` | Required; one per vertex |
| `Normals`, `Tangents` (xyz plus bitangent sign in w) | Optional; computed by `CreateMesh` when missing |
| `Uv0`, `Uv1` | Optional texture coordinates |
| `Colors` | Optional vertex colors, multiplied with the material color |
| `Indices` | Triangle list, three per triangle, counter-clockwise front faces |
| `SubMeshes` | Index ranges (`SubMesh(FirstIndex, IndexCount, BaseVertex)`); one over all indices by default |
| `Validate()` | Throws `InvalidOperationException` if an attribute array has the wrong length, the index count is not a multiple of three, or an index is out of range |
| `ComputeNormals()` | Smooth, area-weighted normals |
| `ComputeTangents()` | Tangents from positions, normals and `Uv0` (Gram-Schmidt, handedness in w) |
| `ComputeBounds()`, `ComputeSphere()` | Local bounding box and sphere |

`IRenderer3D.CreateMesh(data)` validates the data, fills in missing normals and tangents (on the `MeshData` you
passed), computes the bounds, and uploads it. On the GPU every mesh uses the same interleaved 60-byte `MeshVertex`
(position, normal, tangent, uv0, uv1, RGBA8 color). Missing attributes get defaults: normal +Y, tangent +X, zero UVs,
white.

:::note
Every sub-mesh of a mesh is drawn with the one material of its `MeshRenderer`. For several materials, create one mesh
per material (the glTF loader does this: one mesh per glTF primitive).
:::

Other mesh methods:

| Method | Meaning |
|---|---|
| `GetMeshInfo(mesh)` | `MeshInfo`: local `Bounds` (`Aabb`), `Sphere`, `VertexCount`, `IndexCount`, `SubMeshCount`, the source `Attributes` |
| `DestroyMesh(mesh)` | Releases the mesh; its GPU buffers go once no frame in flight uses them |

## Transforms

`Transform` is a position, rotation (quaternion) and scale, unmanaged and 40 bytes, usable as an ECS component:

```csharp
var t = new Transform(new Vector3(0, 1, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), new Vector3(2));
Matrix4x4 world = t.ToMatrix();

var camera = Transform.LookAt(eye: new Vector3(0, 5, 10), target: Vector3.Zero);   // -Z looks at the target
```

| Member | Meaning |
|---|---|
| `new Transform()`, `Transform.Identity` | At the origin, identity rotation, unit scale |
| `new Transform(position, rotation?, scale?)` | Identity rotation and unit scale when omitted |
| `LookAt(eye, target, up?)` | A transform at `eye` whose -Z axis looks at `target` |
| `LookRotation(forward, up)` | A rotation whose -Z points along `forward` |
| `FromMatrix(matrix)` | Decomposes a matrix |
| `Forward` (-Z), `Right` (+X), `Up` (+Y) | Local axes in world space |
| `ToMatrix()` | `S * R * T` (scale, rotation, translation) |
| `TransformPoint`, `TransformDirection`, `LookAtTarget` | Helpers |

Conventions: right-handed, y up, cameras and lights look down their local -Z axis, row vectors
(`Vector3.Transform(p, matrix)`). A parent is applied after a child: `world = child.ToMatrix() * parentWorld`.

:::danger[default(Transform) is not the identity]
`default(Transform)` has a zero scale and a zero quaternion, which collapses the object to a point. Always create
transforms with `new Transform()`, `Transform.Identity` or a constructor.
:::

## Mesh renderers

`MeshRenderer` is what the renderer needs per object besides its world matrix. It is an unmanaged struct and an ECS
component as is.

| Field | Default (`new MeshRenderer()` or the two-argument constructor) | Meaning |
|---|---|---|
| `Mesh` | | The mesh |
| `Material` | none: the renderer's default white PBR material | The material |
| `CastShadows` | `true` | Drawn into the shadow map |
| `ReceiveShadows` | `true` | Darkened by shadows |
| `LayerMask` | `1` | Layers (bit mask) matched against `Camera.CullingMask`; 0 is treated as layer 1 |

```csharp
var glass = new MeshRenderer(_sphere, _glassMaterial) { CastShadows = false };
renderer.Submit(glass, Matrix4x4.CreateTranslation(2, 1, 0));
```

`renderer.Draw(mesh, material, world)` is shorthand for a renderer that casts and receives shadows on layer 1.

## Materials

Colors in materials are **sRGB**, like every Ion `Color`; the renderer converts them to linear for lighting. Shaders
light in linear space and encode sRGB at the end.

### Unlit

`UnlitMaterial`: a color times an optional texture times the vertex color. No lighting, no shadows. Good for UI in 3D,
emissive signs, stylized looks and debugging.

| Field | Default | Meaning |
|---|---|---|
| `BaseColor` | `Color.White` | The color |
| `Texture` | none | Sampled as sRGB color |
| `AlphaMode` | `Opaque` | See [Transparency](#transparency) |
| `AlphaCutoff` | `0.5` | For `AlphaMode.Mask` |
| `DoubleSided` | `false` | Draw back faces too |

```csharp
var marker = renderer.CreateMaterial(new UnlitMaterial(new Color(0xFF, 0x40, 0x40)));
```

### PBR

`PbrMaterial` is glTF 2.0's metallic-roughness material: Cook-Torrance specular with the GGX distribution,
height-correlated Smith visibility and Schlick Fresnel, and Lambert diffuse.

| Field | Default | Meaning |
|---|---|---|
| `BaseColor` | `Color.White` | Albedo for dielectrics, specular color for metals; alpha is opacity |
| `BaseColorTexture` | none | sRGB, multiplied with `BaseColor` |
| `Metallic` | `0` | Metalness, 0 to 1 |
| `Roughness` | `0.5` | Perceptual roughness, 0 (mirror) to 1 (matte) |
| `MetallicRoughnessTexture` | none | glTF layout: roughness in green, metalness in blue; multiplied with the factors |
| `Normal` | none | Tangent-space normal map |
| `NormalScale` | `1` | Strength of the normal map's x and y |
| `Occlusion` | none | Ambient occlusion in the red channel |
| `OcclusionStrength` | `1` | How much occlusion darkens ambient light, 0 to 1 |
| `Emissive` | `Color.Black` | Emitted color (sRGB) |
| `EmissiveIntensity` | `1` | Multiplier; above 1 for glowing surfaces |
| `EmissiveTexture` | none | sRGB |
| `AlphaMode` | `Opaque` | See [Transparency](#transparency) |
| `AlphaCutoff` | `0.5` | For `AlphaMode.Mask` |
| `DoubleSided` | `false` | Draw back faces, lit with a flipped normal |

```csharp
var brushedSteel = renderer.CreateMaterial(new PbrMaterial(new Color(0xB0, 0xB4, 0xBC), metallic: 1f, roughness: 0.35f));

var lamp = renderer.CreateMaterial(new PbrMaterial(Color.White)
{
	Emissive = new Color(0xFF, 0xD0, 0x80),
	EmissiveIntensity = 4f,
});
```

A row of spheres from polished to rough metal, as in the Model sample:

```csharp
for (var i = 0; i < 5; i++)
{
	var material = renderer.CreateMaterial(new PbrMaterial(new Color(0xE6, 0xB8, 0x5C), metallic: 1f, roughness: 0.1f + 0.2f * i));
	_spheres[i] = (material, Matrix4x4.CreateTranslation((i - 2) * 1.2f, 0.45f, -2.2f));
}
```

Metals reflect their surroundings, so they look best with a skybox (see
[Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/#skybox-and-image-based-ambient)).

### Textures

Material textures are `TextureHandle`s registered with the renderer:

| Method | Registers |
|---|---|
| `CreateTexture(ITexture2D texture)` | A texture from the 2D renderer (`Load<ITexture2D>`, `TextureFactory`, a `RenderTarget2D`). The texture stays owned by its loader |
| `CreateTexture(ITexture texture, ownsTexture)` | An RHI texture (2D or cube map). With `ownsTexture: true` the renderer disposes it at teardown |
| `GetRenderTargetTexture(target)` | The color texture of a 3D render target |

```csharp
var bricks = assets.Load<ITexture2D>("bricks.png");
var wall = renderer.CreateMaterial(new PbrMaterial(Color.White) { BaseColorTexture = renderer.CreateTexture(bricks), Roughness = 0.8f });
```

Every material samples with one trilinear, repeating sampler.

:::caution[Premultiplied textures]
Textures loaded with `Load<ITexture2D>` are premultiplied for the sprite batch. That is exact for opaque textures only.
For textures with transparency, or data textures (normal maps, metallic-roughness maps), create them with
`TextureFactory.Create(name, width, height, rgba, premultiply: false)`, as the glTF loader does.
:::

### Changing and destroying materials

| Method | Meaning |
|---|---|
| `UpdateMaterial(handle, in UnlitMaterial)` / `UpdateMaterial(handle, in PbrMaterial)` | Change parameters, textures included. The uniform block is re-uploaded before the next frame |
| `Renderer3D.UpdateMaterial(handle, uniforms)` | Change a custom material's uniform bytes |
| `DestroyMaterial(handle)` | Release a material. Objects submitted with it fall back to the default material |
| `Renderer3D.DefaultMaterial` | The white dielectric used for renderers without a (valid) material |

```csharp
// Pulse an emissive material.
_lampParams.EmissiveIntensity = 2f + MathF.Sin(_time * 4f);
renderer.UpdateMaterial(_lamp, _lampParams);
```

Updating a material is cheaper than creating one per frame, and keeps the handle (and therefore the batch) stable.

## Transparency

`AlphaMode` (glTF's `alphaMode`) decides how alpha is used:

| `AlphaMode` | Pass | Behavior |
|---|---|---|
| `Opaque` | Opaque, front to back, instanced | Alpha ignored |
| `Mask` | Opaque | Fragments with alpha below `AlphaCutoff` are discarded; the rest are opaque. Casts (opaque) shadows |
| `Blend` | Transparent, back to front, after the skybox | Alpha blended; casts no shadow |

```csharp
var glass = renderer.CreateMaterial(new PbrMaterial(new Color(0.7f, 0.85f, 1f, 0.35f))
{
	AlphaMode = AlphaMode.Blend,
	Roughness = 0.05f,
});
```

Blended objects are sorted per object by view depth, not per triangle; intersecting transparent objects can sort
wrongly.

## Custom material shaders

When the built-in materials do not cover a look (toon, water, stylized), register a custom material shader. It draws
through the same passes, sorting, instancing and shadows as the built-in materials.

1. Write a fragment shader (and optionally a vertex shader) in GLSL 4.5 and compile it at build time as an `IonShader`
   item (see [Shaders](/Ion/rendering/shaders/)).
2. Include the renderer's `ion_view.glsl` (group 0: the view block, shadow map, environment, `ionOutput()`) and
   `ion_fragment_inputs.glsl` (the outputs of the built-in `mesh.vert`, and `ionShadow()`), found in
   `Ion/Ion.Extensions.Rendering3D/Shaders`.
3. Put your parameters in a `Material` uniform block at `set = 1, binding = 0` (std140), textures at bindings 1 to 6,
   and the material sampler at binding 7.

```glsl title="Shaders/stripes.frag"
#version 450
// The material's two colors in vertical stripes of world x (1 unit wide), unlit.
// Include paths are relative to this file: point them at your Ion checkout.

#include "../Ion/Ion/Ion.Extensions.Rendering3D/Shaders/ion_view.glsl"
#include "../Ion/Ion/Ion.Extensions.Rendering3D/Shaders/ion_fragment_inputs.glsl"

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform Material
{
	vec4 uColorA;
	vec4 uColorB;
};

layout(set = 1, binding = 7) uniform sampler uMaterialSampler;

void main()
{
	float stripe = mod(floor(vWorldPosition.x), 2.0);
	outColor = mix(uColorA, uColorB, stripe);
}
```

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;

var shader = renderer.CreateMaterialShader(new MaterialShaderDescriptor
{
	Name = "stripes",
	Assembly = typeof(MyGame).Assembly,   // the assembly that embeds the compiled shader
	FragmentShader = "stripes.frag",
	UniformSize = 32,                      // two vec4
	TextureCount = 0,
});

var colors = new Vector4[] { new(1, 0, 0, 1), new(0, 0, 1, 1) };
var stripes = renderer.CreateMaterial(shader, MemoryMarshal.AsBytes(colors.AsSpan()), [], AlphaMode.Opaque);
```

`MaterialShaderDescriptor`:

| Property | Meaning |
|---|---|
| `Name` (required) | For logs and pipeline labels |
| `Assembly` (required) | The assembly that embeds the compiled shaders |
| `FragmentShader` (required) | The fragment shader's file name, for example `"toon.frag"` |
| `VertexShader` | A vertex shader's file name, or `null` for the built-in `mesh.vert` |
| `UniformSize` | Size of the `Material` block in bytes (0 for none; rounded up to 16) |
| `TextureCount` | Textures at bindings 1 to this, at most 6 |
| `Sampler` | The material sampler (binding 7). Default: linear, trilinear, repeating |

The built-in `mesh.vert` writes: location 0 world position (`vWorldPosition`), 1 world normal (`vNormal`), 2 world
tangent with sign in w (`vTangent`), 3 `vUv0`, 4 `vUv1`, 5 vertex color (`vColor`), 6 flags (`vFlags.x` is 1 when the
object receives shadows). A custom vertex shader reads the mesh vertex at locations 0 to 5 and the per-instance world
and normal matrices at 6 to 11. For lit custom materials, compute in linear space, multiply by `ionShadow(worldPosition,
normal)` for the main light, and write `ionOutput(linearColor, alpha)`, which encodes sRGB when the target needs it.

:::note
Custom material uniform bytes are passed as is: colors are not converted from sRGB for you. A custom vertex shader that
moves vertices still casts the shadow of the undeformed mesh (the shadow pass uses the built-in depth-only vertex
stage).
:::

## See also

- [Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/)
- [Models and glTF](/Ion/rendering/3d/models-gltf/): meshes and materials from files
- [Shaders](/Ion/rendering/shaders/)
- [ECS transforms](/Ion/ecs/transforms/): `Transform`, `GlobalTransform` and hierarchies
