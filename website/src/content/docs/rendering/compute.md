---
title: Compute shaders
description: Run general-purpose GPU work on the RHI with compute pipelines and compute passes, storage buffers, storage textures and indirect dispatches, on Vulkan and OpenGL ES 3.1.
sidebar:
  order: 7
---

A compute shader runs on the GPU outside any render pass. It reads and writes buffers and textures directly, so it suits
work like particle simulation, culling, image filters and procedural generation. The RHI's compute API follows WebGPU:
compute pipelines, compute passes and dispatches.

Compute runs on Vulkan and on OpenGL ES 3.1 and later. Check `device.Limits.ComputeShaders` before using it: it is false
at the OpenGL ES 3.0 feature level, where creating a compute pipeline or beginning a compute pass throws
`NotSupportedException`.

## A complete example

This kernel doubles every value in a buffer and adds an offset:

```glsl title="Shaders/scale.comp"
#version 450

layout(local_size_x = 64) in;

layout(set = 0, binding = 0) uniform Params
{
	uint count;
	float scale;
	float offset;
	float pad;
} uParams;

layout(set = 0, binding = 1) readonly buffer Input { float values[]; } uInput;
layout(set = 0, binding = 2) buffer Output { float values[]; } uOutput;

void main()
{
	uint i = gl_GlobalInvocationID.x;
	if (i >= uParams.count) return;   // the last workgroup runs past the end
	uOutput.values[i] = uInput.values[i] * uParams.scale + uParams.offset;
}
```

