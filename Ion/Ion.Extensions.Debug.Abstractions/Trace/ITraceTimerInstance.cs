namespace Ion.Extensions.Debug;

public interface ITraceTimerInstance
{
	void Then(string name);

	void Stop();
}
