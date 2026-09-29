using Ion.Benchmarks.GeneratedApp;
using Ion.Core;
using Ion.Extensions.Metrics;
using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Benchmarks;

/// <summary>
/// The "engine tax": one full headless <see cref="GameLoop.Step"/> (First, FixedUpdate, Update, Render, Last) with the
/// EventSystem installed and 8 trivial user systems that each bind every stage. No graphics, audio or assets involved.
/// Allocations reported here are pure engine overhead that a real game pays every frame.
/// </summary>
[MemoryDiagnoser]
public class FullFrameBenchmarks
{
	private readonly List<IonApplication> _apps = [];
	private GameTime _dt = null!;
	private GameLoop _eventsOnly = null!;
	private GameLoop _eightSystems = null!;
	private GameLoop _eightSystemsWithMetrics = null!;
	private GeneratedBenchmarkApp _eightSystemsGeneratedMetrics = null!;
	private GeneratedBenchmarkApp _eightSystemsGeneratedProfiling = null!;
	private GameLoop _eightSystemsInScene = null!;
	private GeneratedBenchmarkApp _eightSystemsGenerated = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();

		_eventsOnly = Track(BenchUtils.BuildHeadless(null, null)).Build();

		var eight = Enumerable.Range(0, 8).Select(_ => typeof(CounterSystem)).ToArray();
		_eightSystems = Track(BenchUtils.BuildHeadless(null, null, eight)).Build();

		// The metrics module with its defaults: frame stats every frame, profiling off, no frame log, the Ion meter on.
		var metrics = Track(BenchUtils.BuildHeadless(
			services => services.AddMetrics(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
			app => app.UseMetrics(),
			eight));
		_eightSystemsWithMetrics = metrics.Build();

		var scoped = Track(BenchUtils.BuildHeadless(
			services => { services.AddScenes(); services.AddScoped<CounterSystem>(); },
			app => app.UseScene(1, scene => { for (var i = 0; i < 8; i++) scene.UseSystem<CounterSystem>(); })));
		_eightSystemsInScene = scoped.Build();
		var events = scoped.Services.GetRequiredService<IEvents>();
		events.EmitChangeScene(1);
		_eightSystemsInScene.Init(_dt);
		_eightSystemsInScene.Step(_dt);

		// The same shape compiled with the Ion source generator: every stage is one method of direct calls.
		_eightSystemsGenerated = GeneratedApps.EightStageSystems();
		if (!_eightSystemsGenerated.IsGenerated) throw new InvalidOperationException("The generated schedule is not in use.");
		_eightSystemsGenerated.Loop.Initialize();

		// The generated schedule with a frame profiler: stats only, then a span per step, scope and stage.
		_eightSystemsGeneratedMetrics = GeneratedApps.EightStageSystemsWithMetrics(profiling: false);
		_eightSystemsGeneratedMetrics.Loop.Initialize();
		_eightSystemsGeneratedProfiling = GeneratedApps.EightStageSystemsWithMetrics(profiling: true);
		_eightSystemsGeneratedProfiling.Loop.Initialize();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		foreach (var app in _apps) app.Dispose();
		_eightSystemsGenerated.Dispose();
		_eightSystemsGeneratedMetrics.Dispose();
		_eightSystemsGeneratedProfiling.Dispose();
		_apps.Clear();
	}

	private IonApplication Track(IonApplication app)
	{
		_apps.Add(app);
		return app;
	}

	[Benchmark(Baseline = true)]
	public void Step_EventSystemOnly() => _eventsOnly.Step(_dt);

	[Benchmark]
	public void Step_8Systems() => _eightSystems.Step(_dt);

	[Benchmark]
	public void Step_8Systems_GeneratedSchedule() => _eightSystemsGenerated.Loop.Step(_dt);

	[Benchmark]
	public void Step_8Systems_MetricsInstalled() => _eightSystemsWithMetrics.Step(_dt);

	[Benchmark]
	public void Step_8Systems_GeneratedSchedule_FrameStats() => _eightSystemsGeneratedMetrics.Loop.Step(_dt);

	[Benchmark]
	public void Step_8Systems_GeneratedSchedule_Profiling() => _eightSystemsGeneratedProfiling.Loop.Step(_dt);

	[Benchmark]
	public void Step_8Systems_InsideScene() => _eightSystemsInScene.Step(_dt);
}
