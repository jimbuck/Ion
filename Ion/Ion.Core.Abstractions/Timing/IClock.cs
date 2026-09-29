namespace Ion;

/// <summary>
/// The time source of the game loop. The loop never reads the system time directly, so a test or a deterministic
/// replay can substitute its own clock (see <c>ManualClock</c> and <c>FixedStepClock</c> in <c>Ion.Core</c>).
/// </summary>
public interface IClock
{
	/// <summary>
	/// The time elapsed since the clock started. Reading it has no side effects.
	/// </summary>
	TimeSpan Elapsed { get; }

	/// <summary>
	/// <see cref="Elapsed"/> in seconds.
	/// </summary>
	double Seconds => Elapsed.TotalSeconds;

	/// <summary>
	/// Called by the game loop exactly once at the start of every frame; returns the frame's start time.
	/// Real-time clocks return <see cref="Elapsed"/>; virtual clocks may advance first (a fixed-step clock advances by
	/// exactly one step per call).
	/// </summary>
	TimeSpan NextFrame();

	/// <summary>
	/// Blocks the calling thread for <paramref name="duration"/> of this clock's time. Used by the game loop for frame
	/// pacing. Real-time clocks wait precisely; virtual clocks return immediately (and may advance their time instead).
	/// </summary>
	/// <param name="duration">How long to wait. Zero or negative durations return immediately.</param>
	void Sleep(TimeSpan duration);
}
