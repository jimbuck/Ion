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
}
