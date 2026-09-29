---
title: Models and glTF
description: Load glTF 2.0 and GLB models as IModel, draw them immediately or spawn them as ECS entity hierarchies, and load cube maps for skyboxes.
sidebar:
  order: 4
---

Ion loads 3D models from **glTF 2.0** files (`.gltf` with external or embedded buffers, and binary `.glb`) as `IModel`
assets. A model brings its meshes, PBR materials, textures and node hierarchy. You can draw it directly every frame, or
spawn it into an ECS world as one entity per node.

```csharp
var avocado = assets.Load<IModel>("Avocado/Avocado.gltf");
```

The loader is registered by the 3D renderer (`AddRendering3D()`, or `AddEcsRendering3D()`), and like every asset the
model is cached: loading the same path again returns the same instance. See [Assets](/Ion/rendering/assets/) for paths
and the assets folder.

## What a model contains

| Member | Meaning |
|---|---|
| `Nodes` | Every `ModelNode` of the default scene (parents may come before or after children) |
| `RootNodes` | Indices of the root nodes |
| `Meshes`, `Materials`, `Textures` | The handles the loader created (one mesh per glTF primitive) |
| `Bounds` | An `Aabb` around every node's meshes, in model space |
| `Name` | The path it was loaded from |

Each `ModelNode` has a `Name`, a `Parent` index (-1 for a root), `Children` indices, a `LocalTransform` (relative to its
parent), a `ModelMatrix` (relative to the model's root, every ancestor applied) and `Primitives`: the
`ModelPrimitive(Mesh, Material)` pairs drawn at that node, possibly none.

```csharp
logger.LogInformation("Loaded {Model}: {Nodes} nodes, {Meshes} meshes, {Materials} materials, {Textures} textures, bounds {Bounds}.",
	model.Name, model.Nodes.Count, model.Meshes.Count, model.Materials.Count, model.Textures.Count, model.Bounds);
```

## Drawing a model immediately

The `Draw` extension on `IMeshBatch` submits every primitive of every node at the model's matrices times your world
matrix:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class ModelViewerSystem(IRenderer3D renderer, IAssetManager assets)
{
	private IModel _model = null!;
	private float _angle;

	[Init]
	public void Load(GameTime dt) => _model = assets.Load<IModel>("Avocado/Avocado.gltf");

	[Update]
	public void Turn(GameTime dt) => _angle += 0.4f * dt.Delta;

	[Render]
	public void Draw(GameTime dt)
	{
		renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f }, Transform.LookAt(new Vector3(0, 2, 6.5f), new Vector3(0, 1.1f, 0)));
		renderer.AddLight(new DirectionalLight(Color.White, 2.5f), new Vector3(-0.35f, -0.55f, -1f));

		// The Avocado is 6 cm tall: scale it 40x, then turn it.
		var world = Matrix4x4.CreateScale(40f) * Matrix4x4.CreateRotationY(_angle);
		renderer.Draw(_model, world, castShadows: true, receiveShadows: true, layerMask: 1);
	}
}
```

Use `model.Bounds` to frame the camera or to scale an unknown model to a target size:

```csharp
var size = _model.Bounds.Size;
var scale = 2f / MathF.Max(size.X, MathF.Max(size.Y, size.Z));                      // fit in 2 world units
var world = Matrix4x4.CreateTranslation(-_model.Bounds.Center) * Matrix4x4.CreateScale(scale);   // centered on the origin
```

## Spawning a model as entities

With the ECS module, `SpawnModel` turns a model into an entity hierarchy that the 3D extraction submits every frame:

```csharp
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public readonly record struct Spin(float Speed);

public sealed partial class ModelSystem(World world, IAssetManager assets)
{
	private Entity _avocado;

	[Init]
	public void Init(GameTime dt)
	{
		var model = assets.Load<IModel>("Avocado/Avocado.gltf");

		// A root entity (scaled here) with one entity per glTF node under it.
		_avocado = world.SpawnModel(model, new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(40f)));
		world.Add(_avocado, new Spin(0.4f));
	}

	// Turns every entity with a Spin (a generated chunk loop).
	[Update, Query]
	private static void Turn(ref Transform transform, in Spin spin, GameTime dt) =>
		transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)dt.Elapsed.TotalSeconds * spin.Speed);
}
```

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<ModelSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<ModelSystem>();
game.Run();
```

What `SpawnModel` creates:

