using System.Collections;

using Ion.Extensions.Coroutines;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Benchmarks;

/// <summary>
/// Per-frame cost of stepping N live coroutines that each yield <see cref="Wait.For(float)"/> every frame.
/// The coroutines are <c>IEnumerator&lt;Wait&gt;</c>, so the yielded <see cref="Wait"/> is read without boxing and stored
/// inline in the runner's handle: stepping allocates nothing. (Before 0.3 every yield boxed an <c>IWait</c> record struct,
/// 24 B per coroutine per frame.) <see cref="Update100LegacyCoroutines"/> keeps the non-generic <see cref="IEnumerator"/>
/// form for comparison, where the struct is boxed by the routine itself on every yield.
/// </summary>
[MemoryDiagnoser]
public class CoroutineBenchmarks
{
	[Params(100)]
	public int Coroutines { get; set; }

	private IonApplication _app = null!;
	private ICoroutineRunner _runner = null!;
	private ICoroutineRunner _legacyRunner = null!;
	private IonApplication _legacyApp = null!;
	private GameTime _dt = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();
		_app = BenchUtils.BuildHeadless(services => services.AddCoroutines(), null);
		_runner = _app.Services.GetRequiredService<ICoroutineRunner>();
		for (var i = 0; i < Coroutines; i++) _runner.Start(Forever());

		_legacyApp = BenchUtils.BuildHeadless(services => services.AddCoroutines(), null);
		_legacyRunner = _legacyApp.Services.GetRequiredService<ICoroutineRunner>();
		for (var i = 0; i < Coroutines; i++) _legacyRunner.Start(ForeverLegacy());
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_runner.StopAll();
		_app.Dispose();
		_legacyRunner.StopAll();
		_legacyApp.Dispose();
	}

	private static IEnumerator<Wait> Forever()
	{
		while (true) yield return Wait.For(0.001f);
	}

	private static IEnumerator ForeverLegacy()
	{
		while (true) yield return Wait.For(0.001f);
	}

	[Benchmark]
	public int Update100Coroutines()
	{
		_runner.Update(_dt);
		return _runner.Count;
	}

	[Benchmark]
	public int Update100LegacyCoroutines()
	{
		_legacyRunner.Update(_dt);
		return _legacyRunner.Count;
	}
}
