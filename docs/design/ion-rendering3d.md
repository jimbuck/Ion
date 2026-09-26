# Ion 3D renderer (`Ion.Extensions.Rendering3D`)

Status: implemented (Stage 5, 3D half, September 2026). This document describes the pipeline, the render graph API,
the material extension point and the contract the ECS extraction wave builds on. The roadmap context is section 4.8 of
[the engine review](../plans/2026-09-engine-review-and-roadmap.md).

## 1. Shape

One renderer serves immediate-mode games and the ECS. Games (or the ECS extraction systems) submit cameras, lights and
mesh renderers every frame during the Render stage; the renderer owns everything after that: culling, sorting,
batching, uploads, passes and the 2D overlay. Nothing persists between frames except resources (meshes, materials,
textures, render targets) and the environment.

```
Ion.Extensions.Graphics.Abstractions   3D data types (plain unmanaged structs), IMeshBatch / IRenderer3D, IModel, ICubemap,
                                       MeshData and primitive builders, Aabb / BoundingSphere / Frustum; RHI cube maps
Ion.Extensions.Rendering3D             Renderer3D (the pipeline), RenderGraph, shaders, glTF and cube map loaders
Ion.Extensions.Rendering2D             the sprite batch, drawn as the 3D render graph's last pass (the overlay)
```

Registration: `services.AddRendering3D(config)` after `AddIon`, and `app.UseRendering3D()` after `UseIon()`. With an RHI
backend (`IGraphicsFrame` registered) the renderer draws; with the headless null backend it runs its CPU pipeline only
(the statistics are real, nothing is drawn), so headless tests of 3D games work without a GPU.

## 2. Data types

All live in `Ion.Extensions.Graphics` (the abstractions assembly) and, except `MeshData` and `ModelNode`, are unmanaged
structs that can be ECS components as they are. Handles are integer ids (0 means none).

| Type | Content |
|---|---|
| `Transform` | `Position`, `Rotation` (quaternion), `Scale`; `ToMatrix()` is `S * R * T` (row vectors, `child * parent`). `new Transform()` is the identity; `default(Transform)` has a zero scale. `LookAt`, `LookRotation`, `FromMatrix`, `Forward` (-Z), `Right`, `Up`. |
| `Camera` | `Projection` (perspective or orthographic), `FieldOfView` (vertical, radians), `OrthographicSize` (half height), `Near`, `Far`, `Viewport` (normalized, top-left origin), `ClearColor`, `Clear` (`Color`, `Skybox`, `None`), `Priority`, `Target` (`RenderTargetHandle`, none = the frame), `CullingMask`. Zero fields mean defaults, so `default(Camera)` renders. |
| `MeshRenderer` | `Mesh`, `Material`, `CastShadows`, `ReceiveShadows`, `LayerMask` (0 = layer 1). |
| `DirectionalLight`, `PointLight`, `SpotLight` | Color (sRGB) and intensity; range for point and spot; cone angles for spot; `CastShadows` and `ShadowBias` for directional. Directions come from the light's world matrix (-Z). |
| `SceneEnvironment` | `AmbientColor`, `AmbientIntensity`, `Skybox` (a cube map texture handle), `SkyboxIntensity`. Named to avoid `System.Environment`. |
| `UnlitMaterial`, `PbrMaterial` | Material parameters (see section 6). Colors are sRGB like every Ion `Color`. |
| `MeshHandle`, `MaterialHandle`, `TextureHandle`, `RenderTargetHandle`, `MaterialShaderHandle` | Ids handed out by the renderer. |
| `Aabb`, `BoundingSphere`, `Frustum` | Bounds (Arvo's transform for world boxes), plane extraction for WebGPU clip space, box and sphere tests, frustum corners. |
| `MeshData`, `SubMesh`, `VertexAttributes`, `MeshVertex` | CPU geometry (separate attribute arrays, 32-bit indices, sub-meshes) with normal and tangent generation and validation; `MeshVertex` is the 60-byte interleaved GPU vertex every mesh uses (position, normal, tangent with sign, uv0, uv1, RGBA8 color; missing attributes get defaults). |
| `MeshPrimitives` | Cube, sphere, plane, cylinder builders. |
| `IModel`, `ModelNode`, `ModelPrimitive`, `ICubemap` | Loaded assets. |

Depth convention: standard depth (near 0, far 1, `Less`, cleared to 1) in the RHI's WebGPU clip space, `Depth32Float`.
Reverse-Z was rejected because the OpenGL ES backend remaps depth to [-1, 1] in clip space (ES 3.x has no
`glClipControl`), which cancels its precision gain; with 32-bit float depth and a sensible near plane z-fighting is not
an issue at game scales.

## 3. The frame

The renderer's system (`Rendering3DSystem`) has four steps:

| Stage | Order | What |
|---|---|---|
| Init | `StageOrder.Rendering3D` (-860) | `Renderer3D.Initialize`: shaders, layouts, samplers, default textures, shadow map, uniform ring (after the device at -900). |
| Render, scope `Begin` | -860 | `BeginFrame`: forget the previous frame's submissions. |
| Render, scope `End` | -860 | `EndFrame`: the pipeline below, then the render graph. |
| Destroy | `StageOrder.WindowClose - 120` | Release every GPU resource the renderer owns, before the 2D renderer and the device. |

The 3D scope opens after the graphics frame scope (-900) and before the sprite batch scope (-850), so it closes after
the sprite batch: when `EndFrame` runs, every 3D submission and every sprite of the frame has been recorded. The sprite
batch's submission is deferred (`SpriteBatch.DeferSubmission`) and executed by the render graph's last pass, so 2D
draws on top of 3D whatever the order of the game's steps. Submit from any Render step (order 0 by default; the ECS
extraction runs at -300 per the roadmap, which is inside both scopes).

