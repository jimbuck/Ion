---
title: Shaders
description: Write GLSL 4.5 shaders once, compile them at build time to SPIR-V and GLSL ES with Ion.Shaders, and load them on any backend.
sidebar:
  order: 6
---

Ion shaders are written once in **GLSL 4.5 (the Vulkan dialect)** and compiled **at build time** into both forms the
backends need:

- **SPIR-V** for Vulkan, compiled with [Shaderc](https://github.com/google/shaderc).
- **GLSL ES 3.10** for OpenGL ES, translated from the SPIR-V with
  [SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross).

Both are embedded in your assembly as resources. At run time `EmbeddedShaders` picks the one that matches the device.
Nothing is compiled on the player's machine, and a shader error fails your build with the file and line.

You only need this page if you write your own shaders: a custom renderer on the RHI, or a
[custom 3D material](/Ion/rendering/3d/meshes-and-materials/#custom-material-shaders). The built-in sprite, mesh, PBR and
skybox shaders are compiled the same way and ship inside the engine's assemblies.

## Adding shaders to a project

Import `Ion.Shaders.targets` and list your shaders as `IonShader` items:

```xml title="MyGame.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <IonShader Include="Shaders/*.vert;Shaders/*.frag" />
    <!-- Included files: listed so that editing one recompiles the shaders. -->
    <IonShaderInclude Include="Shaders/*.glsl" />
  </ItemGroup>

  <Import Project="path/to/Ion/Ion/Ion.Shaders/Ion.Shaders.targets" />
</Project>
```

:::note
`Ion.Shaders.targets` lives in the Ion source tree (`Ion/Ion.Shaders/`) and builds the `Ion.Shaders` compiler project
before your project. It is not part of the `Ion` NuGet package yet, so a project that compiles its own shaders needs a
checkout of the Ion repository to import it from.
:::

For each `IonShader` item the build:

1. Resolves `#include "file"` lines (relative to the including shader; each file is included once per shader, and
   `#line` directives keep error line numbers right).
2. Compiles the GLSL to SPIR-V for a Vulkan 1.0 environment, so it runs on every Vulkan device.
3. With `IonShaderGles`, translates the SPIR-V to GLSL ES 3.10.
4. Embeds `Shaders/<file>.spv` and `Shaders/<file>.es.glsl` as manifest resources.

Compilation runs incrementally: only when a shader, an `IonShaderInclude` file, the compiler or the targets file
changed.

| MSBuild property | Default | Meaning |
|---|---|---|
| `IonShaderGles` | `true` | Also emit GLSL ES 3.10. Turn off only if you never run on OpenGL ES |
| `IonShaderOptimize` | `true` | Optimize the SPIR-V (Shaderc's performance level). Debug info is always generated |
| `IonShaderOutDir` | `obj/.../IonShaders/` | Where the compiled files are written before embedding |

The stage comes from the file extension: `.vert` (vertex) and `.frag` (fragment). The compiler also accepts `.comp`, but
the RHI has no compute pipelines, so compute shaders cannot be used yet.

### Build errors

Errors are printed in MSBuild's format, so IDEs link them to the file:

```
/src/MyGame/Shaders/glow.frag: error ION_SHADER: glow.frag:14: error: 'uColour' : undeclared identifier
```

A shader that samples one texture with two different samplers is also a build error, because OpenGL ES has only
combined samplers.

## Writing a shader

Shaders use the Vulkan dialect of GLSL 4.5 with **separate textures and samplers** (WebGPU style) and explicit
`set`/`binding` numbers. Clip space is the RHI's: y up, depth 0 to 1.

```glsl title="Shaders/textured_quad.vert"
#version 450
// Clip space is the RHI's (WebGPU): y up, depth in [0, 1].

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inUv;

layout(location = 0) out vec2 vUv;

layout(set = 0, binding = 0) uniform Transform
{
	mat4 uTransform;
};

void main()
{
	vUv = inUv;
	gl_Position = uTransform * vec4(inPosition, 0.0, 1.0);
}
```

```glsl title="Shaders/textured_quad.frag"
#version 450
// The texture and the sampler are separate bindings; the GLES translation combines them into one sampler2D.

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform texture2D uTexture;
layout(set = 0, binding = 2) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv);
}
```

### Rules for portable shaders

The GLSL ES translation has to fit OpenGL ES 3.x, so follow these rules (the build enforces some of them):

| Rule | Why |
|---|---|
| Keep `binding` numbers below 8 and use at most 4 sets (3 on ES 3.0 devices) | Bindings are flattened to `set * 8 + binding` (`GlesBindings.Slot`) |
| At most 16 textures per stage | GLES texture unit limit |
| Sample each texture with one sampler | SPIRV-Cross builds one combined sampler per texture |
| No storage buffers in the vertex stage | Not guaranteed on GLES 3.1 (`DeviceLimits.VertexStorageBuffers`). Put per-instance data in an instance-rate vertex buffer |
| Write clip space as WebGPU does (y up, z in 0 to 1) | The translation negates y and remaps depth to GL's -1 to 1 for you |
| Use `std140` layout rules for uniform blocks | What both APIs agree on |

What the translation does for you:

- Separate `texture2D` and `sampler` bindings become one `sampler2D` (named `texture_sampler`), whose sampler object is
  the one the shader used with it.
- `(set, binding)` pairs become `layout(binding = N)` with `N = set * 8 + binding`, and a comment table lists each
  uniform block and combined sampler with its slot.
- Clip-space y is negated and depth is remapped, so the same geometry lands in the same place on both backends (the GLES
  backend flips once when presenting).
- Varyings are renamed by location (`ion_var_N`) so stages link by name even on GLSL ES 3.00.
- On ES 3.0 devices the 3.10 source is rewritten at load (binding and location qualifiers removed; slots assigned by
  name from the table).

Cube maps are sampled with `textureCube` plus a `sampler`, and shadow maps with a comparison sampler
(`sampler2DShadow` after translation).

## Loading shaders at run time

`EmbeddedShaders` (namespace `Ion.Extensions.Graphics.Rhi`) reads the compiled form for the running device:

```csharp
using Ion.Extensions.Graphics.Rhi;

var assembly = typeof(MyRenderer).Assembly;

// A descriptor in the device's language (SPIR-V or GLSL ES), stage from the extension (.vert or .frag):
var vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.vert"));
var fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.frag"));

// Or the raw bytes:
byte[] spirv = EmbeddedShaders.Load(assembly, "textured_quad.vert", ShaderLanguage.SpirV);
```

`IGraphicsDevice.ShaderLanguage` tells you which language the device accepts (`SpirV` for Vulkan, `GlslEs` for OpenGL
ES). If the resource is missing, `Load` throws `FileNotFoundException` with a hint to add the shader as an `IonShader`
item and import the targets file.

## A complete RHI renderer

This is the Quad sample's renderer, trimmed: a textured quad with one pipeline, one bind group (uniform buffer, texture,
sampler), a vertex buffer and an index buffer. It runs on Vulkan and OpenGL ES unchanged.

```csharp title="TexturedQuad.cs"
using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

public sealed class TexturedQuad : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct Vertex(Vector2 Position, Vector2 Uv);

	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex, _fragment;
	private readonly IBindGroupLayout _layout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly IRenderPipeline _pipeline;
	private readonly IBuffer _vertices, _indices, _uniforms;
	private readonly ITexture _texture;
	private readonly ISampler _sampler;
	private readonly IBindGroup _bindGroup;

	public TexturedQuad(IGraphicsDevice device, TextureFormat colorFormat, TextureFormat depthFormat, byte[] rgba, uint width, uint height)
	{
		_device = device;
		var assembly = typeof(TexturedQuad).Assembly;
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "textured_quad.frag"));

		// Group 0: the uniform block (vertex), the texture and the sampler (fragment).
		_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Vertex, BindingType.UniformBuffer),
			new(1, ShaderStage.Fragment, BindingType.Texture),
			new(2, ShaderStage.Fragment, BindingType.Sampler),
		], "Quad"));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));

		// Pipelines are expensive: create them at load time, never per frame.
		_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = "Textured quad",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout((uint)Marshal.SizeOf<Vertex>(), VertexStepMode.Vertex,
				[
					new VertexAttribute(VertexFormat.Float32x2, 0, 0),   // format, offset, shader location
					new VertexAttribute(VertexFormat.Float32x2, 8, 1),
				]),
			]),
			Fragment = new FragmentState(_fragment, [new ColorTargetState(colorFormat, BlendState.AlphaBlend)]),
			DepthStencil = depthFormat == TextureFormat.Undefined ? null
				: new DepthStencilState(depthFormat, DepthWriteEnabled: false, DepthCompare: CompareFunction.Always),
		});

		ReadOnlySpan<Vertex> vertices =
		[
			new(new(-0.5f, 0.5f), new(0, 0)), new(new(0.5f, 0.5f), new(1, 0)),
			new(new(0.5f, -0.5f), new(1, 1)), new(new(-0.5f, -0.5f), new(0, 1)),
		];
		ReadOnlySpan<ushort> indices = [0, 1, 2, 0, 2, 3];

		_vertices = device.CreateBuffer(new BufferDescriptor((ulong)(vertices.Length * Marshal.SizeOf<Vertex>()), BufferUsage.Vertex | BufferUsage.CopyDst, "Quad vertices"));
		_indices = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Index | BufferUsage.CopyDst, "Quad indices"));
		_uniforms = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.Uniform | BufferUsage.CopyDst, "Quad transform"));
		device.Queue.WriteBuffer(_vertices, 0, vertices);
		device.Queue.WriteBuffer(_indices, 0, indices);
		device.Queue.WriteBuffer(_uniforms, 0, Matrix4x4.Identity);

		_texture = device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "Quad texture"));
		device.Queue.WriteTexture(_texture, rgba);
		_sampler = device.CreateSampler(SamplerDescriptor.PointClamp);

		// Bind groups are immutable: create them once and reuse them every frame.
		_bindGroup = device.CreateBindGroup(new BindGroupDescriptor(_layout,
		[
			BindGroupEntry.ForBuffer(0, _uniforms),
			BindGroupEntry.ForTexture(1, _texture.DefaultView),
			BindGroupEntry.ForSampler(2, _sampler),
		]));
	}

	public void SetTransform(in Matrix4x4 transform) => _device.Queue.WriteBuffer(_uniforms, 0, transform);

	/// <summary>Draws into the current frame and submits.</summary>
	public void Draw(IGraphicsFrame frame)
	{
		if (!frame.IsRendering) return;   // minimized, or the swapchain is being recreated
		var encoder = _device.CreateCommandEncoder("Quad");
		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([frame.ColorAttachment()], frame.DepthAttachment(), "Quad"));
		pass.SetPipeline(_pipeline);
		pass.SetBindGroup(0, _bindGroup);
		pass.SetVertexBuffer(0, _vertices);
		pass.SetIndexBuffer(_indices, IndexFormat.Uint16);
		pass.DrawIndexed(6);
		pass.End();
		_device.Queue.Submit(encoder.Finish());
	}

	public void Dispose()
	{
		_bindGroup.Dispose(); _sampler.Dispose(); _texture.Dispose();
		_uniforms.Dispose(); _indices.Dispose(); _vertices.Dispose();
		_pipeline.Dispose(); _pipelineLayout.Dispose(); _layout.Dispose();
		_fragment.Dispose(); _vertex.Dispose();
	}
}
```

And the system that owns it:

```csharp title="QuadSystem.cs"
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class QuadSystem(IGraphicsFrame frame) : IDisposable
{
	private TexturedQuad? _quad;
	private float _angle;

	// Default order (0) runs after the graphics Init step (StageOrder.Graphics), so the device exists.
	[Init]
	public void Init(GameTime dt) =>
		_quad = new TexturedQuad(frame.Device, frame.ColorFormat, frame.DepthFormat, [255, 64, 0, 255], 1, 1);

	[Update]
	public void Update(GameTime dt) => _angle += dt.Delta;

	[Render]
	public void Render(GameTime dt)
	{
		if (_quad is null || !frame.IsRendering) return;
		var aspect = frame.Width / (float)Math.Max(1, frame.Height);
		_quad.SetTransform(Matrix4x4.CreateRotationZ(_angle) * Matrix4x4.CreateScale(1f / aspect, 1f, 1f));
		_quad.Draw(frame);
	}

	// Release GPU resources in Destroy (default order), before the device is destroyed.
	[Destroy]
	public void Destroy(GameTime dt) => Dispose();

	public void Dispose()
	{
		_quad?.Dispose();
		_quad = null;
	}
}
```

Key points about `IGraphicsFrame`:

- `ColorAttachment()` returns `LoadOp.Clear` with the configured clear color the **first** time it is called in a frame
  and `LoadOp.Load` afterwards, so the first pass clears for free and later passes draw on top. `DepthAttachment()` does
  the same for depth (or returns `null` when `Ion:Graphics:DepthBuffer` is off).
- `ColorFormat` and `DepthFormat` are stable across frames: use them for pipeline targets.
- If no pass is recorded in a frame, the graphics system clears the target before presenting.
- `IQueue.WriteBuffer` and `WriteTexture` copy your data into per-frame staging memory at the call, so the span can be
  reused immediately, and the write is seen by every command buffer submitted after it.

:::tip
With `AddIon()` your RHI passes run in the same frame as the sprite batch. Their order is the order of submission: a
pass you submit from a default-order Render step lands before the sprite batch's submission at the end of the stage, so
sprites draw on top. With the 3D renderer registered, prefer adding a render graph pass (see
[3D rendering overview](/Ion/rendering/3d/overview/#custom-passes)).
:::

## Custom 3D material shaders

The 3D renderer accepts custom fragment (and optionally vertex) shaders that follow its bind group conventions and draw
through the same passes, sorting, instancing and shadows as the built-in materials. The include files `ion_view.glsl` and
`ion_fragment_inputs.glsl` in `Ion/Ion.Extensions.Rendering3D/Shaders` give you the view block, `ionShadow()` and
`ionOutput()`. See [Meshes and materials](/Ion/rendering/3d/meshes-and-materials/#custom-material-shaders).

## Running the compiler by hand

The compiler is an ordinary .NET tool you can run directly:

```bash
dotnet Ion/Ion.Shaders/bin/Release/net10.0/Ion.Shaders.dll --out out/ --gles Shaders/glow.vert Shaders/glow.frag
```

| Argument | Meaning |
|---|---|
| `--out <dir>` | Output folder (required) |
| `--gles` | Also write `<file>.es.glsl` |
| `--no-optimize` | Do not optimize the SPIR-V |
| `<shader>...` | `.vert`, `.frag` or `.comp` files |

It exits with code 1 on the first compilation error and 2 on bad arguments. The Shaderc and SPIRV-Cross native libraries
come from the Silk.NET native packages and are loaded from the tool's own `runtimes/{rid}/native` folder first; if that
fails, the error lists every path tried.

## See also

- [Graphics backends](/Ion/rendering/graphics-backends/): the portability rules in context
- [Rendering overview](/Ion/rendering/overview/): the RHI types
- [Meshes and materials](/Ion/rendering/3d/meshes-and-materials/)
- [Quad example](/Ion/examples/quad/)
- Source: [Ion.Shaders.targets](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Shaders/Ion.Shaders.targets),
  [ShaderCompiler.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Shaders/ShaderCompiler.cs)
