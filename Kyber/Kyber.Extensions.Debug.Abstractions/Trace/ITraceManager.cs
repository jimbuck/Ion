namespace Kyber.Extensions.Debug;

public interface ITraceManager {
	bool IsEnabled { get; set; }
	void Start();
	void Stop();
	void Clear();
	void OutputTrace();
}
