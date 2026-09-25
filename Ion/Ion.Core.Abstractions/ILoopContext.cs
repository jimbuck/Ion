namespace Ion;

/// <summary>
/// The stage of the game loop that is currently running.
/// </summary>
public enum GameLoopStage
{
	/// <summary>No stage is running (between frames, or no game loop is driving the application).</summary>
	None = 0,
	/// <summary>The Init stage, once before the first frame.</summary>
	Init,
	/// <summary>The First stage, at the start of every frame.</summary>
	First,
	/// <summary>A FixedUpdate step. A frame runs zero or more of them.</summary>
	FixedUpdate,
	/// <summary>The Update stage, once per frame.</summary>
	Update,
	/// <summary>The Render stage, once per frame.</summary>
	Render,
	/// <summary>The Last stage, at the end of every frame.</summary>
	Last,
	/// <summary>The Destroy stage, once after the last frame.</summary>
	Destroy,
}

/// <summary>
/// Read-only view of where the game loop is: the running stage, the frame and how many fixed steps have started.
/// Registered as a singleton by <c>IonApplication.CreateBuilder</c> and kept up to date by the game loop, which sets
/// <see cref="Stage"/> before invoking each stage. Engine services use it to give fixed-step and per-frame consumers the
/// right view of per-frame state (see <see cref="IInputState"/> and <see cref="IEventListener"/>) without any new API
/// for callers.
/// </summary>
public interface ILoopContext
{
	/// <summary>
	/// The stage that is currently running, or <see cref="GameLoopStage.None"/> between frames.
	/// </summary>
	GameLoopStage Stage { get; }

	/// <summary>
	/// The index of the current (or, between frames, the next) frame; the same value as <see cref="GameTime.Frame"/>.
	/// </summary>
	uint Frame { get; }

	/// <summary>
	/// The number of FixedUpdate steps started so far. While a FixedUpdate step runs it is that step's one-based index;
	/// between steps it is the index of the last step that ran (zero before the first).
	/// </summary>
	long FixedStepCount { get; }
}
