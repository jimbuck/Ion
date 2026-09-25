using System.Numerics;

namespace Ion;

/// <summary>
/// Keyboard and mouse state, updated once per frame at the start of the First stage.
/// </summary>
/// <remarks>
/// <para>
/// Level queries (<see cref="Down(Key)"/>, <see cref="Up(Key)"/>, <see cref="MousePosition"/>) always return the
/// current state. Edge and delta queries (<see cref="Pressed(Key)"/>, <see cref="Released(Key)"/>, their mouse button
/// and modifier overloads, <see cref="WheelDelta"/> and <see cref="MouseDelta"/>) depend on the stage that asks:
/// </para>
/// <list type="bullet">
/// <item><description>
/// From First, Update, Render and Last they describe the current frame: an edge is true for exactly one frame and a delta
/// is the movement since the previous frame.
/// </description></item>
/// <item><description>
/// From FixedUpdate they describe everything since the previous fixed step: an edge is true in exactly one fixed step
/// (the first one to run on or after the frame it happened in) and a delta is the movement accumulated since then. This
/// holds whatever the ratio of <see cref="GameConfig.MaxFPS"/> to <see cref="GameConfig.FixedUpdateRate"/>: an edge on a
/// frame that runs no fixed step is carried to the next fixed step, and a frame that runs several fixed steps shows it
/// only to the first.
/// </description></item>
/// </list>
/// <para>
/// So a click is seen exactly once by a FixedUpdate system and exactly once by an Update system. The stage is read from
/// <see cref="ILoopContext"/>; when no game loop is running every query uses the per-frame view.
/// </para>
/// </remarks>
public interface IInputState
{
	/// <summary>The mouse position in window coordinates.</summary>
	Vector2 MousePosition { get; }

	/// <summary>The wheel movement of this frame (from FixedUpdate: since the previous fixed step).</summary>
	float WheelDelta { get; }

	/// <summary>The mouse movement of this frame (from FixedUpdate: since the previous fixed step).</summary>
	Vector2 MouseDelta { get; }

	/// <summary>True while <paramref name="key"/> is held.</summary>
	bool Down(Key key);
	/// <summary>True while <paramref name="btn"/> is held.</summary>
	bool Down(MouseButton btn);
	/// <summary>True when <paramref name="key"/> went down this frame (from FixedUpdate: since the previous fixed step). Repeats do not count.</summary>
	bool Pressed(Key key);
	/// <summary>As <see cref="Pressed(Key)"/>, and at least one of <paramref name="modifiers"/> was held.</summary>
	bool Pressed(Key key, ModifierKeys modifiers);
	/// <summary>True when <paramref name="btn"/> went down this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Pressed(MouseButton btn);
	/// <summary>True when <paramref name="key"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Released(Key key);
	/// <summary>As <see cref="Released(Key)"/>, and at least one of <paramref name="modifiers"/> was held.</summary>
	bool Released(Key key, ModifierKeys modifiers);
	/// <summary>True when <paramref name="btn"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Released(MouseButton btn);
	/// <summary>True while <paramref name="key"/> is not held.</summary>
	bool Up(Key key);
	/// <summary>True while <paramref name="btn"/> is not held.</summary>
	bool Up(MouseButton btn);

	/// <summary>Moves the mouse cursor.</summary>
	void SetMousePosition(Vector2 position);
	/// <summary>Moves the mouse cursor.</summary>
	void SetMousePosition(int x, int y);
}
