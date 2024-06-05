using System.Runtime.CompilerServices;

namespace Ion.Extensions.Debug;


internal class TraceTimer(ITraceManager traceManager, string prefix) : ITraceTimer
{
	private readonly TraceManager _traceManager = (TraceManager)traceManager;
	private readonly string _prefix = prefix + "::";

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ITraceTimerInstance Start(string name)
	{
		return _traceManager.StartTraceTimer(_prefix, name);
	}
}

internal class TraceTimer<T>(ITraceManager traceManager) : ITraceTimer<T>
{
	private readonly TraceTimer _timer = new(traceManager, typeof(T).Name);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ITraceTimerInstance Start(string name)
	{
		return _timer.Start(name);
	}
}
