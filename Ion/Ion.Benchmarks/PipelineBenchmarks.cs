using Microsoft.Extensions.DependencyInjection;

using Ion.Benchmarks.GeneratedApp;
using Ion.Core;

namespace Ion.Benchmarks;

/// <summary>
/// Per-frame dispatch cost of one stage with N systems, compared against a flat loop of direct calls (what the source
/// generator will emit):
/// (a) the runtime schedule with leaf steps (a flat array of delegates),
/// (b) the schedule emitted by the Ion source generator (<c>Ion.Benchmarks.GeneratedApp</c> is compiled with it): one
///     method per stage calling every step directly, and
/// (c) a middleware chain built by hand with closures, each system calling <c>next</c>: the pre-0.3 dispatch shape.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
	[Params(1, 8, 32)]
	public int SystemCount { get; set; }

	private GameTime _dt = null!;
	private IonApplication _app = null!;
	private CounterSystem _system = null!;
	private ChainedCounter _chained = null!;
	private GameLoopDelegate _ionUpdate = null!;
	private GameLoopDelegate _manualClosureChain = null!;
	private CounterSystem[] _flat = null!;
	private GeneratedBenchmarkApp _generatedApp = null!;
	private GameLoopDelegate _generatedUpdate = null!;
	private GeneratedCounterSystem _generatedSystem = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();

		// Engine path: UseSystem<T>() N times -> reflection-bound leaf steps -> one flat delegate array for the stage.
		_app = BenchUtils.BuildHeadless(null, null, typeof(CounterSystem));
		for (var i = 1; i < SystemCount; i++) _app.UseSystem<CounterSystem>();
		_ionUpdate = _app.Build().Update;
		_system = (CounterSystem)_app.Services.GetService(typeof(CounterSystem))!;

		// The pre-0.3 shape, hand-built: `next => dt => system.OnUpdate(dt, next)` per system.
		_chained = new ChainedCounter();
		GameLoopDelegate chain = static _ => { };
		for (var i = 0; i < SystemCount; i++)
		{
			var next = chain;
			var sys = _chained;
			chain = dt => sys.OnUpdate(dt, next);
		}
		_manualClosureChain = chain;

		_flat = new CounterSystem[SystemCount];
		Array.Fill(_flat, _system);

		// Generated path: the same systems registered in a project compiled with the generator.
		_generatedApp = GeneratedApps.Counters(SystemCount);
		if (!_generatedApp.IsGenerated) throw new InvalidOperationException("The generated schedule is not in use.");
		_generatedUpdate = _generatedApp.Loop.Update;
		_generatedSystem = _generatedApp.Application.Services.GetRequiredService<GeneratedCounterSystem>();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_app.Dispose();
		_generatedApp.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int DirectCalls_FlatLoop()
	{
		var systems = _flat;
		for (var i = 0; i < systems.Length; i++) systems[i].UpdateDirect(_dt);
		return _system.Update;
	}

	[Benchmark]
	public int Ion_Schedule_LeafSteps()
	{
		_ionUpdate(_dt);
		return _system.Update;
	}

	[Benchmark]
	public int Ion_GeneratedSchedule()
	{
		_generatedUpdate(_dt);
		return _generatedSystem.Update;
	}

	[Benchmark]
	public int ManualClosureChain()
	{
		_manualClosureChain(_dt);
		return _chained.Update;
	}
}

/// <summary>
/// One-time startup cost: build the host and DI container, then plan, validate and bind the schedule of N systems by
/// reflection (all seven stages).
/// The application is disposed each iteration: every host owns a FileSystemWatcher for appsettings.json (reloadOnChange),
/// and leaking them exhausts the inotify limit on Linux within a few hundred builds.
/// </summary>
[MemoryDiagnoser]
public class PipelineBuildBenchmarks
{
	[Params(8, 32)]
	public int SystemCount { get; set; }

	[Benchmark]
	public int BuildApplicationAndSchedule()
	{
		using var app = BenchUtils.BuildHeadless(null, null, typeof(CounterSystem));
		for (var i = 1; i < SystemCount; i++) app.UseSystem<CounterSystem>();
		var loop = app.Build();
		return loop.Update is null ? 0 : 1;
	}
}
