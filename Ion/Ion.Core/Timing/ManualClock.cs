namespace Ion;

/// <summary>
/// A test-controlled <see cref="IClock"/>. Time only moves when <see cref="Advance"/> is called, or when the game loop
/// asks it to <see cref="Sleep"/> and <see cref="AdvanceOnSleep"/> is set. Every sleep request is recorded, so tests can
/// assert frame pacing without really sleeping.
/// </summary>
public sealed class ManualClock : IClock
{
	/// <summary>
	/// Creates a clock at <paramref name="start"/> (zero by default).
	/// </summary>
	public ManualClock(TimeSpan start = default)
	{
		Elapsed = start;
	}

	/// <inheritdoc/>
	public TimeSpan Elapsed { get; private set; }

	/// <summary>
	/// When <see langword="true"/> (the default), <see cref="Sleep"/> advances the clock by the requested duration, as if
	/// the wait really happened. When <see langword="false"/>, sleep requests are only recorded.
	/// </summary>
	public bool AdvanceOnSleep { get; set; } = true;

	/// <summary>The number of <see cref="Sleep"/> calls with a positive duration.</summary>
	public int SleepCount { get; private set; }

	/// <summary>The duration passed to the most recent <see cref="Sleep"/> call with a positive duration.</summary>
	public TimeSpan LastSleep { get; private set; }

	/// <summary>The sum of every positive duration passed to <see cref="Sleep"/>.</summary>
	public TimeSpan TotalSleep { get; private set; }

	/// <summary>
	/// Moves the clock forward by <paramref name="duration"/>.
	/// </summary>
	/// <param name="duration">The time to add. Must not be negative.</param>
	public void Advance(TimeSpan duration)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
		Elapsed += duration;
	}

	/// <inheritdoc/>
	public TimeSpan NextFrame() => Elapsed;

	/// <summary>
	/// Records the request and, when <see cref="AdvanceOnSleep"/> is set, advances the clock. Never blocks.
	/// </summary>
	public void Sleep(TimeSpan duration)
	{
		if (duration <= TimeSpan.Zero) return;

		SleepCount++;
		LastSleep = duration;
		TotalSleep += duration;
		if (AdvanceOnSleep) Elapsed += duration;
	}
}
