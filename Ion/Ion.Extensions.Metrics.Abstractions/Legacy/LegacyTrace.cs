namespace Ion.Extensions.Metrics;

internal static class LegacyTrace
{
	public const string Message = "The 0.2 trace timers are adapters over the frame profiler and will be removed in 0.4. Use MetricsScope: `using var _ = profiler.Scope(spanId);` with a SpanId registered once (MetricsIds.Register).";
}