- A **root entity** with the `Transform` you pass (and an `EntityName` with the model's name).
- **One entity per node**, with `Transform` set to the node's local transform, parented as the nodes are, the model's
  root nodes under the root entity, and an `EntityName` with the node's name.
- A node with **one primitive** gets its `MeshRenderer` on its own entity. A node with **several primitives** gets one
  child entity per primitive (identity transform, one `MeshRenderer` each), since an entity holds one mesh renderer.

The entities are ordinary ECS entities:

| To | Do |
|---|---|
| Move, turn or scale the whole model | Change the root entity's `Transform` |
| Hide it | `world.SetHidden(root, true)` (recursive by default; the `Hidden` tag alone hides only one entity) |
| Remove it | `world.DestroyRecursive(root)` |
| Find a part | Search `EntityName` components, or walk `world.GetChildren(root)` |

Options (`ModelSpawnOptions`, create it with `new()`):

| Field | Default | Meaning |
|---|---|---|
| `CastShadows` | `true` | For every spawned `MeshRenderer` |
| `ReceiveShadows` | `true` | |
| `LayerMask` | `1` | Matched against `Camera.CullingMask` |
| `Names` | `true` | Add `EntityName` components |

```csharp
var tree = world.SpawnModel(model, new Transform(new Vector3(5, 0, 2)), new ModelSpawnOptions { CastShadows = false, Names = false });
```

:::caution
`default(ModelSpawnOptions)` casts no shadows and has an empty layer mask. Always start from `new ModelSpawnOptions()`
(or `ModelSpawnOptions.Default`).
:::

Inside a query or a system that must not make structural changes, use the `Commands` overload:
`commands.SpawnModel(model, transform)` returns a placeholder root entity immediately (usable with the same commands,
for example `commands.SetParent`), and the node entities are created when the commands play back. See
[Entities and commands](/Ion/ecs/entities-and-commands/).

The model's meshes and materials stay owned by the model asset. Unloading the model (`assets.Unload(model)`, or the end
of the scene that loaded it) releases them; spawned entities that still refer to them then draw with the default
material or nothing.

## What the loader supports

Ion has its own glTF reader over `System.Text.Json`: reflection-free and NativeAOT clean.

| Supported | Details |
|---|---|
| Files | `.gltf` with external buffers and images, base64 data URIs, and `.glb` (binary, version 2) |
| Primitives | Triangle lists; other primitive modes are skipped |
| Attributes | `POSITION`, `NORMAL`, `TANGENT`, `TEXCOORD_0`, `TEXCOORD_1`, `COLOR_0` (any component type, normalized or not) |
| Indices | 8, 16 and 32-bit |
| Materials | Metallic-roughness PBR (all factors and textures, normal scale, occlusion strength, emissive), `alphaMode` and `alphaCutoff`, `doubleSided` |
| Extensions | `KHR_materials_unlit` (becomes an `UnlitMaterial`), `KHR_materials_emissive_strength` |
| Scene | Node TRS or matrices, the default scene (nodes not reachable from it are dropped) |

Converted on load:

- Every primitive becomes a mesh; missing normals and tangents are generated.
- glTF's linear color factors are converted to Ion's sRGB `Color`s. Emissive factors above 1 keep their hue in
  `Emissive` and their magnitude in `EmissiveIntensity`.
- A primitive without a material gets glTF's default: white, metallic 1, roughness 1.
- Images are decoded with ImageSharp (straight alpha, CPU mip chain) and created through the 2D renderer's
  `TextureFactory`, so the sprite batch can draw them too.

**Not supported** (a clear `InvalidDataException`): glTF 1.0, sparse accessors, and any *required* extension other than
the two above (for example Draco or meshopt compression, texture transforms).

**Ignored**: skins, animations, morph targets, cameras, lights, and sampler objects (every material uses one trilinear,
repeating sampler).

:::tip
Convert models that need unsupported features with a tool such as Blender or `gltf-transform` (for example to remove
Draco compression) before adding them to your assets.
:::

### Headless

Under `--headless` the model still loads: geometry and materials are created in the CPU-only renderer, and textures are
placeholders. The node tree, handles and bounds are all available, so tests can assert on models and on the spawned
entities without a GPU.

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
host.Step(3);
var stats = host.Get<IRenderer3D>().LastFrameStatistics;
Assert.True(stats.Submitted > 0);
```

## Cube maps

`ICubemap` assets are loaded from a folder of six square images of one size, for skyboxes and image-based ambient:

```csharp
var skybox = assets.Load<ICubemap>("Skybox");
renderer.SetEnvironment(new SceneEnvironment { Skybox = skybox.Handle });
```

| Face | Accepted names | Cube layer |
|---|---|---|
| Right | `px`, `right`, `posx` | +X |
| Left | `nx`, `left`, `negx` | -X |
| Top | `py`, `top`, `up`, `posy` | +Y |
| Bottom | `ny`, `bottom`, `down`, `negy` | -Y |
| Front | `pz`, `front`, `posz` | +Z |
| Back | `nz`, `back`, `negz` | -Z |

Extensions `.png`, `.jpg` and `.jpeg` are tried. A full mip chain is built on the CPU: the PBR shader reads blurrier
mips for rougher reflections and the smallest one for diffuse ambient. A camera looking down -Z (Ion's forward) sees the
front image unmirrored, the right image on its right and the top image above.

`ICubemap.Handle` is the `TextureHandle` for `SceneEnvironment.Skybox`; `Size` is the face size in texels. Missing faces,
non-square images or mismatched sizes throw with the file name.

## Your own model formats

For other formats, build `MeshData` yourself and create meshes and materials directly (see
[Meshes and materials](/Ion/rendering/3d/meshes-and-materials/)). To make it an asset, write an `IAssetLoader<T>` for
your own asset interface (see [Assets](/Ion/rendering/assets/#writing-a-loader)).

## See also

- [Model example](/Ion/examples/model/): the Avocado, PBR spheres, a skybox and point lights
- [Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/)
- [ECS rendering](/Ion/ecs/ecs-rendering/) and [Transforms](/Ion/ecs/transforms/)
- [Assets](/Ion/rendering/assets/)
