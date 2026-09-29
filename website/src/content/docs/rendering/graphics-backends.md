---
title: Graphics backends
description: Choose between the Vulkan, OpenGL ES and headless graphics backends, and configure them with Ion:Graphics.
sidebar:
  order: 2
---

Ion renders through one of two GPU backends, **Vulkan** and **OpenGL ES**, both implementing the same
[RHI](/Ion/rendering/overview/#the-rhi-shaped-like-webgpu). A third, **null** backend draws nothing and needs no GPU,
for servers, CI and unit tests. The renderers above the RHI are identical on every backend, and the repository's
golden-image tests check that Vulkan and OpenGL ES produce the same pixels.

You normally never pick a backend in code: `AddIon()` registers the backend-selecting graphics service, which chooses at
startup from `Ion:Graphics:PreferredBackend`.

## The backends

| Backend | Package | Targets | Windowed | Headless rendering |
|---|---|---|---|---|
| Vulkan | `Ion.Extensions.Graphics.Vulkan` | Windows, Linux, macOS and iOS (through MoltenVK), Android | A swapchain on the Silk.NET window | Any Vulkan driver, for example Mesa lavapipe on Linux CI |
| OpenGL ES 3.1 (3.0 and 3.2 paths) | `Ion.Extensions.Graphics.GLES` | Linux arm64 handhelds such as the R36S, and anywhere Vulkan is missing | The window's GL ES context | EGL surfaceless or pbuffer, for example Mesa llvmpipe |
| Null | `Ion.Extensions.Graphics.Null` | Anywhere | No window, nothing drawn | Not applicable |

`Direct3D12`, `Metal` and `WebGPU` exist in the `GraphicsBackend` enum but are **reserved**: there is no implementation,
and choosing one throws `NotSupportedException` when the engine is registered. Apple platforms use Vulkan over MoltenVK.
A WebGPU backend for a browser build is planned but not built.

## Choosing a backend

Set `Ion:Graphics:PreferredBackend` in `appsettings.json` or on the command line:

```json title="appsettings.json"
{
	"Ion": {
		"Graphics": {
			"PreferredBackend": "Auto"
		}
	}
}
```

```bash
dotnet run -- --Ion:Graphics:PreferredBackend=OpenGLES
```

| Value | Behavior |
|---|---|
| `Auto` (default) | The first **available** backend in platform order |
| `Vulkan` | Vulkan, forced (it is used even if it then fails to start) |
| `OpenGLES` | OpenGL ES, forced |
| `Direct3D12`, `Metal`, `WebGPU` | Throws `NotSupportedException` |

The platform order for `Auto` comes from `GraphicsBackendSelector.AutoOrder()`:

| Platform | Order |
|---|---|
| Linux arm64 (not Android) | OpenGL ES, then Vulkan |
| Everything else (Windows, macOS, Linux x64, Android, iOS) | Vulkan, then OpenGL ES |

Linux arm64 prefers OpenGL ES because the handhelds Ion targets there (the R36S: RK3326, Mali-G31 with Panfrost) have a
stable GLES driver and only an experimental Vulkan one.

"Available" is decided by a probe that runs once, the first time `Auto` is resolved:

- **Vulkan** is available when a Vulkan 1.1 instance can be created and it reports at least one physical device.
- **OpenGL ES**, windowed, is assumed available (the window creates the context). Offscreen, it is available when a
  headless EGL context can be created.

When no probe succeeds, the first registered candidate is used anyway, so its own error message explains what is missing.
The resolved backend is written back into `GraphicsConfig.PreferredBackend`, so the window is created with the matching
API (no client API for Vulkan, an OpenGL ES context for OpenGL ES).

The choice is logged at startup:

```
Graphics backend: Vulkan (configured Auto; Auto order Vulkan, OpenGLES), windowed.
Vulkan device: NVIDIA GeForce RTX 3070 (API 1.3), 2 frames in flight, validation off, windowed.
```

:::caution
`Auto` does not fall back at run time. If Vulkan's probe succeeds but the device later fails to start (for example
because the window cannot create a Vulkan surface), the error is thrown. Force `OpenGLES` on such machines.
:::

## Graphics options

