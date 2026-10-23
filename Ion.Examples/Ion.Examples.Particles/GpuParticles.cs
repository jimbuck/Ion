using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Examples.Particles;

/// <summary>One particle as the shaders see it: (position, velocity) and (color, life in [0, 1]), 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Particle(Vector4 State, Vector4 Color)
{
	/// <summary>The position in world units (y up, the floor at -1, x from minus to plus the aspect ratio).</summary>
	public Vector2 Position => new(State.X, State.Y);

	/// <summary>The remaining life, 1 at spawn and 0 at death.</summary>
	public float Life => Color.W;
}

/// <summary>The simulation uniforms (std140, 48 bytes), matching <c>particles.comp</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct SimulationUniforms(Vector2 Emitter, Vector2 Gravity, float Dt, float Time, uint Count, uint Frame, float Lifetime, float Speed, float Pad0 = 0, float Pad1 = 0);

/// <summary>The view uniforms (std140, 16 bytes), matching <c>particle.vert</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ViewUniforms(Vector2 Scale, float Size, float Pad = 0);

/// <summary>
/// The GPU side of the sample: one storage buffer of particles that a compute pipeline steps every frame and a render
/// pipeline draws as instanced quads, reading the same buffer as an instance-rate vertex buffer. Nothing is read back to
/// the CPU while it runs.
/// </summary>
public sealed class GpuParticles : IDisposable
{
	/// <summary>The compute shader's <c>local_size_x</c>.</summary>
	public const uint WorkgroupSize = 64;

	private static readonly uint ParticleSize = (uint)Marshal.SizeOf<Particle>();

	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _compute, _vertex, _fragment;
	private readonly IBindGroupLayout _simulationLayout, _drawLayout;
	private readonly IPipelineLayout _simulationPipelineLayout, _drawPipelineLayout;
	private readonly IComputePipeline _simulate;
	private readonly IRenderPipeline _draw;
	private readonly IBuffer _particles, _simulationUniforms, _viewUniforms;
	private readonly IBindGroup _simulationGroup, _drawGroup;

	/// <summary>Creates the buffers and pipelines and uploads <paramref name="initial"/>, drawing into <paramref name="colorFormat"/> targets.</summary>
	/// <exception cref="NotSupportedException">The device has no compute (<see cref="DeviceLimits.ComputeShaders"/>).</exception>
	public GpuParticles(IGraphicsDevice device, TextureFormat colorFormat, ReadOnlySpan<Particle> initial)
	{
		if (!device.Limits.ComputeShaders) throw new NotSupportedException($"GPU particles need compute shaders, which {device.Backend} on {device.AdapterName} does not have.");
		_device = device;
		Count = (uint)initial.Length;
		var assembly = typeof(GpuParticles).Assembly;
		_compute = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "particles.comp"));
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "particle.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "particle.frag"));

		// The particles are a storage buffer for the compute pass and a vertex buffer for the draw. CopySrc: tests read them back.
		_particles = device.CreateBuffer(new BufferDescriptor(Count * ParticleSize, BufferUsage.Storage | BufferUsage.Vertex | BufferUsage.CopyDst | BufferUsage.CopySrc, "Particles"));
		_simulationUniforms = device.CreateBuffer(new BufferDescriptor((ulong)Marshal.SizeOf<SimulationUniforms>(), BufferUsage.Uniform | BufferUsage.CopyDst, "Particle simulation"));
		_viewUniforms = device.CreateBuffer(new BufferDescriptor((ulong)Marshal.SizeOf<ViewUniforms>(), BufferUsage.Uniform | BufferUsage.CopyDst, "Particle view"));
		device.Queue.WriteBuffer(_particles, 0, initial);

		// Simulation: group 0 holds the uniforms and the particles (binding 1: within the 4 storage bindings ES 3.1 guarantees).
		_simulationLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Compute, BindingType.UniformBuffer),
			new(1, ShaderStage.Compute, BindingType.ReadWriteStorageBuffer),
		], "Particle simulation"));
		_simulationPipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_simulationLayout]));
		_simulate = device.CreateComputePipeline(new ComputePipelineDescriptor
		{
			Label = "Particle simulation",
			Layout = _simulationPipelineLayout,
			Compute = new ComputeState(_compute),
		});
		_simulationGroup = device.CreateBindGroup(new BindGroupDescriptor(_simulationLayout,
		[
			BindGroupEntry.ForBuffer(0, _simulationUniforms),
			BindGroupEntry.ForBuffer(1, _particles),
		]));

		// Drawing: a 4-vertex strip per particle, the particle read per instance; additive blending.
		_drawLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Vertex, BindingType.UniformBuffer)], "Particle view"));
		_drawPipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_drawLayout]));
		_draw = device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = "Particles",
			Layout = _drawPipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout(ParticleSize, VertexStepMode.Instance,
				[
					new VertexAttribute(VertexFormat.Float32x4, 0, 0),
					new VertexAttribute(VertexFormat.Float32x4, 16, 1),
				]),
			]),
			Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleStrip },
			Fragment = new FragmentState(_fragment, [new ColorTargetState(colorFormat, BlendState.Additive)]),
		});
		_drawGroup = device.CreateBindGroup(new BindGroupDescriptor(_drawLayout, [BindGroupEntry.ForBuffer(0, _viewUniforms)]));
	}

	/// <summary>The number of particles.</summary>
	public uint Count { get; }

	/// <summary>
	/// Steps the simulation by <paramref name="simulation"/>'s time step in a compute pass, then draws the particles into
	/// <paramref name="frame"/> in a render pass of the same command buffer: the draw sees the dispatch's writes.
	/// </summary>
	internal void StepAndDraw(IGraphicsFrame frame, in SimulationUniforms simulation, in ViewUniforms view)
	{
		_device.Queue.WriteBuffer(_simulationUniforms, 0, simulation);
		_device.Queue.WriteBuffer(_viewUniforms, 0, view);

		var encoder = _device.CreateCommandEncoder("Particles");
		var compute = encoder.BeginComputePass("Particle simulation");
		compute.SetPipeline(_simulate);
		compute.SetBindGroup(0, _simulationGroup);
		compute.DispatchFor(Count, WorkgroupSize);
		compute.End();

		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([frame.ColorAttachment()], label: "Particles"));
		pass.SetPipeline(_draw);
		pass.SetBindGroup(0, _drawGroup);
		pass.SetVertexBuffer(0, _particles);
		pass.Draw(4, Count);
		pass.End();
		_device.Queue.Submit(encoder.Finish());
	}

	/// <summary>Copies the particles back to the CPU (blocks until the GPU is done). For tests and debugging, not per frame.</summary>
	public Particle[] Read()
	{
		var result = new Particle[Count];
		using var readback = _device.CreateBuffer(new BufferDescriptor(Count * ParticleSize, BufferUsage.MapRead | BufferUsage.CopyDst));
		var encoder = _device.CreateCommandEncoder("Particle readback");
		encoder.CopyBufferToBuffer(_particles, 0, readback, 0, Count * ParticleSize);
		_device.Queue.Submit(encoder.Finish());
		readback.Read(0, MemoryMarshal.AsBytes(result.AsSpan()));
		return result;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_drawGroup.Dispose(); _simulationGroup.Dispose();
		_viewUniforms.Dispose(); _simulationUniforms.Dispose(); _particles.Dispose();
		_draw.Dispose(); _simulate.Dispose();
		_drawPipelineLayout.Dispose(); _simulationPipelineLayout.Dispose();
		_drawLayout.Dispose(); _simulationLayout.Dispose();
		_fragment.Dispose(); _vertex.Dispose(); _compute.Dispose();
	}
}
