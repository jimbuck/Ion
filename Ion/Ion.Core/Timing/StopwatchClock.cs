using System.Diagnostics;

namespace Ion;

/// <summary>
/// The default <see cref="IClock"/>: real time from <see cref="Stopwatch"/>, with a precise <see cref="Sleep"/> that
/// sleeps the thread for the bulk of the wait and spins for the final <see cref="SpinThreshold"/>.
/// </summary>
public sealed class StopwatchClock : IClock
{
	/// <summary>
	/// The tail of every <see cref="Sleep"/> that is spun rather than slept, to absorb the scheduler's wake-up jitter.
	/// </summary>
	public static readonly TimeSpan SpinThreshold = TimeSpan.FromMilliseconds(1);

	private static readonly long SpinThresholdTicks = (long)(SpinThreshold.TotalSeconds * Stopwatch.Frequency);

	private readonly long _start = Stopwatch.GetTimestamp();

	/// <inheritdoc/>
	public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);

	/// <inheritdoc/>
	public TimeSpan NextFrame() => Elapsed;

	/// <inheritdoc/>
	public void Sleep(TimeSpan duration)
	{
		if (duration <= TimeSpan.Zero) return;

		var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

		// Sleep for everything except the last SpinThreshold; Thread.Sleep only guarantees "at least", so wake early.
		var sleepTicks = deadline - Stopwatch.GetTimestamp() - SpinThresholdTicks;
		if (sleepTicks > 0)
		{
			var sleepMs = (int)(sleepTicks * 1000 / Stopwatch.Frequency);
			if (sleepMs > 0) Thread.Sleep(sleepMs);
		}

		// Spin the remainder, yielding to other ready threads when there are any.
		while (Stopwatch.GetTimestamp() < deadline)
		{
			if (!Thread.Yield()) Thread.SpinWait(20);
		}
	}
}