### 3.1 Extract

`Submit`, `Draw`, `AddCamera`/`SetCamera`, `AddLight` and `SetEnvironment` copy their arguments into flat arrays that
grow by doubling and are reset (not cleared) each frame. 10,000 submissions cost about 60 us and allocate nothing.

### 3.2 Prepare

- World bounds: each object's mesh `Aabb` transformed by its world matrix (Arvo's method, no corner loop).
- Views: cameras sorted by `Priority` (then submission order), resolved against their target (the frame or a render
  target) into viewport pixels, view and projection matrices and a `Frustum`. The first camera on a target clears it;
  later ones draw a clear triangle limited to their viewport (`Clear = Color`) or the skybox (`Clear = Skybox`).
- Lights: the first directional light is the main light (and gets the shadow map when it casts shadows); further
  directional lights and the point and spot lights whose range sphere is inside the view fill up to 8 local lights per
  view.
- Shadows: `ShadowMath.Fit` covers the bounding sphere of the first frame camera's frustum slice up to
  `Rendering3DOptions.ShadowDistance` with an orthographic projection along the light, snaps it to whole shadow map
  texels (no shimmering as the camera moves) and extends the depth range back to every caster whose footprint overlaps
  it (casters outside the view still cast).
- GPU (after queueing): the per-view uniform blocks (`ViewUniforms`, 1 KiB slots) are written into a ring of
  `FramesInFlight x (MaxCameras + 1)` slots; the frame's instance data (96 bytes per drawn instance: the world matrix's
  first three columns and the normal matrix as the adjugate of the 3x3 part) is uploaded in one `WriteBuffer` into the
  frame slot's instance buffer; dirty material uniforms are written.

### 3.3 Queue and sort

Per view, for every object: layer mask test, frustum test on the world box, then a 64-bit key:

- Opaque and masked: `pipeline (8 bits) | material (16) | mesh (16) | view depth (24)`: binned by pipeline, material
  and mesh, front to back inside a bin.
- Blended: `inverted view depth (32) | pipeline (8) | material (12) | mesh (12)`: back to front.

Keys and object indices are sorted together (`MemoryExtensions.Sort`, no allocation), then runs of one mesh and one
material become instanced batches whose instances are written contiguously in sorted order. Shadow casters (opaque and
masked objects with `CastShadows`) inside the light's frustum are sorted and batched by mesh only (the depth-only
pipeline ignores materials).

`Renderer3DBenchmarks` (10,000 renderers, 3 materials, 2 meshes, 2.1 GHz Xeon VM): extract and queue with shadows
1.20 ms, without shadows 0.88 ms, two cameras 1.42 ms, 0 B per frame.

### 3.4 Render graph execution and present

The frame's graph (section 4) is built from the views, compiled, and executed on one command encoder, which is
submitted before the overlay pass submits the sprite batch. The graphics frame scope then presents.

## 4. The render graph

A small DAG of passes with declared texture reads and writes, rebuilt every frame without allocating.

