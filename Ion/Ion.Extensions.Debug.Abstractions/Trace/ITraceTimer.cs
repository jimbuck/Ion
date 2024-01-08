namespace Ion.Extensions.Debug;


public interface ITraceTimer
{
	ITraceTimerInstance Start(string name);
}

public interface ITraceTimer<T> : ITraceTimer { }