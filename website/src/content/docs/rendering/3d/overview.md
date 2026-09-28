---
title: 3D rendering overview
description: Set up the Rendering3D module, submit cameras, lights and meshes each frame, and understand its culling, instancing and render graph pipeline.
sidebar:
  order: 1
---

`Ion.Extensions.Rendering3D` is Ion's 3D renderer. It is written once against the [RHI](/Ion/rendering/overview/), so
Vulkan and OpenGL ES render the same pixels, and it provides:

- meshes (built-in primitives, your own `MeshData`, glTF models),
- unlit and PBR (metallic-roughness) materials, plus custom material shaders,
- a shadowed directional light and up to 8 point, spot and extra directional lights per camera,
- a skybox that also lights the scene,
- several cameras per frame, render targets, layers,
- automatic frustum culling, sorting and instancing,
- a render graph you can add passes to,
- the 2D sprite batch composited on top.

## Setting up

Add the renderer on the builder and its system on the app. `AddRendering3D()` also registers the engine core
(`AddIon()`), and `UseRendering3D()` adds the engine's systems (`UseIon()`), so this is a complete program:

```csharp title="Program.cs"
using Ion;

var builder = IonApplication.CreateBuilder(args);
builder.AddRendering3D().AddSystem<SceneSystem3D>();

using var game = builder.Build();
game.UseRendering3D().UseSystem<SceneSystem3D>();
game.Run();
```

