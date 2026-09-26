#pragma warning disable CS0618 // The 0.2 API, kept as an adapter for one release.

using Ion.Extensions.Debug;

namespace Ion.Extensions.Metrics;

/// <summary>The <see cref="ITraceManager"/> adapter over <see cref="IMetrics"/>.</summary>
internal sealed class TraceManagerAdapter(IMetrics metrics) : ITraceManager
{
	public bool IsEnabled
	{
		get => metrics.IsProfiling;
		set => metrics.IsProfiling = value;
	}

	public void Start() => metrics.IsProfiling = true;

	public void Stop() => metrics.IsProfiling = false;

	public void Clear() => metrics.Profiler.Clear();

	public void OutputTrace() => metrics.WriteTrace();

	public ITraceTimer CreateTimer(string prefix) => new TraceTimerAdapter(metrics.Profiler, prefix);
}
