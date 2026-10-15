using System.Numerics;

namespace Ion;

/// <summary>
/// Where a <see cref="TouchPoint"/> is in its life, as of the current frame.
/// </summary>
public enum TouchPhase : byte
{
	/// <summary>Not a touch.</summary>
	None = 0,
	/// <summary>The finger went down this frame (and may have moved since).</summary>
	Began,
	/// <summary>The finger is down and moved this frame.</summary>
	Moved,
	/// <summary>The finger is down and did not move this frame.</summary>
	Stationary,
	/// <summary>The finger was lifted this frame. The touch is gone next frame.</summary>
	Ended,
	/// <summary>The system cancelled the touch this frame (for example a gesture took it, or the window lost focus). The touch is gone next frame.</summary>
	Canceled,
}

/// <summary>
/// One finger (or stylus) on a touch screen, as seen in the current frame. Read the frame's touches with
/// <see cref="IInputState.Touches"/>.
/// </summary>
/// <remarks>
/// Like keys, a touch that goes down and up within one frame is reported once, with <see cref="Pressed"/> and
/// <see cref="Released"/> both true, so a quick tap is never lost. A pointer consumer (a UI) can treat
/// <see cref="Pressed"/> as a button press at <see cref="StartPosition"/> and <see cref="Released"/> as the release at
/// <see cref="Position"/>.
/// </remarks>
/// <param name="Id">
/// The touch's id, stable from the frame it began to the frame it ended and unique among the touches held at the same
/// time. Ids are small (0 to <see cref="InputTracker.MaxTouches"/> minus one on the Silk.NET backend) and are reused after
/// a touch ends.
/// </param>
/// <param name="Position">The position in window coordinates (the same space as <see cref="IInputState.MousePosition"/>).</param>
/// <param name="Delta">The movement this frame.</param>
/// <param name="StartPosition">Where the touch began.</param>
/// <param name="Phase">The phase as of this frame: the last event applied, or <see cref="TouchPhase.Began"/> for a touch that began and moved this frame.</param>
/// <param name="Pressed">True when the touch began this frame.</param>
public readonly record struct TouchPoint(int Id, Vector2 Position, Vector2 Delta, Vector2 StartPosition, TouchPhase Phase, bool Pressed)
{
	/// <summary>True when the touch ended or was cancelled this frame.</summary>
	public bool Released => Phase is TouchPhase.Ended or TouchPhase.Canceled;

	/// <summary>True while the finger is down (the touch has not ended or been cancelled).</summary>
	public bool IsDown => !Released;
}
