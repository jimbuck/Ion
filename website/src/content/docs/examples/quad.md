---
title: Quad
description: The smallest app on the Silk.NET stack, a spinning textured quad drawn directly through the RHI with GLSL shaders compiled at build time, on Vulkan or OpenGL ES, windowed or offscreen.
sidebar:
  order: 10
---

**Source:** [`Ion.Examples/Ion.Examples.Quad`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Quad).
Its `TexturedQuad` class is also used by the Vulkan and OpenGL ES backend test suites.

A checkerboard quad spinning in the middle of the window. There is no sprite batch, no `AddIon` and no ECS: just a
Silk.NET window, the render hardware interface (RHI) with whichever backend is selected, and one system that creates
GPU resources and records a render pass every frame. Read it to learn the RHI, or as the starting point of a custom
renderer.

## What it shows

- Registering only what the app needs: `AddInputTracker`, `AddSilkWindowing`, `AddRhiGraphics` (or offscreen), and the
  matching `UseEvents`, `UseSilkWindowing`, `UseRhiGraphics`.
- `IGraphicsFrame`: the frame's device, color and depth formats and attachments.
- The RHI: shader modules, a bind group layout, a pipeline, vertex, index and uniform buffers, a texture, a sampler, a
  bind group, a command encoder and a render pass.
- Shaders written once in GLSL 4.5 and compiled at build time to SPIR-V (Vulkan) and GLSL ES 3.10 (OpenGL ES), loaded
  with `EmbeddedShaders`.
- Backend selection: Vulkan, OpenGL ES, or `Auto`.
- Releasing GPU resources in a `[Destroy]` step, before the device goes.
- Screenshots through `IScreenshotSource`.

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Quad
dotnet run --project Ion.Examples/Ion.Examples.Quad -- --Ion:Graphics:PreferredBackend=OpenGLES
dotnet run --project Ion.Examples/Ion.Examples.Quad -- --Ion:Headless=true --Quad:Frames=60 --Quad:Screenshot=quad.png
```

| Flag | Effect |
|---|---|
| `--Ion:Headless=true` | Render offscreen, no window. (Here headless means offscreen rendering: there is no null backend in this sample.) |
| `--Ion:Graphics:PreferredBackend=<b>` | `Vulkan`, `OpenGLES`, or `Auto` (Vulkan then OpenGL ES; OpenGL ES first on linux-arm64). |
| `--Quad:Frames=<n>` | Exit after n frames. |
| `--Quad:Screenshot=<file>` | Save the last frame as PNG on exit (headless, or windowed with `--Ion:Graphics:RetainLastFrame=true`). |
| `--Quad:Spin=false` | Keep the quad still, for deterministic screenshots. |
| `--Ion:Window:Platform=Sdl` | Use SDL instead of GLFW (as on the R36S). |
| `--Ion:Graphics:Gles:MaxFeatureLevel=Es30` | Force the OpenGL ES 3.0 paths. |

## Program.cs

```csharp title="Program.cs"
// The window (unless headless) and the backend-selecting RHI graphics, without the rest of the engine (no AddIon).
var builder = IonApplication.CreateBuilder(args);
var headless = bool.TryParse(builder.Configuration["Ion:Headless"], out var configured) && configured;
if (headless)
{
	builder.Services.AddInputTracker();
	builder.Services.AddRhiGraphics(builder.Configuration, offscreen: true);
}
else
{
	builder.Services.AddSilkWindowing(builder.Configuration);
	builder.Services.AddRhiGraphics(builder.Configuration);
}

builder.AddSystem<QuadSystem>();

using var app = builder.Build();
app.UseEvents();
if (!headless) app.UseSilkWindowing();
app.UseRhiGraphics();
app.UseSystem<QuadSystem>();
app.Run();
```

`AddRhiGraphics` (from `Ion.Extensions.Graphics.Headless`) registers the Vulkan and OpenGL ES backends and picks one
from `Ion:Graphics:PreferredBackend` with the platform's `Auto` order. With `offscreen: true` it renders into an
offscreen target sized from `Ion:Window`. The project references only `Ion.Core`, `Ion.Extensions.Windowing.SilkNet` and
`Ion.Extensions.Graphics.Headless`, which is why it publishes to a 7.7 MB executable.

## The system

```csharp title="Program.cs"
public sealed class QuadSystem(IGraphicsFrame frame, IScreenshotSource screenshots, IEvents events, IConfiguration config, ILogger<QuadSystem> logger) : IDisposable
{
	private TexturedQuad? _quad;
	private float _angle;

	[Init]
	public void Init(GameTime dt)
	{
		_quad = new TexturedQuad(frame.Device, frame.ColorFormat, frame.DepthFormat);
		logger.LogInformation("Quad ready on {Adapter} ({Backend}), target {Width}x{Height} {Format}.", frame.Device.AdapterName, frame.Device.Backend, frame.Width, frame.Height, frame.ColorFormat);
	}

	[Update]
	public void Update(GameTime dt)
	{
		if (_spin) _angle += dt.Delta;
	}

	[Render]
	public void Render(GameTime dt)
	{
		if (_quad is null || !frame.IsRendering) return;
		var aspect = frame.Width / (float)Math.Max(1, frame.Height);
		_quad.SetTransform(Matrix4x4.CreateRotationZ(_angle) * Matrix4x4.CreateScale(1f / aspect, 1f, 1f));
		_quad.Draw(frame);
	}

	[Destroy]
	public void Destroy(GameTime dt) => Dispose();

