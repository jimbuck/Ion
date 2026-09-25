using System.Collections;

using Ion.Extensions.Coroutines;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Benchmarks;

/// <summary>
/// Per-frame cost of stepping N live coroutines that each yield <see cref="Wait.For(float)"/> every frame.
/// Every yielded <see cref="IWait"/> is a record struct stored in an interface slot, so it is boxed on each yield.
/// </summary>
[MemoryDiagnoser]
public class CoroutineBenchmarks
{
	[Params(100)]
	public int Coroutines { get; set; }

	private ICoroutineRunner _runner = null!;
	private GameTime _dt = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();
		var app = BenchUtils.BuildHeadless(services => services.AddCoroutines(), null);
		_runner = app.Services.GetRequiredService<ICoroutineRunner>();
		for (var i = 0; i < Coroutines; i++) _runner.Start(Forever());
	}

	private static IEnumerator Forever()
	{
		while (true) yield return Wait.For(0.001f);
	}

	[Benchmark]
	public int Update100Coroutines()
	{
		_runner.Update(_dt);
		return _runner.Count;
	}
}
