---
title: Rendering overview
description: How Ion draws a frame, from the WebGPU-shaped RHI and its Vulkan and OpenGL ES backends to the 2D sprite batch and the 3D renderer.
sidebar:
  order: 1
---

Ion's rendering is built in three layers. At the bottom a small **render hardware interface (RHI)** hides the graphics
API. On top of it sit two renderers written once against the RHI: the **2D renderer** (the sprite batch, text and
textures) and the **3D renderer** (meshes, PBR materials, lights, shadows, glTF models). Your game talks to the
renderers through interfaces (`ISpriteBatch`, `IRenderer3D`) and almost never touches the RHI.

This page explains how those pieces fit together and what happens during a frame. The rest of the section covers each
piece in depth.

## The layers

```
 Your game (systems with [Render] steps)       ECS extraction (sprites, meshes, cameras, lights)
                 |                                              |
                 v                                              v
   ISpriteBatch (Ion.Extensions.Rendering2D)       IRenderer3D / IMeshBatch (Ion.Extensions.Rendering3D)
                 |                                              |
                 +---------------------+------------------------+
                                       |
                                       v
             IGraphicsFrame + RHI (Ion.Extensions.Graphics.Abstractions, namespace Ion.Extensions.Graphics.Rhi)
                                       |
             +-------------------------+--------------------------+
             v                         v                          v
   Vulkan backend             OpenGL ES backend           Null backend (no GPU)
   (Graphics.Vulkan)          (Graphics.GLES)             (Graphics.Null)
             |                         |
             +------------+------------+
                          v
            Silk.NET window (Windowing.SilkNet), or an offscreen target (Graphics.Headless)
```

| Layer | Package | What it gives you |
|---|---|---|
| Abstractions | `Ion.Extensions.Graphics.Abstractions` | `IWindow`, `ISpriteBatch`, `ITexture2D`, `IFontSet`, `Color`, `RectangleF`, the 3D data types (`Transform`, `Camera`, `MeshRenderer`, lights, materials), and the RHI interfaces |
| 2D renderer | `Ion.Extensions.Rendering2D` | `SpriteBatch` (as `ISpriteBatch`), texture and font loaders, the glyph atlas, `TextureFactory`, `RenderTarget2D` |
| 3D renderer | `Ion.Extensions.Rendering3D` | `Renderer3D` (as `IRenderer3D` and `IMeshBatch`), the render graph, glTF and cube map loaders |
| RHI backends | `Ion.Extensions.Graphics.Vulkan`, `Ion.Extensions.Graphics.GLES` | a device, a swapchain or GL context, frames in flight |
| Backend selection and offscreen | `Ion.Extensions.Graphics.Headless` | `RhiGraphics` (picks Vulkan or OpenGL ES at startup), headless rendering into an offscreen target |
| No GPU | `Ion.Extensions.Graphics.Null` | `NullWindow`, `NullSpriteBatch` (records draws), size-only textures and fonts |
| Window | `Ion.Extensions.Windowing.SilkNet` | a GLFW or SDL window, keyboard, mouse, gamepad and touch input |

The `Ion` package references all of them, and `AddIon()` registers the right combination for you.

## The RHI: shaped like WebGPU

The RHI is a thin, explicit abstraction modelled on WebGPU, which is the common subset of Vulkan, Metal and Direct3D 12
and maps cleanly onto OpenGL ES 3.1. If you know WebGPU, the names are familiar:

| RHI type | Role |
|---|---|
| `IGraphicsDevice` | Creates every GPU resource; owns the `Queue` and (when windowed) the `Surface` |
| `IQueue` | `Submit` command buffers, `WriteBuffer`, `WriteTexture` |
| `IBuffer`, `ITexture`, `ITextureView`, `ISampler` | Resources |
| `IShaderModule` | A compiled shader stage (SPIR-V on Vulkan, GLSL ES on OpenGL ES) |
| `IBindGroupLayout`, `IBindGroup`, `IPipelineLayout` | Resource binding, by group and binding number |
| `IRenderPipeline` | Shaders, vertex layout, primitive, depth and blend state |
| `ICommandEncoder`, `IRenderPassEncoder`, `ICommandBuffer` | Recording and submitting work |
| `ISurface` | The window's presentable surface (a Vulkan swapchain, the GLES default framebuffer) |