	public void Dispose()
	{
		_quad?.Dispose();
		_quad = null;
	}
}
```

- The GPU resources are created in `[Init]` at the default order, which runs after the graphics device is created at
  `StageOrder.Graphics` (-900). Using `frame.Device` earlier throws with a message that says so.
- `frame.IsRendering` is true during the Render stage, when the frame's targets are set. The first `ColorAttachment()`
  of a frame clears the target, later ones load it.
- The quad is released in `[Destroy]` at the default order, before the device goes away late in Destroy.

## The RHI in one class

`TexturedQuad` creates everything once:

```csharp title="TexturedQuad.cs"
_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.vert"));
_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.frag"));

_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
[
	new(0, ShaderStage.Vertex, BindingType.UniformBuffer),
	new(1, ShaderStage.Fragment, BindingType.Texture),
	new(2, ShaderStage.Fragment, BindingType.Sampler),
], "Quad"));
_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));

_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
{
	Label = "Textured quad",
	Layout = _pipelineLayout,
	Vertex = new VertexState(_vertex,
	[
		new VertexBufferLayout((uint)Marshal.SizeOf<Vertex>(), VertexStepMode.Vertex,
		[
			new VertexAttribute(VertexFormat.Float32x2, 0, 0),
			new VertexAttribute(VertexFormat.Float32x2, 8, 1),
		]),
	]),
	Fragment = new FragmentState(_fragment, [new ColorTargetState(colorFormat, BlendState.AlphaBlend)]),
	DepthStencil = depthFormat == TextureFormat.Undefined ? null : new DepthStencilState(depthFormat, DepthWriteEnabled: false, DepthCompare: CompareFunction.Always),
});
```

Then the buffers, a 4 x 4 checkerboard texture uploaded with `device.Queue.WriteTexture`, a point-clamp sampler and the
bind group. Drawing records one render pass and submits it:

```csharp title="TexturedQuad.cs"
public void Draw(ICommandEncoder encoder, RenderPassColorAttachment color, RenderPassDepthStencilAttachment? depth = null)
{
	var pass = encoder.BeginRenderPass(new RenderPassDescriptor([color], depth, "Quad"));
	pass.SetPipeline(_pipeline);
	pass.SetBindGroup(0, _bindGroup);
	pass.SetVertexBuffer(0, _vertices);
	pass.SetIndexBuffer(_indices, IndexFormat.Uint16);
	pass.DrawIndexed(6);
	pass.End();
}

public void Draw(IGraphicsFrame frame)
{
	if (!frame.IsRendering) return;
	var encoder = _device.CreateCommandEncoder("Quad");
	Draw(encoder, frame.ColorAttachment(), frame.DepthAttachment());
	_device.Queue.Submit(encoder.Finish());
}
```

The RHI is shaped like WebGPU: clip space is y up with depth 0 to 1, and the top-left vertex has UV (0, 0), so texel row
0 is at the top of the screen on every backend.

## Build-time shaders

```xml title="Ion.Examples.Quad.csproj"
<ItemGroup>
  <IonShader Include="Shaders/*.vert;Shaders/*.frag" />
</ItemGroup>
<Import Project="..\..\Ion\Ion.Shaders\Ion.Shaders.targets" />
```

```glsl title="Shaders/textured_quad.frag"
#version 450
// The texture and the sampler are separate bindings (WebGPU style); the GLES translation
// combines them into one sampler2D.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform texture2D uTexture;
layout(set = 0, binding = 2) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv);
}
```

Each `IonShader` is compiled with Shaderc to SPIR-V and translated with SPIRV-Cross to GLSL ES 3.10, and both are
embedded in the assembly. A shader error fails the build with the file and line. On OpenGL ES, `(set, binding)` pairs
are flattened to `set * 8 + binding`. See [Shaders](/Ion/rendering/shaders/).

## Tests and publishing

The sample has no test project of its own; its `TexturedQuad` is exercised by the backend contract tests in
`Ion.Extensions.Graphics.Rhi.Tests.Shared` (run by `Ion.Extensions.Graphics.Vulkan.Tests` and
`Ion.Extensions.Graphics.GLES.Tests`), which render it offscreen and check the checkerboard's pixels and goldens.

It is in the CI AOT lane, and it is the sample that was cross-published for linux-arm64 and run under
`qemu-aarch64` with Mesa's llvmpipe at OpenGL ES 3.1 and 3.0, producing frames identical to the x64 goldens:

```bash
dotnet publish Ion.Examples/Ion.Examples.Quad -p:IonTarget=linux-x64
dotnet publish Ion.Examples/Ion.Examples.Quad -p:IonTarget=linux-arm64 -p:IonArm64SysRoot=$HOME/sysroot-bionic-arm64
```

## Ideas to extend it

**Load a real texture.** Decode a PNG yourself and upload it with `device.Queue.WriteTexture`, or add the 2D renderer
and draw sprites next to the quad: `builder.Services.AddRendering2D()` and `app.UseRendering2D()` register the sprite
batch on the same device.

**Two quads.** Create a second `TexturedQuad` with `halfSize: 0.25f` and a different transform. Each has its own uniform
buffer, so both can be drawn in the same frame.

**A post effect.** Render the quad into a texture (`TextureUsage.RenderAttachment | TextureUsage.TextureBinding`) and
draw that texture to the frame with a second pipeline, as the backend tests do with `QuadRenderer`.

**Screenshots in a test.** Wrap the program in `IonTestHost` with `WithRendering(64, 64)` and assert on
`host.Screenshot()`; see [Snapshots and goldens](/Ion/tooling/snapshots-and-goldens/).

## See also

- [Graphics backends](/Ion/rendering/graphics-backends/) and [Windowing](/Ion/rendering/windowing/).
- [Shaders](/Ion/rendering/shaders/).
- [R36S](/Ion/platforms/r36s/): the arm64 cross build this sample verified.
