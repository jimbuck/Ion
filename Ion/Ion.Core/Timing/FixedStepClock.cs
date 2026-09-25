namespace Ion;

/// <summary>
/// A deterministic <see cref="IClock"/> for replays, headless simulation and tests: every frame lasts exactly
/// <see cref="Step"/>, however long it really took. Time only moves when the game loop starts a frame
/// (<see cref="NextFrame"/>) or when <see cref="Advance"/> is called, and <see cref="Sleep"/> never blocks.
/// </summary>
public sealed class FixedStepClock : IClock
{
	private long _steps;

	/// <summary>
	/// Creates a clock that advances by <paramref name="step"/> per frame.
	/// </summary>
	/// <param name="step">The duration of one frame. Must be positive.</param>
	public FixedStepClock(TimeSpan step)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
		Step = step;
	}

	/// <summary>
	/// The duration of one frame.
	/// </summary>
	public TimeSpan Step { get; }

	/// <inheritdoc/>
	public TimeSpan Elapsed => Step * _steps;

	/// <summary>
	/// Advances the clock by exactly one <see cref="Step"/>.
	/// </summary>
	public void Advance() => _steps++;

	/// <summary>
	/// Advances the clock by one <see cref="Step"/> and returns the new time.
	/// </summary>
	public TimeSpan NextFrame()
	{
		Advance();
		return Elapsed;
	}

	/// <summary>
	/// Does nothing: virtual time does not pass while the loop waits, so frame pacing never blocks.
	/// </summary>
	public void Sleep(TimeSpan duration) { }
}