Two things differ from WebGPU on purpose:

- **Frames in flight are explicit.** The graphics system calls `IGraphicsDevice.BeginFrame` at the start of the Render
  stage and `EndFrame` at its end. `BeginFrame` waits until the GPU has finished the frame that last used the same slot,
  then recycles that slot's command buffers, staging memory and deferred deletions. Code inside the Render stage never
  calls them.
- **Disposal is deferred.** Every RHI object is `IDisposable`, and disposing one the GPU may still be reading is safe:
  the backend destroys it once the frames in flight have completed.

Clip space is WebGPU's: y points up and depth runs from 0 (near) to 1 (far). Texture row 0 is the top row on every
backend (the OpenGL ES backend negates y at build time and flips once at present), so the same shaders produce the same
pixels everywhere. The repository's golden-image tests compare Vulkan and OpenGL ES output against one image.

:::note
Everything created from a device is used from the main thread (the game loop's thread). There is no multithreaded
command recording.
:::

Most games never touch the RHI. When you do need it (a custom renderer, a post effect, a debug view), take
`IGraphicsFrame` in a system. See [Shaders](/Ion/rendering/shaders/) for a complete example.

## Registering rendering

For a 2D game, `AddIon()` and `UseIon()` register and add everything: the window, the RHI backend, the sprite batch,
and the texture and font loaders.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Graphics;

var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x1B, 0x26, 0x3B))
	.AddSystem<GameSystem>();

using var game = builder.Build();
game.UseIon().UseSystem<GameSystem>();
game.Run();
```

For 3D, add the 3D renderer. `AddRendering3D()` registers the engine core too, so you can list the modules in any order:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddRendering3D().AddSystem<SceneSystem3D>();

using var game = builder.Build();
game.UseRendering3D().UseSystem<SceneSystem3D>();
game.Run();
```

With the ECS module, `AddEcsRendering()` (sprites) and `AddEcsRendering3D()` (meshes, cameras and lights) pull in the
renderers they need. See [ECS rendering](/Ion/ecs/ecs-rendering/).

:::tip
Each `AddX()` registers what it depends on and is idempotent. Calling `AddIon()` again, directly or through another
module, only applies its options. The graphics output (windowed or headless) is decided by the **first** `AddIon` call.
:::

## What happens in a frame

Rendering happens in the **Render** stage. Engine systems open *scopes* around it (a `Begin` step before the stage's
steps and an `End` step after them), and your `[Render]` steps run inside all of them. The orders come from
[`StageOrder`](/Ion/reference/stage-order/):

| Order | Scope or step | What happens |
|---|---|---|
| `StageOrder.Graphics` (-900) | Begin | The RHI frame starts: wait for the frame slot, acquire the swapchain image (reconfiguring after a resize), create the depth target |
| `StageOrder.Rendering3D` (-860) | Begin | The 3D renderer forgets last frame's submissions (only when the 3D renderer is registered) |
| `StageOrder.SpriteBatch` (-850) | Begin | The sprite batch opens its outermost segment with default options |
| `StageOrder.TransformPropagation` (-400) | Step | ECS only: global transforms are recomputed so this frame draws what Update did |
| `StageOrder.Extract` (-300) | Step | ECS only: sprites, meshes, cameras and lights are copied into the renderers |
| `StageOrder.Default` (0) | Step | **Your `[Render]` steps**: `spriteBatch.Draw(...)`, `renderer.Submit(...)` |
| `StageOrder.PhysicsDebugDraw` (650) | Step | Physics debug outlines, if enabled |
| `StageOrder.Ui` (700) | Step | The UI module draws its widgets |
| `StageOrder.MetricsOverlay` (800) | Step | The metrics overlay |
| `StageOrder.WindowClose` (900) | Step | A closed window becomes an `ExitGameEvent` |
| `StageOrder.SpriteBatch` (-850) | End | The outermost segment closes: sprites are sorted, uploaded and drawn (or deferred to the 3D overlay pass) |
| `StageOrder.Rendering3D` (-860) | End | Cull, sort, batch, then run the render graph: shadow, opaque, skybox, transparent, custom passes, then the 2D overlay |
| `StageOrder.Graphics` (-900) | End | Clear the target if nothing drew, present, end the RHI frame |

