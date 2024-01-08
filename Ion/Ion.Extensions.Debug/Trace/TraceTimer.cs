namespace Ion.Extensions.Debug;


internal class TraceTimer : ITraceTimer
{
	private readonly TraceManager _traceManager;
	private readonly string _prefix;

	public TraceTimer(ITraceManager traceManager, string prefix)
	{
		_traceManager = (TraceManager)traceManager;
		_prefix = prefix + "::";
	}

	public ITraceTimerInstance Start(string name)
	{
		return _traceManager.StartTraceTimer(_prefix, name);
	}
}

internal class TraceTimer<T> : ITraceTimer<T>
{
	private readonly ITraceTimer _timer;

	public TraceTimer(ITraceManager traceManager)
	{
		_timer = new TraceTimer(traceManager, typeof(T).Name);
	}

	public ITraceTimerInstance Start(string name)
	{
		return _timer.Start(name);
	}
}
