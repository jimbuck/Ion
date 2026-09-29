namespace Ion;

/// <summary>
/// Core game settings, bound from the <c>Ion</c> configuration section.
/// </summary>
public class GameConfig
{
	/// <summary>The default <see cref="FixedUpdateRate"/>, 60 Hz.</summary>
	public const double DefaultFixedUpdateRate = 60;

	/// <summary>The default <see cref="MaxFrameTime"/>, 100 ms.</summary>
	public static readonly TimeSpan DefaultMaxFrameTime = TimeSpan.FromMilliseconds(100);

	/// <summary>
	/// The game's title. Used for the window title and the per-game user data folder.
	/// </summary>
	public string Title { get; set; } = "Ion";

	/// <summary>
	/// The maximum number of rendered frames per second. The game loop waits at the end of each frame so that frames
	/// are not produced faster than this rate. <c>0</c> (or any value below 1) means uncapped. Ignored when
	/// <see cref="VSync"/> is <see langword="true"/>, because the swapchain paces presentation instead.
	/// This setting does not affect the simulation rate; see <see cref="FixedUpdateRate"/>.
	/// </summary>
	public int MaxFPS { get; set; } = 300;

	/// <summary>
	/// The rate, in steps per second (Hz), of the <c>FixedUpdate</c> stage. The fixed step
	/// (<c>FixedGameTime.Delta</c>) is <c>1 / FixedUpdateRate</c> seconds regardless of the render rate: each frame adds
	/// its (clamped) duration to an accumulator and runs as many fixed steps as fit, carrying the remainder to the next
	/// frame. <c>GameTime.Alpha</c> is the remainder divided by the fixed step, for interpolating rendered state.
	/// Values that are not positive fall back to <see cref="DefaultFixedUpdateRate"/>.
	/// </summary>
	public double FixedUpdateRate { get; set; } = DefaultFixedUpdateRate;

	/// <summary>
	/// The longest frame duration the game loop accounts for. Longer frames (a breakpoint, a window drag, a loading
	/// hitch) are clamped to this value so the fixed-step accumulator does not try to catch up with a burst of steps.
	/// Values that are not positive fall back to <see cref="DefaultMaxFrameTime"/> (100 ms).
	/// </summary>
	public TimeSpan MaxFrameTime { get; set; } = DefaultMaxFrameTime;

	/// <summary>
	/// Whether presentation is synchronised with the display refresh. When <see langword="true"/> the game loop does no
	/// frame pacing of its own (<see cref="MaxFPS"/> is ignored) and relies on the graphics layer to block on present.
	/// </summary>
	public bool VSync { get; set; }
}