Scopes nest like brackets: the one that opens first closes last. That is why the 3D scope (-860) opens before the
sprite batch scope (-850) and closes after it. When the 3D renderer ends its frame, every 3D submission and every sprite
of the frame has been recorded.

### Extract, then draw

Both renderers are **immediate mode**. Nothing you submit persists to the next frame (apart from resources such as
textures, meshes and materials, and the 3D environment). Every frame you, or the ECS extraction systems, submit what
should be drawn, and the renderer does the rest when its scope closes:

1. **Record.** `Draw`, `DrawString`, `Submit` and `AddLight` copy their arguments into flat arrays. They allocate
   nothing once the arrays have grown to the scene.
2. **Prepare and sort.** The sprite batch sorts each segment by its `SortMode`. The 3D renderer computes world bounds,
   culls against each camera's frustum, and sorts opaque objects front to back and blended objects back to front.
3. **Batch.** Runs of sprites with the same texture become one instanced draw call. Runs of 3D objects with the same
   mesh and material become one instanced draw call.
4. **Upload and draw.** Instance data goes into a per-frame ring buffer with one `WriteBuffer`, and the draw calls are
   recorded and submitted.

The ECS extraction is the same thing done for you: at `StageOrder.Extract` it walks the world and calls the same
`ISpriteBatch` and `IMeshBatch` APIs your own code uses, so there is exactly one renderer either way.

## How 2D and 3D coexist

You can use the sprite batch and the 3D renderer in the same game, in the same frame, from any Render step:

- **Without the 3D renderer**, the sprite batch submits its own command buffer when its scope closes, drawing straight
  into the frame.
- **With the 3D renderer**, the sprite batch's submission is deferred (`SpriteBatch.DeferSubmission`) and the 3D render
  graph draws it as its last pass (`Overlay2D`, order `RenderGraphPass.Orders.Overlay`). 2D therefore always lands on
  top of 3D, whatever the order of your steps. This is how HUDs, the UI module and the metrics overlay appear over a
  3D scene.

```csharp
[Render]
public void Draw(GameTime dt)
{
	// 3D: drawn by the render graph when the Render stage closes.
	renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 3, 8), Vector3.Zero));
	renderer.Draw(_cube, _material, Matrix4x4.Identity);

	// 2D: drawn after every 3D pass, on top.
	sprites.DrawString(_font, "Score: 42", new Vector2(16, 16), Color.White);
}
```

Both renderers can also render into textures that the other can sample: a `RenderTarget2D` drawn by the sprite batch
can be registered with `IRenderer3D.CreateTexture`, and a 3D camera's render target (`CreateRenderTarget`) can be used
as a material texture.

## Headless and testing

Rendering has three modes, chosen from configuration:

| Mode | How | What renders |
|---|---|---|
| Windowed | default | The selected RHI backend into a Silk.NET window |
| Headless | `Ion:Headless=true` (or `--headless`) | Nothing. `NullSpriteBatch` records draw calls; the 3D renderer runs its CPU pipeline only, so its statistics are real |
| Headless rendering | `Ion:Headless=true` and `Ion:Headless:Render=true` (or `--headless-render`) | The selected RHI backend into an offscreen target; `IScreenshotSource` captures frames |

A game that depends only on the interfaces (`IWindow`, `ISpriteBatch`, `ITexture2D`, `IFontSet`, `IRenderer3D`) runs
unchanged in all three. See [Graphics backends](/Ion/rendering/graphics-backends/) and
[Testing](/Ion/tooling/testing/).

## See also

- [Graphics backends](/Ion/rendering/graphics-backends/): Vulkan, OpenGL ES, headless, backend selection
- [Windowing](/Ion/rendering/windowing/): the Silk.NET window and its configuration
- [Sprites](/Ion/rendering/sprites/) and [Text](/Ion/rendering/text/): the 2D renderer
- [3D rendering overview](/Ion/rendering/3d/overview/): the 3D renderer and its pipeline
- [Stages](/Ion/concepts/stages/) and [Stage order reference](/Ion/reference/stage-order/)
- Design notes: [docs/design/ion-rendering3d.md](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-rendering3d.md)
