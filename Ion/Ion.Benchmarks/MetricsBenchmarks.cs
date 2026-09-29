using BenchmarkDotNet.Attributes;

using Ion.Extensions.Metrics;

namespace Ion.Benchmarks;

/// <summary>
/// Metrics v2 hot paths: a <see cref="MetricsScope"/> (what engine systems put around their work), the raw
/// <c>Begin</c>/<c>End</c> pair the generated schedule emits around every step, a game counter increment, and the
/// once-per-frame stats write. Every row runs <see cref="Ops"/> operations per invocation; the profile is rotated after
/// each batch (amortized, as the loop does once per frame) so the enabled rows always write into a live buffer.
/// </summary>
/// <remarks>
/// Targets (roadmap 4.6): a disabled scope below 1 ns and 0 B, an enabled one below 30 ns and 0 B. The enabled cost is
/// dominated by two <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> reads (clock_gettime through the vDSO on Linux).
/// </remarks>
[MemoryDiagnoser]
public class MetricsBenchmarks
{
	private const int Ops = 256;

	private static readonly SpanId Span = MetricsIds.Register("Bench.Scope");

	private FrameProfiler _disabled = null!;
	private FrameProfiler _enabled = null!;
	private FrameProfiler _stats = null!;
	private MetricsCounter _counter = null!;

	[GlobalSetup]
	public void Setup()
	{
		_disabled = new FrameProfiler(4, Ops) { IsActive = false };
		_enabled = new FrameProfiler(4, Ops) { IsActive = true };
		_stats = new FrameProfiler(300, Ops);
		_counter = new MetricsCounter("bench");
		_disabled.BeginFrame(0);
		_enabled.BeginFrame(0);
		_stats.BeginFrame(0);
	}

	[Benchmark(Baseline = true, OperationsPerInvoke = Ops)]
	public void Scope_Disabled()
	{
		var profiler = _disabled;
		for (var i = 0; i < Ops; i++)
		{
			using var _ = profiler.Scope(Span);
		}
	}

	[Benchmark(OperationsPerInvoke = Ops)]
	public void Scope_Enabled()
	{
		var profiler = _enabled;
		for (var i = 0; i < Ops; i++)
		{
			using var _ = profiler.Scope(Span);
		}

		Rotate(profiler);
	}

	[Benchmark(OperationsPerInvoke = Ops)]
	public void BeginEnd_GeneratedBracket_Disabled()
	{
		var profiler = _disabled;
		for (var i = 0; i < Ops; i++)
		{
			var t = FrameProfiler.IsProfilingEnabled ? profiler.Begin(Span) : 0L;
			if (FrameProfiler.IsProfilingEnabled) profiler.End(Span, t);
		}
	}

	[Benchmark(OperationsPerInvoke = Ops)]
	public void BeginEnd_GeneratedBracket_Enabled()
	{
		var profiler = _enabled;
		for (var i = 0; i < Ops; i++)
		{
			var t = FrameProfiler.IsProfilingEnabled ? profiler.Begin(Span) : 0L;
			if (FrameProfiler.IsProfilingEnabled) profiler.End(Span, t);
		}

		Rotate(profiler);
	}

	[Benchmark(OperationsPerInvoke = Ops)]
	public void Counter_Increment()
	{
		var counter = _counter;
		for (var i = 0; i < Ops; i++) counter.Increment();
	}

	[Benchmark]
	public void FrameStats_Write()
	{
		var stats = new FrameStats { DeltaMs = 16.667, FixedSteps = 1, DrawCalls = 10, Sprites = 100, Triangles = 200 };
		_stats.EndFrame(ref stats);
		_stats.BeginFrame((uint)_stats.FramesCompleted);
	}

	private static void Rotate(FrameProfiler profiler)
	{
		profiler.EndFrame();
		profiler.BeginFrame((uint)profiler.FramesCompleted);
	}
}
