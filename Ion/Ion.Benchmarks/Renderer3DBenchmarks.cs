using BenchmarkDotNet.Attributes;

using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

namespace Ion.Benchmarks;

/// <summary>
/// CPU side of the 3D renderer, 10,000 <see cref="MeshRenderer"/>s per frame with 3 materials over 2 meshes, no GPU (the
/// CPU-only <see cref="Renderer3D"/>), the Stage 5 acceptance target being under 2 ms for extract and queue:
/// <list type="bullet">
/// <item><c>Extract_Submit10k</c>: the submissions alone (<c>BeginFrame</c> and 10,000 <c>Submit</c> calls copying the
/// renderer and its world matrix).</item>
/// <item><c>ExtractAndQueue_10k</c>: a whole frame's CPU pipeline: submissions, world bounds, frustum culling (about a
/// third of the grid is outside the view), sort keys, the sort, instanced batches and the 96-byte instance data, plus the
/// directional shadow fit and the caster batches (every object casts).</item>
/// <item><c>ExtractAndQueue_10k_NoShadows</c>: the same without a shadow-casting light.</item>
/// <item><c>ExtractAndQueue_10k_TwoCameras</c>: two cameras (split screen), so culling, sorting and batching run twice.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class Renderer3DBenchmarks
{
	private const int Objects = 10_000;

	private Renderer3D _renderer = null!;
	private MeshRenderer[] _renderers = null!;
	private Matrix4x4[] _worlds = null!;
	private Camera _camera;
	private Transform _eye;
	private Matrix4x4 _secondEye;

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

		_renderers = new MeshRenderer[Objects];
		_worlds = new Matrix4x4[Objects];
		var random = new Random(3);
		for (var i = 0; i < Objects; i++)
		{
			_renderers[i] = new MeshRenderer(meshes[i % meshes.Length], materials[i % materials.Length]);
			var x = i % 100 - 50;
			var z = i / 100 - 50;
			_worlds[i] = Matrix4x4.CreateRotationY(random.NextSingle() * MathF.Tau) * Matrix4x4.CreateTranslation(x * 1.5f, random.NextSingle(), z * 1.5f);
		}

		_camera = new Camera { FieldOfView = MathF.PI / 3, Near = 0.5f, Far = 300f };
		_eye = Transform.LookAt(new Vector3(0, 40, 90), new Vector3(0, 0, 10));
		_secondEye = Transform.LookAt(new Vector3(60, 30, 0), Vector3.Zero).ToMatrix();

		// Warm the arrays so the steady state (not first-frame growth) is measured.
		ExtractAndQueue_10k();
		ExtractAndQueue_10k_TwoCameras();
		ExtractAndQueue_10k_NoShadows();
	}

	[GlobalCleanup]
	public void Cleanup() => _renderer.Dispose();

	[Benchmark]
	public int Extract_Submit10k()
	{
		_renderer.BeginFrame();
		Submit();
		return _renderers.Length;
	}

	[Benchmark(Baseline = true)]
	public int ExtractAndQueue_10k()
	{
		_renderer.BeginFrame();
		_renderer.SetCamera(_camera, _eye);
		_renderer.AddLight(new DirectionalLight(), new Vector3(-0.4f, -1f, -0.3f));
		Submit();
		_renderer.RunCpuPipeline();
		return _renderer.LastFrameStatistics.Batches;
	}

	[Benchmark]
	public int ExtractAndQueue_10k_NoShadows()
	{
		_renderer.BeginFrame();
		_renderer.SetCamera(_camera, _eye);
		_renderer.AddLight(new DirectionalLight { CastShadows = false }, new Vector3(-0.4f, -1f, -0.3f));
		Submit();
		_renderer.RunCpuPipeline();
		return _renderer.LastFrameStatistics.Batches;
	}

	[Benchmark]
	public int ExtractAndQueue_10k_TwoCameras()
	{
		_renderer.BeginFrame();
		_renderer.AddCamera(_camera with { Viewport = new RectangleF(0, 0, 0.5f, 1) }, _eye.ToMatrix());
		_renderer.AddCamera(_camera with { Viewport = new RectangleF(0.5f, 0, 0.5f, 1), Priority = 1 }, _secondEye);
		_renderer.AddLight(new DirectionalLight(), new Vector3(-0.4f, -1f, -0.3f));
		Submit();
		_renderer.RunCpuPipeline();
		return _renderer.LastFrameStatistics.Batches;
	}

	private void Submit()
	{
		var renderers = _renderers;
		var worlds = _worlds;
		for (var i = 0; i < renderers.Length; i++) _renderer.Submit(renderers[i], worlds[i]);
	}
}
