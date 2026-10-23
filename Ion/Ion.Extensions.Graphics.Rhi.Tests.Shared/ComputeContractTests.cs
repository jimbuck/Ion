using System.Reflection;
using System.Runtime.InteropServices;

using Ion.Testing;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// The compute part of the RHI contract: compute pipelines and passes, storage buffers read and written by dispatches,
/// the ordering WebGPU guarantees (each dispatch sees earlier dispatches, and later draws, copies and indirect dispatches
/// see its writes), indirect dispatches and storage textures. Run on every backend like
/// <see cref="HeadlessContractTests"/>, at feature levels that have compute.
/// </summary>
public abstract class ComputeContractTests
{
	private static readonly Assembly Shaders = typeof(ComputeContractTests).Assembly;

	/// <summary>The backend of the concrete test class (its <see cref="RhiBackendAttribute"/>).</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	/// <summary>Creates a validated headless device on <see cref="Backend"/>.</summary>
	protected virtual IGraphicsDevice CreateDevice(ValidationLog log) => log.CreateDevice(Backend, 2);

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ReportsComputeLimits()
	{
		var log = new ValidationLog();
		using (var device = CreateDevice(log))
		{
			var limits = device.Limits;
			Assert.True(limits.ComputeShaders);
			Assert.True(limits.MaxComputeWorkgroupSizeX >= 128, $"MaxComputeWorkgroupSizeX {limits.MaxComputeWorkgroupSizeX}");
			Assert.True(limits.MaxComputeWorkgroupSizeY >= 128, $"MaxComputeWorkgroupSizeY {limits.MaxComputeWorkgroupSizeY}");
			Assert.True(limits.MaxComputeWorkgroupSizeZ >= 64, $"MaxComputeWorkgroupSizeZ {limits.MaxComputeWorkgroupSizeZ}");
			Assert.True(limits.MaxComputeInvocationsPerWorkgroup >= 128, $"MaxComputeInvocationsPerWorkgroup {limits.MaxComputeInvocationsPerWorkgroup}");
			Assert.True(limits.MaxComputeWorkgroupsPerDimension >= 65535, $"MaxComputeWorkgroupsPerDimension {limits.MaxComputeWorkgroupsPerDimension}");
			Assert.True(limits.MaxComputeWorkgroupStorageSize >= 16384, $"MaxComputeWorkgroupStorageSize {limits.MaxComputeWorkgroupStorageSize}");
			Assert.True(limits.MaxStorageBufferBindingSize >= 1 << 24, $"MaxStorageBufferBindingSize {limits.MaxStorageBufferBindingSize}");
			Assert.True(limits.MinStorageBufferOffsetAlignment is >= 1 and <= 256, $"MinStorageBufferOffsetAlignment {limits.MinStorageBufferOffsetAlignment}");
		}

		log.AssertClean();
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ADispatchReadsAUniformAndAStorageBufferAndWritesAnother()
	{
		// 100 values: two workgroups of 64, the second partly past the end (the shader skips those invocations).
		const int Count = 100;
		var log = new ValidationLog();
		var result = new float[Count + 4];
		using (var device = CreateDevice(log))
		{
			var input = new float[Count];
			for (var i = 0; i < Count; i++) input[i] = i;

			using var parameters = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Uniform | BufferUsage.CopyDst));
			using var source = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.Storage | BufferUsage.CopyDst));
			using var output = device.CreateBuffer(new BufferDescriptor((Count + 4) * 4, BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor((Count + 4) * 4, BufferUsage.MapRead | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(parameters, 0, new Params(Count, 2f, 0.5f));
			device.Queue.WriteBuffer<float>(source, 0, input);
			// Sentinels past the end: the invocations past count must not write them.
			device.Queue.WriteBuffer<float>(output, 0, Enumerable.Repeat(-1f, Count + 4).ToArray());

			using var kernel = new Kernel(device, "scale.comp",
				[new(0, ShaderStage.Compute, BindingType.UniformBuffer), new(1, ShaderStage.Compute, BindingType.StorageBuffer), new(2, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer)],
				[BindGroupEntry.ForBuffer(0, parameters), BindGroupEntry.ForBuffer(1, source), BindGroupEntry.ForBuffer(2, output, 0, Count * 4)]);

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginComputePass();
			pass.SetPipeline(kernel.Pipeline);
			pass.SetBindGroup(0, kernel.Group);
			pass.DispatchFor(Count, 64);
			pass.End();
			encoder.CopyBufferToBuffer(output, 0, readback, 0, (Count + 4) * 4);
			device.Queue.Submit(encoder.Finish());
			readback.Read(0, MemoryMarshal.AsBytes(result.AsSpan()));
		}

		log.AssertClean();
		for (var i = 0; i < Count; i++) Assert.Equal(i * 2f + 0.5f, result[i]);
		for (var i = Count; i < Count + 4; i++) Assert.Equal(-1f, result[i]);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void DispatchesInOnePassAndAcrossPassesAreOrdered()
	{
		const int Count = 300;
		var log = new ValidationLog();
		var result = new uint[Count];
		using (var device = CreateDevice(log))
		{
			using var data = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.MapRead | BufferUsage.CopyDst));
			var initial = new uint[Count];
			for (var i = 0; i < Count; i++) initial[i] = (uint)i * 10;
			device.Queue.WriteBuffer<uint>(data, 0, initial);

			using var kernel = new Kernel(device, "increment.comp", [new(0, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer)], [BindGroupEntry.ForBuffer(0, data)]);

			// Three dispatches in one pass, then two more in a second pass of another command buffer: each read-modify-write
			// must see the one before.
			var first = device.CreateCommandEncoder();
			var pass = first.BeginComputePass();
			pass.SetPipeline(kernel.Pipeline);
			pass.SetBindGroup(0, kernel.Group);
			for (var i = 0; i < 3; i++) pass.DispatchFor(Count, 64);
			pass.End();
			device.Queue.Submit(first.Finish());

			var second = device.CreateCommandEncoder();
			pass = second.BeginComputePass();
			pass.SetBindGroup(0, kernel.Group);
			pass.SetPipeline(kernel.Pipeline);
			pass.DispatchFor(Count, 64);
			pass.DispatchFor(Count, 64);
			pass.DispatchWorkgroups(0); // nothing
			pass.End();
			second.CopyBufferToBuffer(data, 0, readback, 0, Count * 4);
			device.Queue.Submit(second.Finish());
			readback.Read(0, MemoryMarshal.AsBytes(result.AsSpan()));
		}

		log.AssertClean();
		for (var i = 0; i < Count; i++) Assert.Equal((uint)i * 10 + 5, result[i]);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AnIndirectDispatchReadsArgumentsWrittenByAnEarlierDispatch()
	{
		const int Count = 200;
		var log = new ValidationLog();
		var result = new uint[Count];
		var args = new uint[3];
		using (var device = CreateDevice(log))
		{
			using var data = device.CreateBuffer(new BufferDescriptor(Count * 4, BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var indirect = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor(Count * 4 + 16, BufferUsage.MapRead | BufferUsage.CopyDst));
			device.Queue.WriteBuffer<uint>(data, 0, new uint[Count]);
			device.Queue.WriteBuffer(indirect, 0, new DispatchIndirectArgs(0, 0, 0));

			using var writeArgs = new Kernel(device, "indirect_args.comp",
				[new(0, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer), new(1, ShaderStage.Compute, BindingType.StorageBuffer)],
				[BindGroupEntry.ForBuffer(0, indirect, 0, 12), BindGroupEntry.ForBuffer(1, data)]);
			using var increment = new Kernel(device, "increment.comp", [new(0, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer)], [BindGroupEntry.ForBuffer(0, data)]);

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginComputePass();
			pass.SetPipeline(writeArgs.Pipeline);
			pass.SetBindGroup(0, writeArgs.Group);
			pass.DispatchWorkgroups(1);
			pass.SetPipeline(increment.Pipeline);
			pass.SetBindGroup(0, increment.Group);
			pass.DispatchWorkgroupsIndirect(indirect);
			pass.End();
			encoder.CopyBufferToBuffer(data, 0, readback, 0, Count * 4);
			encoder.CopyBufferToBuffer(indirect, 0, readback, Count * 4, 16);
			device.Queue.Submit(encoder.Finish());
			readback.Read(0, MemoryMarshal.AsBytes(result.AsSpan()));
			readback.Read(Count * 4, MemoryMarshal.AsBytes(args.AsSpan()));
		}

		log.AssertClean();
		Assert.Equal([4u, 1u, 1u], args);
		Assert.All(result, value => Assert.Equal(1u, value));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ADrawReadsVerticesWrittenByADispatch()
	{
		// The compute pass writes the six vertices of a quad over the left half; the render pass draws them in red.
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var vertices = device.CreateBuffer(new BufferDescriptor(6 * 16, BufferUsage.Storage | BufferUsage.Vertex));
			using var kernel = new Kernel(device, "quad_vertices.comp", [new(0, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer)], [BindGroupEntry.ForBuffer(0, vertices)]);
			using var vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, Shaders, "depth_quad.vert"));
			using var fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, Shaders, "red.frag"));
			using var layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([]));
			using var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
			{
				Label = "Computed quad",
				Layout = layout,
				Vertex = new VertexState(vertex, [new VertexBufferLayout(16, VertexStepMode.Vertex, [new VertexAttribute(VertexFormat.Float32x3, 0, 0)])]),
				Fragment = new FragmentState(fragment, [new ColorTargetState(TextureFormat.Rgba8Unorm)]),
			});
			using var target = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			var encoder = device.CreateCommandEncoder();
			var compute = encoder.BeginComputePass();
			compute.SetPipeline(kernel.Pipeline);
			compute.SetBindGroup(0, kernel.Group);
			compute.DispatchWorkgroups(1);
			compute.End();
			var render = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, new Vector4(0, 0, 0, 1))]));
			render.SetPipeline(pipeline);
			render.SetVertexBuffer(0, vertices);
			render.Draw(6);
			render.End();
			device.Queue.Submit(encoder.Finish());
			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.AssertPixel(shot, 1, 1, new Rgba8(255, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 2, 6, new Rgba8(255, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 6, 1, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 5, 6, new Rgba8(0, 0, 0, 255));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AStorageTextureWrittenByADispatchIsCopiedAndSampled()
	{
		var log = new ValidationLog();
		Screenshot copied;
		Screenshot sampled;
		using (var device = CreateDevice(log))
		{
			using var image = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.StorageBinding | TextureUsage.TextureBinding | TextureUsage.CopySrc));
			using var kernel = new Kernel(device, "gradient.comp", [new(0, ShaderStage.Compute, BindingType.StorageTexture)], [BindGroupEntry.ForTexture(0, image.DefaultView)]);

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginComputePass();
			pass.SetPipeline(kernel.Pipeline);
			pass.SetBindGroup(0, kernel.Group);
			pass.DispatchWorkgroups(1, 1);
			pass.End();
			device.Queue.Submit(encoder.Finish());
			copied = Readback.Read(device, image);

			// Sampled after the copy (the texture goes back to its shader layout).
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var target = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var draw = new FullscreenDraw(device, "sample.frag", [new(0, ShaderStage.Fragment, BindingType.Texture), new(1, ShaderStage.Fragment, BindingType.Sampler)],
				[BindGroupEntry.ForTexture(0, image.DefaultView), BindGroupEntry.ForSampler(1, sampler)]);
			draw.Draw(target);
			sampled = Readback.Read(device, target);
		}

		log.AssertClean();
		foreach (var shot in (Screenshot[])[copied, sampled])
		{
			for (var y = 0; y < 8; y++)
			{
				for (var x = 0; x < 8; x++) QuadAssert.AssertPixel(shot, x, y, new Rgba8((byte)(x * 16), (byte)(y * 16), 255, 255));
			}
		}
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void RejectsWritableBindingsOutsideComputeAndUnsupportedStorageFormats()
	{
		var log = new ValidationLog();
		using (var device = CreateDevice(log))
		{
			Assert.Throws<NotSupportedException>(() => device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Fragment, BindingType.ReadWriteStorageBuffer)])));
			Assert.Throws<NotSupportedException>(() => device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.VertexFragment | ShaderStage.Compute, BindingType.StorageTexture)])));
			Assert.Throws<NotSupportedException>(() => device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Bgra8Unorm, TextureUsage.StorageBinding)));
			Assert.Throws<NotSupportedException>(() => device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Rgba8Unorm, TextureUsage.StorageBinding | TextureUsage.TextureBinding, Dimension: TextureDimension.Cube)));

			using var plain = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.Vertex | BufferUsage.CopyDst));
			using var layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer)]));
			Assert.Throws<ArgumentException>(() => device.CreateBindGroup(new BindGroupDescriptor(layout, [BindGroupEntry.ForBuffer(0, plain)])));

			var encoder = device.CreateCommandEncoder();
			var pass = encoder.BeginComputePass();
			Assert.Throws<ArgumentException>(() => pass.DispatchWorkgroupsIndirect(plain));
			Assert.Throws<InvalidOperationException>(() => encoder.BeginComputePass());
			Assert.Throws<InvalidOperationException>(() => encoder.Finish());
			pass.End();
			device.Queue.Submit(encoder.Finish());
		}

		log.AssertClean();
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct Params(uint Count, float Scale, float Offset, float Pad = 0);

	/// <summary>A compute pipeline over one bind group (group 0), from a compute shader of this assembly.</summary>
	private sealed class Kernel : IDisposable
	{
		private readonly IShaderModule _module;
		private readonly IBindGroupLayout _layout;
		private readonly IPipelineLayout _pipelineLayout;

		public Kernel(IGraphicsDevice device, string shader, BindGroupLayoutEntry[] layout, BindGroupEntry[] entries)
		{
			_module = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, Shaders, shader));
			Assert.Equal(ShaderStage.Compute, _module.Stage);
			_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(layout));
			_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));
			Pipeline = device.CreateComputePipeline(new ComputePipelineDescriptor { Label = shader, Layout = _pipelineLayout, Compute = new ComputeState(_module) });
			Group = device.CreateBindGroup(new BindGroupDescriptor(_layout, entries));
		}

		public IComputePipeline Pipeline { get; }

		public IBindGroup Group { get; }

		public void Dispose()
		{
			Group.Dispose();
			Pipeline.Dispose();
			_pipelineLayout.Dispose();
			_layout.Dispose();
			_module.Dispose();
		}
	}
}
