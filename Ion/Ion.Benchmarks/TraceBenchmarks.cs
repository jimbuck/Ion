using Ion.Extensions.Debug;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Benchmarks;

/// <summary>
/// Cost of a `trace.Start("x")` / `timer.Stop()` bracket, which the engine's own systems put around nearly every stage
/// (and the font renderer puts around every glyph). Two configurations:
///  - Core default: `NullTraceTimer&lt;T&gt;` (used when AddDebugUtils is not called). Returns `new NullTimerInstance()` through an interface => boxes.
///  - Debug package installed: `TraceTimer&lt;T&gt;` -> `TraceManager.StartTraceTimer`, which in Release builds returns a cached boxed null instance.
/// </summary>
[MemoryDiagnoser]
public class TraceBenchmarks
{
	private ITraceTimer<TraceBenchmarks> _coreNullTimer = null!;
	private ITraceTimer<TraceBenchmarks> _debugTimer = null!;

	[GlobalSetup]
	public void Setup()
	{
		_coreNullTimer = BenchUtils.BuildHeadless(null, null).Services.GetRequiredService<ITraceTimer<TraceBenchmarks>>();
		_debugTimer = BenchUtils.BuildHeadless(
			services => services.AddDebugUtils(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
			null).Services.GetRequiredService<ITraceTimer<TraceBenchmarks>>();
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