`GraphicsConfig` is bound from `Ion:Graphics`. You can also set it in code with the `AddIon` callback, which runs after
binding:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics =>
{
	graphics.ClearColor = new Color(0x20, 0x20, 0x20);
	graphics.VSync = true;
	graphics.FramesInFlight = 3;
});
```

| Key (`Ion:Graphics:...`) | Type | Default | Meaning |
|---|---|---|---|
| `PreferredBackend` | `GraphicsBackend` | `Auto` | `Auto`, `Vulkan` or `OpenGLES` (see above) |
| `VSync` | `bool` | `false` | Present with FIFO (wait for vertical blank). Off: mailbox where supported, else immediate. Also sets the window's swap interval |
| `FramesInFlight` | `int` | `2` | Frames the CPU may record ahead of the GPU (2 or 3; other values are clamped) |
| `Validation` | `bool?` | `null` | Enable API validation (Vulkan: `VK_LAYER_KHRONOS_validation` and a debug messenger, when installed; GLES: GL error checks). `null` is on in Debug builds of the backend, off in Release |
| `DepthBuffer` | `bool` | `true` | Create a `Depth32Float` depth target with the color target (`IGraphicsFrame.DepthTarget`) |
| `RetainLastFrame` | `bool` | `false` | Windowed: copy every presented frame to host memory so `IScreenshotSource.Capture` works. Costs a full-frame copy per frame. Headless rendering always supports capture |
| `Adapter` | `string?` | `null` | Pick the GPU whose name contains this text (case-insensitive). `null`: prefer a discrete GPU, then integrated, then anything |
| `ClearColor` | `Color` | `Color.Black` | The color the frame is cleared to. Set in code |
| `ClearColorHex` | `string?` | | `ClearColor` as hex for configuration: `RGB`, `RGBA`, `RRGGBB` or `RRGGBBAA`, with or without `#` |
| `Output` | `GraphicsOutput` | `Window` | `Window`, or `None` to select the headless backends (same as `Ion:Headless=true`) |