```csharp
public abstract class RenderGraphPass(string name, int order)
{
    public abstract void Setup(RenderGraphBuilder builder);        // declare textures
    public abstract void Execute(in RenderGraphContext context);   // record commands
}

graph.Import(name, texture, output: true)        // an existing texture (the backbuffer, a render target, the shadow map)
graph.CreateTexture(name, descriptor)            // a transient texture, pooled
builder.Read(t) / Write(t) / ReadWrite(t) / CreateTexture(name, d) / Find(name) / HasSideEffects()
context.Encoder / context.Device / context.GetTexture(t) / context.Flush()
```

Compilation:

1. Order: by `Order`, then insertion. Built-in orders (`RenderGraphPass.Orders`): shadow 100, depth prepass 200, opaque
   300, skybox 400, transparent 500, post 600 (the first free band), overlay 900. The read-after-write,
   write-after-write and write-after-read dependencies are derived in this order, so it is a topological order of them.
2. Validation: reading a transient texture no earlier pass writes throws.
3. Culling: passes that write an imported output (the backbuffer, a camera's render target) or declare side effects
   are roots; the latest earlier writer of every texture a live pass reads is live. The shadow pass is culled when no
   pass samples the shadow map; a pass whose result is overwritten before anyone reads it is culled.
4. Lifetimes: each transient texture lives from its first to its last live pass. Pooled textures are assigned by
   descriptor and lifetime, so textures whose lifetimes do not overlap share one (`RenderGraph.Lifetimes` shows the
   assignment). Pooled textures unused for `UnusedFramesBeforeRelease` (3) frames are released.

Well-known names (`RenderGraphResources`): `Backbuffer` (the frame's color target, an output), `ShadowMap` (imported,
persistent), `Depth` (the depth buffer of the first camera that renders into the frame).

Per camera the renderer adds `DepthPrepass.N` (with `Rendering3DOptions.DepthPrepass`), `Opaque.N`, `Skybox.N` (when the
camera shows the skybox) and `Transparent.N` (when it has blended objects); then the custom passes; then `Overlay2D`.

A custom pass (a post effect) is added once and set up every frame:

```csharp
sealed class Tint(Renderer3D renderer) : RenderGraphPass("Tint", RenderGraphPass.Orders.Post)
{
    RenderGraphTexture _target;
    public override void Setup(RenderGraphBuilder b) => _target = b.ReadWrite(b.Find(RenderGraphResources.Backbuffer));
    public override void Execute(in RenderGraphContext c)
    {
        var pass = c.Encoder.BeginRenderPass(new RenderPassDescriptor(
            [new RenderPassColorAttachment(c.GetTexture(_target).DefaultView, LoadOp.Load, StoreOp.Store)]));
        // ... draw a full-screen triangle with a blending pipeline ...
        pass.End();
    }
}

renderer.AddPass(new Tint(renderer));
```

An effect that samples the scene color renders the scene into a transient texture first: create it with
`builder.CreateTexture` in a pass before the opaque passes' order, or render the cameras into a `RenderTargetHandle` and
composite it in a pass after them.

## 5. GPU layout (portable to OpenGL ES 3.x)

Two bind groups, binding numbers below 8, one sampler per texture, instance data in an instance-rate vertex buffer (no
storage buffers in the vertex stage), which is what the GLES backend and Mali-G31 require.

| Group | Binding | Content |
|---|---|---|
| 0 (view) | 0 | `View` uniform block (`ion_view.glsl`): view-projection, view, projection, inverse view-projection, shadow matrix, camera position and time, ambient and environment intensity, shadow parameters, main light direction and color, clear color, counts (local lights, environment mips, sRGB encode, solid background), 8 local lights |
| | 1, 2 | shadow map (`texture2D`) and its comparison sampler (`samplerShadow`, linear, `LessEqual`: 2x2 PCF per tap, 3x3 taps) |
| | 3, 4 | environment cube map (`textureCube`) and its trilinear sampler |
| 1 (material) | 0 | `Material` uniform block (std140) |
| | 1..6 | material textures (`texture2D`) |
| | 7 | the material's sampler |

Vertex inputs (`ion_mesh_inputs.glsl`): slot 0 the mesh's `MeshVertex` at locations 0 to 5; slot 1 the instance at
locations 6 to 11 (world columns, normal rows; the first normal row's w carries the receive-shadows flag). Batches bind
slot 1 at `FirstInstance * 96` bytes, so GLES 3.1 needs no base instance. The shadow and prepass pipelines use a smaller
vertex layout (position and world only) and the shadow pass a smaller view layout (the uniform block only), so the
shadow map is never bound while it is being rendered.

Color: targets are 8-bit UNORM (the frame, render targets), so shaders light in linear space and encode sRGB at the end
(`ionOutput`); sRGB-format targets skip the encode. Textures are uploaded as UNORM and decoded in the shader where they
hold color (base color, emissive, the skybox). Blended materials blend in the encoded space.

Cube maps follow the Vulkan/GL face convention, which is left-handed; the shaders look up `(x, y, -z)`
(`ionEnvironmentDirection`) so that a camera looking down -Z (Ion's forward) sees the +Z (front) face unmirrored, with
+X on its right and +Y above. The contract test `TheSkyboxShowsTheFrontFaceAheadAndTheRightFaceOnTheRight` pins this on
every backend.

## 6. Materials

- `UnlitMaterial`: base color times texture times vertex color; alpha modes; double sided.
- `PbrMaterial` (glTF 2.0 metallic-roughness): base color and texture, metallic and roughness factors and texture (G
  roughness, B metalness), tangent-space normal map with scale, occlusion (R) with strength, emissive color, intensity
  and texture, `AlphaMode` (`Opaque`, `Mask` with cutoff, `Blend`), `DoubleSided` (back faces lit with a flipped normal).
  Shading: Cook-Torrance with the GGX distribution, height-correlated Smith visibility and Schlick Fresnel, Lambert
  diffuse; the main directional light with the shadow map; up to 8 point, spot and extra directional lights with glTF's
  windowed inverse-square falloff; ambient from the environment cube map (diffuse from its smallest mip, specular along
  the reflection at a mip chosen by roughness) or the flat ambient color, darkened by occlusion.
- The material of a mesh renderer without one (or with a destroyed one) is `Renderer3D.DefaultMaterial`, a white
  dielectric.

### 6.1 Custom materials (the extension point)

A custom material shader is a fragment shader (and optionally a vertex shader) compiled at build time like the engine's
(`IonShader` items, GLSL 4.5 translated to SPIR-V and GLSL ES 3.10). It follows the conventions of section 5, so it
draws through the same passes, sorting, instancing and shadows as the built-in materials:

```glsl
#version 450
#include "ion_view.glsl"             // group 0: the view block, shadow map, environment, ionOutput()
#include "ion_fragment_inputs.glsl"  // mesh.vert's outputs and ionShadow()

layout(location = 0) out vec4 outColor;
layout(set = 1, binding = 0) uniform Material { vec4 uColorA; vec4 uColorB; };
layout(set = 1, binding = 1) uniform texture2D uPattern;
layout(set = 1, binding = 7) uniform sampler uMaterialSampler;

void main() { ... }
```

```csharp
var shader = renderer.CreateMaterialShader(new MaterialShaderDescriptor
{
    Name = "stripes", Assembly = typeof(Game).Assembly, FragmentShader = "stripes.frag",
    UniformSize = 32, TextureCount = 1,
});
var material = renderer.CreateMaterial(shader, MemoryMarshal.AsBytes(colors.AsSpan()), [patternTexture], AlphaMode.Opaque);
```

The include files live in `Ion/Ion.Extensions.Rendering3D/Shaders` (the test project includes them by relative path;
packaging them as build content is left for the packaging work). A WGSL backend, if the browser target is picked up,
would take WGSL modules with the same group and binding numbers; the RHI already describes bind groups in WebGPU terms,
so the material conventions do not change.

## 7. Assets

- `Load<IModel>("path.gltf")` or `.glb`: an own glTF 2.0 reader over `System.Text.Json` (reflection-free, NativeAOT
  clean; SharpGLTF was not needed for the subset). Supported: triangle-list primitives (other modes are skipped),
  POSITION, NORMAL, TANGENT, TEXCOORD_0/1, COLOR_0 (any component type, normalized or not), 8/16/32-bit indices,
  external, data-URI and GLB buffers and images, metallic-roughness materials, `KHR_materials_unlit`,
  `KHR_materials_emissive_strength`, node TRS or matrices, the default scene. Not supported (a clear
  `InvalidDataException`): sparse accessors, required extensions other than the two above (Draco, meshopt, texture
  transforms), glTF 1.0. Ignored: skins, animations, morph targets, cameras, lights, samplers (one trilinear repeat
  sampler per material). Every primitive becomes a mesh (with generated normals and tangents when missing), images are
  decoded with ImageSharp like the 2D loader (straight alpha, CPU mip chain) and created through the 2D renderer's
  `TextureFactory`, so the sprite batch can draw them too. `IModel` exposes the node tree (`ModelNode`: name, parent,
  children, local `Transform`, `ModelMatrix`, primitives), the handles and the model-space bounds; `batch.Draw(model,
  world)` submits every primitive.
- `Load<ICubemap>("Folder")`: six images named `px nx py ny pz nz` (or `right left top bottom front back`), square and of
  one size, with a CPU mip chain.
- `MeshPrimitives`: cube, UV sphere, plane (XZ, facing +Y), cylinder with caps; counter-clockwise front faces, normals,
  tangents and UVs.

## 8. What the ECS extraction wave must do

The ECS module (`Ion.Extensions.Ecs.Rendering`, section 4.9 of the roadmap) needs no renderer changes. Its extraction
systems call `IRenderer3D` (or `IMeshBatch`) from Render steps at the roadmap's order (-300), which is inside the 3D
renderer's scope:

1. **Components.** Use the abstraction types as components: `Transform` (local), `MeshRenderer`, `Camera`,
   `DirectionalLight`, `PointLight`, `SpotLight`. `SceneEnvironment` is a per-world resource (singleton), passed with
   `SetEnvironment` when it changes. If the ECS module defines its own `Transform`, it should be layout-identical or
   convert explicitly; two public types named `Transform` in `Ion.Extensions.Graphics` and `Ion.Extensions.Ecs` would
   make games that import both namespaces ambiguous, so reusing `Ion.Extensions.Graphics.Transform` is recommended.
2. **World matrices.** `GlobalTransform` must hold `Matrix4x4` world matrices in Ion's row-vector convention
   (`world = local.ToMatrix() * parent.world`), propagated before Render (the roadmap's Last(-200) of the previous
   frame, or PostUpdate). The renderer computes world bounds itself from the mesh bounds and the matrix; an ECS `Aabb`
   component is only needed for gameplay queries.
3. **Extraction.** For each entity with `MeshRenderer` and `GlobalTransform`: `renderer.Submit(meshRenderer,
   globalTransform.Matrix)`. Cameras: `AddCamera(camera, world)` (not `SetCamera`, which replaces the frame's cameras).
   Lights: `AddLight(directional, world)`, `AddLight(point, world.Translation)`, `AddLight(spot, world)`. The calls take
   `in` parameters and copy; a chunk-span query loop over 10,000 entities costs about the 60 us measured for
   `Extract_Submit10k` plus the query itself.
4. **Visibility.** `Visibility`/hidden markers are the extraction's filter (do not submit). `LayerMask` and
   `Camera.CullingMask` cover per-camera visibility. The renderer's per-view culling results are internal; if gameplay
   needs `ViewVisibility`, expose it from `Renderer3D` then (not needed by rendering).
5. **Models.** `IModel.Nodes` maps to entities: one entity per node with `Transform` = `LocalTransform` and a parent link,
   and one `MeshRenderer` per `ModelPrimitive` (on the node's entity, or on child entities when a node has several
   primitives, since an entity holds one `MeshRenderer`).
6. **Frame order.** Nothing persists between frames: extraction must submit every visible entity every frame. The 2D
   extraction (sprites) is unaffected: the sprite batch keeps its API and is composited by the 3D graph when the 3D
   renderer is registered.
7. **Tests.** The CPU-only renderer (`new Renderer3D(null)`, or `AddRendering3D` on the headless null backend) runs the
   whole CPU pipeline; `LastFrameStatistics` (submitted, visible, culled, batches, draw calls, triangles, shadow casters)
   is the assertion surface for extraction tests without a GPU.

## 9. Limits and next steps

- One shadow-casting directional light, one cascade; no point or spot shadows; no shadow for blended objects; masked
  objects cast opaque shadows (the depth-only pipeline has no fragment stage).
- No MSAA, HDR targets, tone mapping or post stack (the graph supports adding them as passes); no GPU culling, no LOD.
- Image based lighting uses box-filtered mips, not prefiltered GGX lobes or an irradiance map.
- Materials use one sampler (glTF sampler objects are ignored); the unlit material's texture from the 2D loader is
  premultiplied, which is exact for opaque textures only.
- Custom materials cannot change the shadow or prepass vertex stage (a custom vertex shader that moves vertices casts its
  undeformed shadow).
- Skinning, morph targets, animation and glTF cameras and lights are not imported.
