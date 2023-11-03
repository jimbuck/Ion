namespace Kyber.Extensions.Debug;

internal struct TraceTimerInstance : ITraceTimerInstance
{
	private readonly TraceManager _traceManager;
	private readonly string _prefix;
	private string _name;
	private int _id;
	private double _start;
	private int _threadId;

	public TraceTimerInstance(TraceManager tracerManager, string prefix, int id, string name, double start)
	{
		_traceManager = tracerManager;
		_prefix = prefix;
		_id = id;
		_name = name;
		_start = start;
		_threadId = Environment.CurrentManagedThreadId;
	}

	public void Then(string name)
	{
		if (!_traceManager.IsEnabled) return;

		_traceManager.StopTraceTimer(_id, _prefix + _name, _start, _threadId);
		var newInstance = (TraceTimerInstance)_traceManager.StartTraceTimer(_prefix, name);
		_id = newInstance._id;
		_name = newInstance._name;
		_start = newInstance._start;
		_threadId = newInstance._threadId;
	}

	public void Stop()
	{
		if (!_traceManager.IsEnabled) return;

		_traceManager.StopTraceTimer(_id, _prefix + _name, _start, _threadId);
	}
}