One more key lives next to these but on its own options class, `GlesConfig` (in `Ion.Extensions.Graphics.GLES`), bound
from `Ion:Graphics:Gles`. It is not a `GraphicsConfig` property, so it cannot be set from the `AddIon` callback:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Ion:Graphics:Gles:MaxFeatureLevel` | `GlesFeatureLevel` | `Es32` | OpenGL ES only: cap the feature level (`Es30`, `Es31`, `Es32`) to test the fallback paths |

There is no frame rate setting under `Ion:Graphics`: frame pacing is controlled by `Ion:MaxFPS` (0 means uncapped) and
`Ion:VSync` on `GameConfig` (see [The game loop](/Ion/concepts/game-loop/)).

:::caution[Two VSync settings]
`Ion:Graphics:VSync` sets the **present mode** (FIFO vs mailbox). `Ion:VSync` (on `GameConfig`) tells the **game loop**
to stop its own pacing and let presentation block. For a vsynced game set `Ion:Graphics:VSync=true` and either
`Ion:VSync=true` or `Ion:MaxFPS=0`, as the samples do:

```json
{ "Ion": { "MaxFPS": 0, "Graphics": { "VSync": true } } }
```

With only `Ion:Graphics:VSync`, the loop still sleeps to honour `Ion:MaxFPS` (300 by default).
:::

A full frame configuration for a desktop game:

```json title="appsettings.json"
{
	"Ion": {
		"Title": "My Game",
		"MaxFPS": 0,
		"Graphics": {
			"PreferredBackend": "Auto",
			"VSync": true,
			"FramesInFlight": 2,
			"ClearColorHex": "#1B263B",
			"Validation": false
		},
		"Window": { "Width": 1280, "Height": 720 }
	}
}
```

## Vulkan

The Vulkan backend (`Silk.NET.Vulkan`) is the desktop and mobile reference:

- A swapchain on the window, recreated when the framebuffer is resized or reported out of date.
- 2 or 3 frames in flight, staging uploads per frame, SPIR-V shaders.
- Validation when `Validation` is on and the Khronos layer is installed. If you ask for validation and the layer is
  missing, a warning is logged and the device starts without it.
- **macOS and iOS** run Vulkan over **MoltenVK**. The device enables `VK_KHR_portability_enumeration` and
  `VK_KHR_portability_subset` automatically when they are reported. Ship `libMoltenVK.dylib` with the game, for example
  through the `Silk.NET.MoltenVK.Native` package.

On Linux CI without a GPU, install Mesa's software Vulkan driver (lavapipe, package `mesa-vulkan-drivers`) to render
headless.

## OpenGL ES

The OpenGL ES backend (`Silk.NET.OpenGLES`) maps the WebGPU-shaped RHI onto OpenGL ES 3.x:

- Command buffers are recorded and replayed at submit, with the same queue ordering as Vulkan; frames in flight wait on
  fence syncs.
- Bind groups become uniform block binding points and texture units at `group * 8 + binding`, the same numbers the
  [shader build](/Ion/rendering/shaders/) writes into the GLSL ES.
- The window surface renders into an offscreen framebuffer and is blitted to the default framebuffer with a vertical
  flip at present, so texture row 0 is at the top as on Vulkan. The window is therefore created without a depth or
  stencil buffer.
- Windowed, it uses the Silk.NET window's GL ES context. If no ES 3.1 context can be created, the window retries with
  ES 3.0.
- Headless, it creates an EGL context (Mesa's surfaceless platform, or a pbuffer). On Linux CI, Mesa llvmpipe
  (`libegl-mesa0`) is enough, and no X server is needed.

The feature levels:

| `GlesFeatureLevel` | What changes |
|---|---|
| `Es30` | GLSL ES 3.00 (the 3.10 shaders are rewritten at load), slots assigned by name, no storage buffers, base vertex emulated |
| `Es31` | Binding qualifiers in shaders, separate vertex formats and bindings, storage buffers. Base vertex emulated |
| `Es32` | Adds native base vertex draws |

To check on a desktop what an ES 3.0-only device will run:

```bash
dotnet run -- --Ion:Graphics:PreferredBackend=OpenGLES --Ion:Graphics:Gles:MaxFeatureLevel=Es30
```

### Portability rules

Because OpenGL ES 3.x is the lowest common denominator, portable RHI code follows a few rules (they are documented on
the RHI types, and the built-in renderers follow them):

- Keep binding numbers below 8, use at most 4 bind groups (3 on ES 3.0 devices with 24 uniform buffer bindings) and 16
  textures per stage.
- Sample each texture with one sampler (SPIRV-Cross builds a combined sampler per texture).
- Do not rely on storage buffers in the vertex stage (`DeviceLimits.VertexStorageBuffers` is false on some GLES 3.1
  devices). Put per-instance data in a vertex buffer with `VertexStepMode.Instance`.
- One blend state and write mask apply to every color target.

`IGraphicsDevice.Limits` reports `MaxTextureDimension2D`, `MinUniformBufferOffsetAlignment`,
`MaxUniformBufferBindingSize`, `MaxBindGroups` and `VertexStorageBuffers` for the running device.

## Headless: null and offscreen

Two headless setups share one switch, `Ion:Headless=true` (or `--headless` on the command line):

```bash
dotnet run -- --headless            # null graphics: nothing is drawn, no GPU needed
dotnet run -- --headless-render     # an RHI backend renders into an offscreen target
```

`--headless-render` is shorthand for `--Ion:Headless=true --Ion:Headless:Render=true`.

**Null graphics** (`AddNullGraphics`) registers `NullWindow`, `NullInputState` and `NullSpriteBatch`, plus texture and
font loaders that read image sizes from file headers and measure text with a fixed glyph width. The 3D renderer runs its
CPU pipeline (culling, sorting, batching) without a device, so `IRenderer3D.LastFrameStatistics` is meaningful. Resolve
the concrete types in tests:

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
host.Step(10);
var stats = host.SpriteBatch.LastFrame;   // NullSpriteBatch: DrawCalls, Sprites, Strings, Rects, Commands
Assert.True(stats.Sprites > 0);
```

**Headless rendering** (`AddHeadlessRendering`) keeps the null window and input, and adds the backend-selecting RHI
device without a surface, rendering every frame into an offscreen `Rgba8Unorm` target sized from `Ion:Window:Width` and
`Height` (960x540 by default), plus the real 2D renderer. `IScreenshotSource` returns the last frame:

```csharp
using Ion;
using Ion.Extensions.Graphics;

public sealed class CaptureSystem(IScreenshotSource screenshots, IEvents events)
{
	private long _frames;

	[Last]
	public void Last(GameTime dt)
	{
		if (++_frames < 60) return;
		screenshots.SaveScreenshot("out/frame.png");   // PNG; Capture() returns RGBA8 pixels
		events.Emit<ExitGameEvent>();
	}
}
```

