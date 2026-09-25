using Ion.Extensions.Debug;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Benchmarks;

/// <summary>
/// Cost of a `trace.Start("x")` / `timer.Stop()` bracket, which the engine's own systems put around nearly every stage
/// (and the font renderer puts around every glyph). Two configurations:
///  - Core default: `NullTraceTimer&lt;T&gt;` (used when AddDebugUtils is not called). Returns a shared, pre-boxed `NullTimerInstance`, so Start allocates nothing.
///  - Debug package installed: `TraceTimer&lt;T&gt;` -> `TraceManager.StartTraceTimer`, which in Release builds returns a cached boxed null instance.
/// </summary>
[MemoryDiagnoser]
public class TraceBenchmarks
{
	private IonApplication _coreApp = null!;
	private IonApplication _debugApp = null!;
	private ITraceTimer<TraceBenchmarks> _coreNullTimer = null!;
	private ITraceTimer<TraceBenchmarks> _debugTimer = null!;

	[GlobalSetup]
	public void Setup()
	{
		_coreApp = BenchUtils.BuildHeadless(null, null);
		_coreNullTimer = _coreApp.Services.GetRequiredService<ITraceTimer<TraceBenchmarks>>();

		_debugApp = BenchUtils.BuildHeadless(
			services => services.AddDebugUtils(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
			null);
		_debugTimer = _debugApp.Services.GetRequiredService<ITraceTimer<TraceBenchmarks>>();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_coreApp.Dispose();
		_debugApp.Dispose();
	}

	[Benchmark(Baseline = true)]
	public void Core_NullTraceTimer_StartStop()
	{
		var t = _coreNullTimer.Start("Bench");
		t.Stop();
	}

	[Benchmark]
	public void Debug_TraceTimer_StartStop()
	{
		var t = _debugTimer.Start("Bench");
		t.Stop();
	}

	[Benchmark]
	public void Core_NullTraceTimer_StartStop_x100()
	{
		for (var i = 0; i < 100; i++)
		{
			var t = _coreNullTimer.Start("Bench");
			t.Stop();
		}
	}
}