With the ECS module, `AddEcsRendering3D()` and `UseEcsRendering3D()` pull in the renderer, the ECS module and the engine,
and submit entities for you (see [With the ECS](#with-the-ecs)). Listing the modules explicitly
(`builder.AddIon().AddRendering3D().AddEcs().AddEcsRendering3D()`) changes nothing: each registration is idempotent.

:::note
`builder.AddRendering3D()` and `app.UseRendering3D()` are in the `Ion` namespace (the `Ion` package). The lower-level
`services.AddRendering3D(config)` in `Ion.Extensions.Rendering3D` registers the renderer alone, without the engine.
:::

### Options

`Rendering3DOptions` is bound from `Ion:Rendering3D`. Set it in configuration or in the `AddRendering3D` callback, which
runs after binding:

```csharp
builder.AddRendering3D(options =>
{
	options.ShadowMapSize = 4096;
	options.ShadowDistance = 60f;
});
```

```json title="appsettings.json"
{ "Ion": { "Rendering3D": { "ShadowMapSize": 2048, "ShadowDistance": 20 } } }
```

| Key (`Ion:Rendering3D:...`) | Default | Meaning |
|---|---|---|
| `Shadows` | `true` | Whether the main directional light renders a shadow map |
| `ShadowMapSize` | `2048` | Shadow map width and height in texels |
| `ShadowDistance` | `40` | How far from the camera shadows reach, in world units |
| `DepthPrepass` | `false` | Draw opaque objects into the depth buffer first, so the opaque pass shades each pixel once. Pays off with expensive materials and heavy overdraw; costs a second vertex pass |
| `MaxCameras` | `8` | The most cameras rendered in one frame (the uniform ring is sized for this many) |

## Immediate mode: resources and submissions

The renderer separates two kinds of things:

- **Resources** are created once, at load time, and addressed by small integer handles: meshes (`MeshHandle`),
  materials (`MaterialHandle`), textures (`TextureHandle`), render targets (`RenderTargetHandle`) and custom shaders
  (`MaterialShaderHandle`). Create them in an `[Init]` step (default order or later, so the device exists).
- **Submissions** are made every frame, during the Render stage: cameras, lights and mesh renderers with their world
  matrices. Nothing persists from one frame to the next, except the environment (ambient light and skybox).

```csharp title="SceneSystem3D.cs"
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class SceneSystem3D(IRenderer3D renderer)
{
	private MeshHandle _cube, _ground;
	private MaterialHandle _red, _grey;
	private float _angle;

	[Init]
	public void Load(GameTime dt)
	{
		_cube = renderer.CreateMesh(MeshPrimitives.Cube(1f));
		_ground = renderer.CreateMesh(MeshPrimitives.Plane(40f, 4));
		_red = renderer.CreateMaterial(new PbrMaterial(new Color(0xE0, 0x6A, 0x3B), metallic: 0.1f, roughness: 0.4f));
		_grey = renderer.CreateMaterial(new PbrMaterial(new Color(0x7A, 0x80, 0x8A), metallic: 0f, roughness: 0.9f));
		renderer.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
	}

	[FixedUpdate]
	public void Spin(GameTime dt) => _angle = (_angle + dt.Delta) % MathF.Tau;

	[Render]
	public void Draw(GameTime dt)
	{
		renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, ClearColor = new Color(0x87, 0xA9, 0xD6) },
			Transform.LookAt(new Vector3(0, 6, 12), Vector3.Zero));
		renderer.AddLight(new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), Vector3.Normalize(new Vector3(-0.5f, -0.8f, -0.3f)));

		renderer.Draw(_ground, _grey, Matrix4x4.Identity);
		for (var i = 0; i < 5; i++)
		{
			var world = Matrix4x4.CreateRotationY(_angle + i * 0.4f) * Matrix4x4.CreateTranslation((i - 2) * 2f, 0.5f, 0);
			renderer.Submit(new MeshRenderer(_cube, _red), world);
		}
	}
}
```

The submission API is `IMeshBatch` (which `IRenderer3D` extends):

| Method | Submits |
|---|---|
| `AddCamera(camera, world)` | A camera placed by a world matrix (it looks down its -Z axis). Cameras render by ascending `Priority` |
| `SetCamera(camera, transform)` | Replaces this frame's cameras with one camera |
| `Submit(meshRenderer, world)` | A mesh with a material, shadow flags and layers |
| `Draw(mesh, material, world)` | Shorthand: casts and receives shadows, layer 1 |
| `AddLight(directional, world)` or `AddLight(directional, direction)` | A directional light |
| `AddLight(point, position)` | A point light |
| `AddLight(spot, world)` | A spot light at the matrix's origin, shining along its -Z |
| `SetEnvironment(environment)` | Ambient light and skybox; kept until changed |
| `batch.Draw(model, world)` (extension) | Every primitive of every node of an `IModel` |

Submissions copy their arguments (they are `in` parameters of unmanaged types) into flat arrays that grow by doubling
and are reset each frame, so 10,000 submissions allocate nothing once the arrays have grown. Submit from any Render step;
the renderer does all its work when the Render stage closes.

World matrices use System.Numerics' row-vector convention: `Transform.ToMatrix()` is scale, then rotation, then
translation, and a child's world matrix is `child.ToMatrix() * parentWorld`. See
[Meshes and materials](/Ion/rendering/3d/meshes-and-materials/#transforms).

## The pipeline

When the Render stage closes (the `End` of the renderer's scope at `StageOrder.Rendering3D`), the renderer runs:

1. **Extract.** Already done: your submissions are in flat arrays.
2. **Prepare.**
   - Each object's world bounding box is its mesh's box transformed by the world matrix.
   - Cameras are sorted by `Priority` (then submission order) and resolved against their target into viewport pixels,
     view and projection matrices and a frustum.
   - Light lists: the first directional light is the main light (with the shadow map); extra directional lights and the
     point and spot lights whose range touches the view fill up to 8 local lights per camera.
   - The shadow projection is fitted to the camera's view up to `ShadowDistance` and snapped to whole shadow map
     texels, so shadows do not shimmer as the camera moves.
3. **Queue and sort.** Per camera: layer test, frustum test, then a 64-bit sort key per visible object.
   - Opaque and masked objects: binned by pipeline, material and mesh, **front to back** inside a bin (early depth
     rejection).
   - Blended objects: **back to front** by view depth.
   - The sort allocates nothing.
4. **Batch (instancing).** Runs of one mesh with one material become one instanced draw call. Per-instance data (96
   bytes: the world matrix's first three columns and the normal matrix) for the whole frame is uploaded with one
   `WriteBuffer` into the frame slot's instance buffer. Shadow casters are batched by mesh only.
5. **Render graph.** The frame's passes run on one command encoder: shadow map, optional depth prepass, opaque,
   skybox, transparent, your custom passes, then the 2D overlay.

Measured with `Renderer3DBenchmarks` (10,000 mesh renderers, 3 materials, 2 meshes, 2.1 GHz Xeon VM): extract and queue
take about 1.2 ms with shadows, 0.88 ms without, 1.42 ms with two cameras, and 0 bytes are allocated per frame.

:::tip[Instancing is automatic]
You never set up instancing yourself. Submit the same `MeshHandle` with the same `MaterialHandle` many times and it
becomes one draw call per pass. To keep batches large, share materials: two cubes with materials that have identical
parameters but different handles are two batches.
:::

### Statistics

`IRenderer3D.LastFrameStatistics` reports what the last frame did:

| Field | Meaning |
|---|---|
| `Frame` | The frame number (-1 before the first) |
| `Views` | Cameras rendered |
| `Submitted` | Mesh renderers submitted |
| `Visible`, `Culled` | Objects drawn and culled, summed over the cameras |
| `Batches` | Instanced batches over every pass |
| `DrawCalls` | GPU draw calls over every pass (shadow, prepass, opaque, skybox, transparent) |
| `Triangles` | Triangles drawn over every pass |
| `ShadowCasters` | Objects drawn into the shadow map |
| `Lights` | Lights used |

The draw calls and triangles are also added to the metrics module's `FrameStats` every frame.

```csharp
var s = renderer.LastFrameStatistics;
_hud = $"{s.Visible} visible, {s.Batches} batches, {s.DrawCalls} draw calls, {s.Triangles:N0} triangles";
```

## Drawing 2D on top

The sprite batch works as usual in a 3D game. With the 3D renderer registered, the sprite batch's frame is drawn as the
render graph's last pass (`Overlay2D`), after every 3D pass, so HUD text and UI are always on top:

```csharp
[Render]
public void Draw(GameTime dt)
{
	renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 2, 6), Vector3.Zero));
	renderer.Draw(_cube, _red, Matrix4x4.Identity);
	sprites.DrawString(_font, "Hello, 3D", new Vector2(16, 16), Color.White);   // drawn over the cube
}
```

## Custom passes

The render graph is a small DAG of passes with declared texture reads and writes. Each frame the renderer adds its
passes, then the custom passes you registered with `Renderer3D.AddPass`, then the overlay. The graph orders passes by
`Order` (then insertion), derives dependencies from the declared reads and writes, culls passes whose output nobody
reads, and pools transient textures by lifetime.

| `RenderGraphPass.Orders` | Value |
|---|---|
| `Shadow` | 100 |
| `DepthPrepass` | 200 |
| `Opaque` | 300 |
| `Skybox` | 400 |
| `Transparent` | 500 |
| `Post` | 600 (the first free band) |
| `Overlay` | 900 |

Well-known textures (`RenderGraphResources`): `Backbuffer` (the frame's color target), `ShadowMap` and `Depth` (the depth
buffer of the first camera that renders into the frame).

A post-effect pass that draws a full-screen triangle over the backbuffer:

```csharp
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Rendering3D;

public sealed class TintPass(IRenderPipeline pipeline, IBindGroup bindGroup) : RenderGraphPass("Tint", RenderGraphPass.Orders.Post)
{
	private RenderGraphTexture _target;

	// Called every frame: declare what the pass reads and writes.
	public override void Setup(RenderGraphBuilder builder) =>
		_target = builder.ReadWrite(builder.Find(RenderGraphResources.Backbuffer));

	// Record commands on the graph's encoder.
	public override void Execute(in RenderGraphContext context)
	{
		var pass = context.Encoder.BeginRenderPass(new RenderPassDescriptor(
			[new RenderPassColorAttachment(context.GetTexture(_target).DefaultView, LoadOp.Load, StoreOp.Store)]));
		pass.SetPipeline(pipeline);   // created at load time with a blending color target, see the Shaders page
		pass.SetBindGroup(0, bindGroup);
		pass.Draw(3);                 // a full-screen triangle generated in the vertex shader
		pass.End();
	}
}
```

Register it once, for example in an `[Init]` step, by taking the concrete `Renderer3D` (registered alongside
`IRenderer3D`):

```csharp
public sealed class PostSystem(Renderer3D renderer, IGraphicsFrame frame)
{
	[Init]
	public void Init(GameTime dt)
	{
		var (pipeline, bindGroup) = CreateTintPipeline(frame.Device, frame.ColorFormat);   // your RHI code
		renderer.AddPass(new TintPass(pipeline, bindGroup));
	}

	// Create the shader modules, a pipeline with a blending color target of `format`, and its bind group,
	// as in the Shaders page's TexturedQuad.
	private static (IRenderPipeline Pipeline, IBindGroup BindGroup) CreateTintPipeline(IGraphicsDevice device, TextureFormat format) =>
		throw new NotImplementedException();
}
```

`RenderGraphBuilder` offers `Find(name)`, `Read`, `Write`, `ReadWrite`, `CreateTexture(name, descriptor)` (a pooled
transient texture) and `HasSideEffects()` (keep the pass even if nothing reads its output). `RenderGraphContext` offers
`Encoder`, `Device`, `GetTexture(texture)` and `Flush()`. An effect that samples the scene color should render the
cameras into a render target (`Camera.Target`) and composite it in a pass after them. Remove a pass with
`Renderer3D.RemovePass`. For the RHI calls, see [Shaders](/Ion/rendering/shaders/).

## With the ECS

The ECS module uses the same renderer. `Scene3DExtractionSystem` runs in the Render stage at `StageOrder.Extract` (-300)
and submits every entity with a `MeshRenderer` and a `GlobalTransform` (and no `Hidden` tag), every `Camera`, every
light and the world's `SceneEnvironment`. The components are the same unmanaged structs you pass to `IMeshBatch`.

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<ModelSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<ModelSystem>();
game.Run();
```

```csharp
[Init]
public void Init(GameTime dt)
{
	var cube = renderer.CreateMesh(MeshPrimitives.Cube(0.8f));
	var red = renderer.CreateMaterial(new PbrMaterial(Color.Red, roughness: 0.45f));

	world.Create(Transform.LookAt(new Vector3(0, 5, 10), Vector3.Zero), new Camera { FieldOfView = MathF.PI / 4 });
	world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.6f, -0.6f, -0.3f), Vector3.UnitY)), new DirectionalLight(Color.White, 3f));
	world.Create(new Transform(new Vector3(0, 0.4f, 0)), new MeshRenderer(cube, red));
	world.SetEnvironment(new SceneEnvironment { AmbientIntensity = 0.3f });
}
```

See [ECS rendering](/Ion/ecs/ecs-rendering/) and [Transforms](/Ion/ecs/transforms/) for hierarchies, visibility and
the extraction options, and [Models and glTF](/Ion/rendering/3d/models-gltf/) for `SpawnModel`.

## Headless and tests

Without a GPU (`--headless`), the renderer still accepts resources and submissions and runs its whole CPU pipeline:
culling, sorting and batching. Nothing is drawn, but `LastFrameStatistics` is real, so you can assert on it:

```csharp
using Ion.Extensions.Graphics;
using Ion.Testing;
using Xunit;

public class SceneTests
{
	[Fact]
	public void TheCubesAreBatched()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>();
		host.Step(3);
		var stats = host.Get<IRenderer3D>().LastFrameStatistics;
		Assert.Equal(1, stats.Views);
		Assert.Equal(6, stats.Submitted);   // the ground and five cubes
	}
}
```

With `--headless-render` (or `IonTestHost.WithRendering()`), the renderer draws for real into an offscreen target and
frames can be compared with golden images. See [Testing](/Ion/tooling/testing/) and
[Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/).

## Limits

What the 3D renderer does not do yet:

- One shadow-casting directional light with one cascade. No point or spot light shadows, no shadows from blended
  objects; masked objects cast opaque shadows.
- No MSAA, HDR targets, tone mapping or post-processing stack (the render graph can host them as custom passes).
- No GPU culling and no LOD.
- Image-based lighting uses box-filtered mips of the skybox, not prefiltered specular or an irradiance map.
- No skinning, morph targets or animation; glTF cameras and lights are not imported.
- A custom vertex shader that moves vertices still casts its undeformed shadow.

## See also

- [Meshes and materials](/Ion/rendering/3d/meshes-and-materials/)
- [Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/)
- [Models and glTF](/Ion/rendering/3d/models-gltf/)
- [3D cameras](/Ion/rendering/3d/cameras/)
- [Cubes example](/Ion/examples/cubes/) and [Model example](/Ion/examples/model/)
- Design notes: [docs/design/ion-rendering3d.md](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-rendering3d.md)