`Screenshot` exposes `Width`, `Height`, the `Rgba` bytes (rows top to bottom) and `GetPixel(x, y)`. Windowed games can
capture too, with `Ion:Graphics:RetainLastFrame=true`. See [Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/)
for comparing frames in tests.

## Registering a backend by hand

`AddIon()` covers almost every game. For a minimal app (no audio, assets or scenes), or to force one backend in code,
register the pieces yourself. The Quad sample does this:

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Windowing;

var builder = IonApplication.CreateBuilder(args);
builder.Services.AddSilkWindowing(builder.Configuration);   // window and input
builder.Services.AddRhiGraphics(builder.Configuration);     // Vulkan or OpenGL ES, from PreferredBackend
builder.AddSystem<QuadSystem>();

using var app = builder.Build();
app.UseEvents();
app.UseSilkWindowing();
app.UseRhiGraphics();
app.UseSystem<QuadSystem>();
app.Run();
```

| Registration | Adds | Use it for |
|---|---|---|
| `AddGraphics(config)` / `UseGraphics()` | Window, `AddRhiGraphics`, the 2D renderer | The windowed stack without the rest of the engine |
| `AddRhiGraphics(config)` / `UseRhiGraphics()` | The backend-selecting RHI device (windowed) | Custom renderers |
| `AddRhiGraphics(config, offscreen: true)` | The same, offscreen | Custom headless renderers |
| `AddVulkanGraphics(config)` / `UseVulkanGraphics()` | Vulkan only | Forcing Vulkan in code |
| `AddGlesGraphics(config)` / `UseGlesGraphics()` | OpenGL ES only (sets `PreferredBackend` to `OpenGLES` so the window gets a GL ES context) | Forcing OpenGL ES in code |
| `AddNullGraphics(config)` / `UseNullGraphics()` | Null window, input and sprite batch | Tests and servers |
| `AddHeadlessGraphics(config)` / `UseHeadlessGraphics()` | Null window and input plus headless rendering | Offscreen rendering without `AddIon` |

Call the `Use` methods in order: the window before the graphics device (the device needs the native window at Init),
and the device before anything that creates GPU resources.

:::note
The device is created by the graphics Init step (`StageOrder.Graphics`). Create your GPU resources in an `[Init]` step
with the default order (0) or later. Accessing `IGraphicsFrame.Device` earlier throws `InvalidOperationException`.
:::

## Common problems

| Message or symptom | Cause and fix |
|---|---|
| `No Vulkan device found. Install a Vulkan driver (on Linux CI: Mesa lavapipe, package mesa-vulkan-drivers).` | Vulkan was forced (or its probe passed) on a machine without a usable driver. Install one, or set `Ion:Graphics:PreferredBackend=OpenGLES` |
| `The window has no GL context: create it with the OpenGL ES API (Ion:Graphics:PreferredBackend = OpenGLES, or AddRhiGraphics, which sets it).` | `AddGlesGraphics` or the OpenGL ES device was used with a window created for Vulkan. Use `AddRhiGraphics`, or set `PreferredBackend` to `OpenGLES` before the window is created |
| `Graphics backend Direct3D12 is reserved and has no implementation; use Auto, Vulkan or OpenGLES.` (`NotSupportedException`) | A reserved `GraphicsBackend` value in configuration or the `AddIon` callback |
| `AddIon was called again with options that select the headless backends ...` (`InvalidOperationException`) | Two `AddIon` calls disagree about the output. Choose windowed or headless in the first call, or with `Ion:Headless` / `Ion:Graphics:Output` |
| `Capturing a windowed frame needs Ion:Graphics:RetainLastFrame = true` | `IScreenshotSource.Capture` in a windowed run without the copy enabled |
| `No frame is being rendered (IsRendering is false).` | `IGraphicsFrame.ColorAttachment()` outside the Render stage, or while the window is minimized. Check `IsRendering` first |
| Nothing renders after `Auto` picked Vulkan on a machine whose driver cannot present | The probe only checks that a device exists. Force `OpenGLES` |

## See also

- [Rendering overview](/Ion/rendering/overview/)
- [Windowing](/Ion/rendering/windowing/): the window the backends present to
- [Shaders](/Ion/rendering/shaders/): SPIR-V and GLSL ES from one source
- [R36S](/Ion/platforms/r36s/), [Desktop](/Ion/platforms/desktop/) and [Mobile](/Ion/platforms/mobile/): platform notes
- [Configuration reference](/Ion/reference/configuration/)
