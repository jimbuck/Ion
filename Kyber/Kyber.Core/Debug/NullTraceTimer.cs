using Kyber.Extensions.Debug;

namespace Kyber.Debug;

internal class NullTraceTimer : ITraceTimer
{
	public ITraceTimerInstance Start(string name)
	{
		return new NullTimerInstance();
	}
}

internal class NullTraceTimer<T> : ITraceTimer<T>
{
	public ITraceTimerInstance Start(string name)
	{
		return new NullTimerInstance();
	}
}
