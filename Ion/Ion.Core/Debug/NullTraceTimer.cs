using Ion.Extensions.Debug;

namespace Ion.Debug;

/// <summary>
/// Shared, pre-boxed <see cref="NullTimerInstance"/> so that <c>Start</c> never allocates.
/// </summary>
internal static class NullTraceTimerInstance
{
	public static readonly ITraceTimerInstance Instance = new NullTimerInstance();
}

internal class NullTraceTimer : ITraceTimer
{
	public ITraceTimerInstance Start(string name) => NullTraceTimerInstance.Instance;
}

internal class NullTraceTimer<T> : ITraceTimer<T>
{
	public ITraceTimerInstance Start(string name) => NullTraceTimerInstance.Instance;
}
