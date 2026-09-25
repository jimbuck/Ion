using System.Runtime.CompilerServices;

namespace Ion.Extensions.Debug;

internal sealed class TraceTimer(TraceManager traceManager, string prefix) : ITraceTimer
{
	private readonly string _prefix = prefix + "::";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ITraceTimerInstance Start(string name)
	{
		return traceManager.StartTraceTimer(_prefix, name);
	}
}

internal sealed class TraceTimer<T>(ITraceManager traceManager) : ITraceTimer<T>
{
	private readonly ITraceTimer _timer = traceManager.CreateTimer(typeof(T).Name);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ITraceTimerInstance Start(string name)
	{
		return _timer.Start(name);
	}
}
