using Ion.Core;

namespace Ion.Benchmarks;

/// <summary>
/// Per-frame dispatch cost of the middleware pipeline for N systems, compared against
/// (a) the same closure chain built by hand (no reflection binder involved) and
/// (b) a flat loop of direct calls, which is what a source-generated schedule would emit.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
	[Params(1, 8, 32)]
	public int SystemCount { get; set; }

	private GameTime _dt = null!;
	private CounterSystem _system = null!;
	private GameLoopDelegate _ionUpdate = null!;
	private GameLoopDelegate _manualClosureChain = null!;
	private CounterSystem[] _flat = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();

		// Engine path: UseSystem<T>() N times -> reflection binder -> MiddlewarePipelineBuilder.Build().
		var app = BenchUtils.BuildHeadless(null, null, typeof(CounterSystem));
		for (var i = 1; i < SystemCount; i++) app.UseSystem<CounterSystem>();
		var loop = app.Build();
		_ionUpdate = loop.Update;
		_system = (CounterSystem)app.Services.GetService(typeof(CounterSystem))!;

		// Same shape, hand-built: `next => dt => system.OnUpdate(dt, next)`.
		GameLoopDelegate chain = static _ => { };
		for (var i = 0; i < SystemCount; i++)
		{
			var next = chain;
			var sys = _system;
			chain = dt => sys.OnUpdate(dt, next);
		}
		_manualClosureChain = chain;

		_flat = new CounterSystem[SystemCount];
		Array.Fill(_flat, _system);
	}

	[Benchmark(Baseline = true)]
	public int DirectCalls_FlatLoop()
	{
		var systems = _flat;
		for (var i = 0; i < systems.Length; i++) systems[i].UpdateDirect(_dt);
		return _system.Update;
	}

	[Benchmark]
	public int Ion_ReflectionBoundPipeline()
	{
		_ionUpdate(_dt);
		return _system.Update;
	}

	[Benchmark]
	public int ManualClosureChain()
	{
		_manualClosureChain(_dt);
		return _system.Update;
	}
}

/// <summary>
/// One-time startup cost: build the host and DI container, bind N systems by reflection and build all seven stage pipelines.
/// The application is disposed each iteration: every host owns a FileSystemWatcher for appsettings.json (reloadOnChange),
/// and leaking them exhausts the inotify limit on Linux within a few hundred builds.
/// </summary>
[MemoryDiagnoser]
public class PipelineBuildBenchmarks
{
	[Params(8, 32)]
	public int SystemCount { get; set; }

	[Benchmark]
	public int BuildApplicationAndPipelines()
	{
		using var app = BenchUtils.BuildHeadless(null, null, typeof(CounterSystem));
		for (var i = 1; i < SystemCount; i++) app.UseSystem<CounterSystem>();
		var loop = app.Build();
		return loop.Update is null ? 0 : 1;
	}
}
