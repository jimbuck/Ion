namespace Ion.Extensions.Debug;

/// <summary>
/// Collects trace timings and writes them out (Chrome trace format).
/// </summary>
public interface ITraceManager
{
	/// <summary>Whether timers started now record anything.</summary>
	bool IsEnabled { get; set; }

	/// <summary>Enables tracing (Debug builds only).</summary>
	void Start();

	/// <summary>Disables tracing (Debug builds only).</summary>
	void Stop();

	/// <summary>Discards every recorded timing.</summary>
	void Clear();

	/// <summary>Writes the recorded timings to the configured trace output.</summary>
	void OutputTrace();

	/// <summary>
	/// Creates a timer whose timings are named <c>{prefix}::{name}</c> and recorded by this manager.
	/// </summary>
	/// <param name="prefix">The prefix for every timing started from the returned timer, usually a type name.</param>
	ITraceTimer CreateTimer(string prefix);
}
