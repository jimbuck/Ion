namespace Ion;

/// <summary>
/// The <see cref="ILoopContext"/> written by <see cref="Core.GameLoop"/>. Registered as a singleton (and as
/// <see cref="ILoopContext"/>) by <see cref="IonApplication.CreateBuilder()"/>.
/// </summary>
public sealed class GameLoopContext : ILoopContext
{
	/// <inheritdoc/>
	public GameLoopStage Stage { get; internal set; }

	/// <inheritdoc/>
	public uint Frame { get; internal set; }

	/// <inheritdoc/>
	public long FixedStepCount { get; internal set; }

	/// <summary>
	/// The game loop that writes this context (the last one created over it), or null before one is built. Tools such as
	/// the remote protocol use it to read the running schedule and to stop the loop.
	/// </summary>
	public Core.GameLoop? Loop { get; internal set; }

	/// <summary>The schedule the loop runs (<see cref="Core.GameLoop.Schedule"/>), or null before one is set.</summary>
	public Schedule? Schedule => Loop?.Schedule;
}