Add `.comp` files to your `IonShader` items (see [Shaders](/Ion/rendering/shaders/#adding-shaders-to-a-project)). They
compile to SPIR-V and GLSL ES 3.10 like the other stages.

```csharp
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics.Rhi;

[StructLayout(LayoutKind.Sequential)]
readonly record struct Params(uint Count, float Scale, float Offset, float Pad = 0);

const uint Count = 1000;

// Load time: the shader, the layout, the pipeline and the bind group.
var module = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, typeof(MyKernel).Assembly, "scale.comp"));
var layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
[
	new(0, ShaderStage.Compute, BindingType.UniformBuffer),
	new(1, ShaderStage.Compute, BindingType.StorageBuffer),          // read-only
	new(2, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer),
]));
var pipeline = device.CreateComputePipeline(new ComputePipelineDescriptor
{
	Label = "Scale",
	Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([layout])),
	Compute = new ComputeState(module),
});

var parameters = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
var input = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.Storage | BufferUsage.CopyDst));
var output = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.Storage | BufferUsage.Vertex));
var group = device.CreateBindGroup(new BindGroupDescriptor(layout,
[
	BindGroupEntry.ForBuffer(0, parameters),
	BindGroupEntry.ForBuffer(1, input),
	BindGroupEntry.ForBuffer(2, output),
]));
device.Queue.WriteBuffer(parameters, 0, new Params(Count, 2f, 0.5f));

// Per frame: a compute pass, then the render passes that use the output.
var encoder = device.CreateCommandEncoder();
var pass = encoder.BeginComputePass();
pass.SetPipeline(pipeline);
pass.SetBindGroup(0, group);
pass.DispatchFor(Count, 64);   // ceil(1000 / 64) = 16 workgroups
pass.End();
// ... a render pass that reads `output` as a vertex buffer ...
device.Queue.Submit(encoder.Finish());
```

`DispatchFor(count, workgroupSize)` dispatches `ceil(count / workgroupSize)` workgroups in x. Pass the shader's
`local_size_x`, and skip the invocations past the end in the shader. `DispatchWorkgroups(x, y, z)` takes the workgroup
counts directly.

## Bindings

| `BindingType` | GLSL | Stages | Resource |
|---|---|---|---|
| `UniformBuffer` | `uniform Block { ... }` | any | a buffer with `BufferUsage.Uniform` |
| `StorageBuffer` | `readonly buffer Block { ... }` | fragment and compute (vertex where `VertexStorageBuffers` is true) | a buffer with `BufferUsage.Storage` |
| `ReadWriteStorageBuffer` | `buffer Block { ... }` | compute only | a buffer with `BufferUsage.Storage` |
| `StorageTexture` | `layout(rgba8) uniform writeonly image2D` | compute only | a view of one mip level of a texture with `TextureUsage.StorageBinding` |
| `Texture`, `Sampler` | `texture2D`, `sampler` | any | as in render pipelines |

Writable bindings are compute-only because OpenGL ES 3.1 guarantees no writable storage in the vertex and fragment
stages. Creating a bind group layout that makes them visible to another stage throws `NotSupportedException`.

Use `std430` rules for storage blocks (the default for `buffer` blocks in GLSL 4.5) and `std140` for uniform blocks. In both,
a `vec3` is aligned to 16 bytes and an array of `vec3` has a 16-byte stride, so use `vec4` or pad your C# structs.

### Storage textures

A storage texture is written by a compute shader with `imageStore`, then sampled, copied or written again:

```glsl title="Shaders/gradient.comp"
#version 450
layout(local_size_x = 8, local_size_y = 8) in;
layout(set = 0, binding = 0, rgba8) uniform writeonly image2D uImage;

void main()
{
	ivec2 p = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(p, imageSize(uImage)))) return;
	imageStore(uImage, p, vec4(vec2(p) / vec2(imageSize(uImage)), 1.0, 1.0));
}
```

```csharp
var image = device.CreateTexture(new TextureDescriptor(256, 256, TextureFormat.Rgba8Unorm,
	TextureUsage.StorageBinding | TextureUsage.TextureBinding));
// layout entry: new(0, ShaderStage.Compute, BindingType.StorageTexture)
// bind group entry: BindGroupEntry.ForTexture(0, image.DefaultView)
pass.DispatchWorkgroups(256 / 8, 256 / 8);
```

Storage textures are write-only, single-sampled 2D textures in one of the formats every backend can write:
`Rgba8Unorm`, `Rgba16Float`, `R32Float` and `Rgba32Float` (`TextureFormat.IsStorageFormat()`). The format qualifier in
the shader (`rgba8`, `rgba16f`, `r32f`, `rgba32f`) must match the texture's format. To write a single mip level of a
mipmapped texture, bind a view of that level (`texture.CreateView(new TextureViewDescriptor(level, 1))`).

## Ordering

You never insert barriers. As in WebGPU:

- Each dispatch sees the writes of every dispatch, draw and copy recorded or submitted before it, including earlier
  dispatches in the same pass.
- Everything after a dispatch sees its writes: draws reading the buffers as vertex, index, uniform or storage data,
  samplers reading the storage textures, copies, indirect dispatches, and `IBuffer.Read`.

The backends are conservative about this. Vulkan inserts a barrier between dispatches and at the start and end of every
compute pass. OpenGL ES issues a full `glMemoryBarrier` after every dispatch. If you dispatch many small kernels, batch
the work into fewer, larger dispatches.

## Indirect dispatch

`DispatchWorkgroupsIndirect(buffer, offset)` reads the workgroup counts from a buffer on the GPU, so an earlier pass
can decide how much work a later one does (for example, how many particles are alive). The arguments are three `uint`s,
`DispatchIndirectArgs` in C#:

```csharp
var args = device.CreateBuffer(new BufferDescriptor(12, BufferUsage.Indirect | BufferUsage.Storage | BufferUsage.CopyDst));
device.Queue.WriteBuffer(args, 0, new DispatchIndirectArgs(16));

pass.SetPipeline(countAlive);        // writes args from the GPU
pass.DispatchWorkgroups(1);
pass.SetPipeline(simulate);
pass.DispatchWorkgroupsIndirect(args);
```

The buffer needs `BufferUsage.Indirect` and the offset must be a multiple of 4.

## Limits

`device.Limits` reports what the running device allows. The minimums below hold on every device where `ComputeShaders`
is true:

| `DeviceLimits` | Minimum | Meaning |
|---|---|---|
| `ComputeShaders` | | Whether compute is available at all |
| `MaxComputeWorkgroupSizeX`, `Y` | 128 | Largest `local_size_x`, `local_size_y` |
| `MaxComputeWorkgroupSizeZ` | 64 | Largest `local_size_z` |
| `MaxComputeInvocationsPerWorkgroup` | 128 | Largest `local_size_x * local_size_y * local_size_z` |
| `MaxComputeWorkgroupsPerDimension` | 65535 | Largest count in one dimension of a dispatch |
| `MaxComputeWorkgroupStorageSize` | 16384 | Bytes of `shared` memory per workgroup |
| `MaxStorageBufferBindingSize` | 2^27 | Largest storage buffer binding, in bytes |
| `MinStorageBufferOffsetAlignment` | at most 256 | Alignment of storage buffer binding offsets |

A workgroup of 64 (`local_size_x = 64`, or 8 by 8) is a good default: it fits every device and fills a warp or
wavefront on desktop GPUs.

### OpenGL ES 3.1 devices

OpenGL ES 3.1 (the R36S's Mali-G31, many phones) guarantees only 4 storage buffer bindings and 4 image units in the
compute stage. Bindings flatten to `group * 8 + binding` as everywhere on the GLES backend, so keep a compute shader's
storage buffers and storage textures in **group 0, bindings 0 to 3**. A pipeline whose bindings flatten past the
device's limits fails when you create it, with a message naming the binding.

## See also

- [Shaders](/Ion/rendering/shaders/): writing and compiling shaders
- [Graphics backends](/Ion/rendering/graphics-backends/): feature levels and portability rules
- [Rendering overview](/Ion/rendering/overview/): the RHI types
