using Arch.Core;

using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

using Ecs = Ion.Extensions.Ecs;

namespace Ion.Benchmarks;

/// <summary>
/// The ECS 3D extraction (<see cref="Scene3DExtractionSystem"/>) of 10,000 mesh entities (the scene of
/// <see cref="Renderer3DBenchmarks"/>: 2 meshes, 3 materials, a 100 x 100 grid) plus a camera and a directional light,
/// into the CPU-only <see cref="Renderer3D"/>:
/// <list type="bullet">
/// <item><c>Submit10k_Arrays</c>: the same 10,000 <c>Submit</c> calls from flat arrays (<c>Extract_Submit10k</c>, the baseline).</item>
/// <item><c>Extract10k</c>: the extraction (the chunk loop over <c>MeshRenderer</c> + <c>GlobalTransform</c>, the camera and light queries, the environment check).</item>
/// <item><c>ExtractAndQueue10k</c>: the extraction plus the renderer's CPU pipeline (culling, sort, batches, shadow), against
/// <c>ExtractAndQueue_10k</c> of <see cref="Renderer3DBenchmarks"/>.</item>
/// <item><c>PropagateUnchangedAndExtract10k</c>: the Render pass of the transform propagation when nothing moved (its dirty check) and the extraction, what the ECS adds per frame on top of the renderer.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class Scene3DExtractionBenchmarks
{
	private const int Objects = 10_000;

	private World _world = null!;
	private Renderer3D _renderer = null!;
	private Scene3DExtractionSystem _extraction = null!;
	private Ecs.TransformPropagationSystem _propagation = null!;
	private MeshRenderer[] _renderers = null!;
	private Matrix4x4[] _worlds = null!;
	private readonly GameTime _time = new();

	[GlobalSetup]
	public void Setup()
	{
		_renderer = new Renderer3D(null, new Rendering3DOptions()) { CpuTargetSize = (1920, 1080) };
		MeshHandle[] meshes = [_renderer.CreateMesh(MeshPrimitives.Cube()), _renderer.CreateMesh(MeshPrimitives.Sphere(0.5f, 16, 8))];
		MaterialHandle[] materials =
		[
			_renderer.CreateMaterial(new PbrMaterial(Color.Red)),
			_renderer.CreateMaterial(new PbrMaterial(Color.Blue, metallic: 1f)),
			_renderer.CreateMaterial(new UnlitMaterial(Color.Green)),
		];

		_world = World.Create();
		_renderers = new MeshRenderer[Objects];
		_worlds = new Matrix4x4[Objects];
		var random = new Random(3);
		for (var i = 0; i < Objects; i++)
		{
			var x = i % 100 - 50;
			var z = i / 100 - 50;
			var transform = new Transform(new Vector3(x * 1.5f, random.NextSingle(), z * 1.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, random.NextSingle() * MathF.Tau));
			_renderers[i] = new MeshRenderer(meshes[i % meshes.Length], materials[i % materials.Length]);
			_worlds[i] = transform.ToMatrix();
			_world.Create(transform, _renderers[i]);
		}

		_world.Create(Transform.LookAt(new Vector3(0, 40, 90), new Vector3(0, 0, 10)), new Camera { FieldOfView = MathF.PI / 3, Near = 0.5f, Far = 300f });
		_world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.4f, -1f, -0.3f), Vector3.UnitY)), new DirectionalLight());

		_propagation = new Ecs.TransformPropagationSystem(_world);
		_propagation.Propagate();
		_extraction = new Scene3DExtractionSystem(_world, _renderer);

		// Warm the arrays so the steady state is measured.
		ExtractAndQueue10k();
		Submit10k_Arrays();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		World.Destroy(_world);
		_renderer.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int Submit10k_Arrays()
	{
		_renderer.BeginFrame();
		var renderers = _renderers;
		var worlds = _worlds;
		for (var i = 0; i < renderers.Length; i++) _renderer.Submit(renderers[i], worlds[i]);
		return renderers.Length;
	}

	[Benchmark]
	public int Extract10k()
	{
		_renderer.BeginFrame();
		_extraction.ExtractAll(_time);
		return _extraction.LastFrame.MeshRenderers;
	}

	[Benchmark]
	public int ExtractAndQueue10k()
	{
		_renderer.BeginFrame();
		_extraction.ExtractAll(_time);
		_renderer.RunCpuPipeline();
		return _renderer.LastFrameStatistics.Batches;
	}

	[Benchmark]
	public int PropagateUnchangedAndExtract10k()
	{
		_propagation.Propagate();
		_renderer.BeginFrame();
		_extraction.ExtractAll(_time);
		return _extraction.LastFrame.MeshRenderers;
	}
}
