namespace Kyber.Extensions.Debug;

public interface ITraceTimerInstance
{
	void Then(string name);

	void Stop();
}
