using System.Numerics;

namespace Ion;

/// <summary>
/// Keyboard, mouse, text and gamepad state, captured once per frame at the start of the First stage and read-only for the
/// rest of the frame.
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
/// <para>
/// Touch screens: <see cref="Touches"/> lists the fingers of the frame (the Silk.NET module maps SDL touch events into it).
/// </para>
/// <para>
/// Both backends keep their state in one shared <see cref="InputTracker"/> (bitsets, no per-frame allocation); see it for
/// the modifier and focus-loss rules. Gamepads: the Silk.NET windowing module feeds real ones (GLFW or SDL) and the headless
/// backend's <c>NullInputState</c> can script them. Input can be recorded and replayed with <c>AddInputRecording</c> and <c>AddInputPlayback</c>.
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

	/// <summary>
	/// The text typed this frame (from FixedUpdate: since the previous fixed step), after keyboard layout and IME
	/// processing. Only valid until the next frame starts.
	/// </summary>
	ReadOnlySpan<char> Text { get; }

	/// <summary>The modifiers held now, derived from the held modifier keys.</summary>
	ModifierKeys Modifiers { get; }

	/// <summary>The connected gamepads, by ascending slot index.</summary>
	IReadOnlyList<IGamepadState> Gamepads { get; }

	/// <summary>
	/// The gamepad in slot <paramref name="index"/> (0 to <see cref="InputTracker.MaxGamepads"/> minus one). Never null: a
	/// slot with no gamepad (or an out-of-range index) reports <see cref="IGamepadState.IsConnected"/> false and nothing held.
	/// </summary>
	IGamepadState Gamepad(int index);

	/// <summary>True while <paramref name="key"/> is held.</summary>
	bool Down(Key key);
	/// <summary>True while <paramref name="btn"/> is held.</summary>
	bool Down(MouseButton btn);
	/// <summary>True when <paramref name="key"/> went down this frame (from FixedUpdate: since the previous fixed step). Repeats do not count.</summary>
	bool Pressed(Key key);
	/// <summary>
	/// As <see cref="Pressed(Key)"/>, and at least one of <paramref name="modifiers"/> was held with the press (as reported
	/// with the key event). <see cref="ModifierKeys.None"/> never matches.
	/// </summary>
	bool Pressed(Key key, ModifierKeys modifiers);
	/// <summary>True when <paramref name="btn"/> went down this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Pressed(MouseButton btn);
	/// <summary>True when <paramref name="key"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Released(Key key);
	/// <summary>
	/// As <see cref="Released(Key)"/>, and at least one of <paramref name="modifiers"/> was held with the release.
	/// <see cref="ModifierKeys.None"/> never matches.
	/// </summary>
	bool Released(Key key, ModifierKeys modifiers);
	/// <summary>True when <paramref name="btn"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Released(MouseButton btn);
	/// <summary>True while <paramref name="key"/> is not held.</summary>
	bool Up(Key key);
	/// <summary>True while <paramref name="btn"/> is not held.</summary>
	bool Up(MouseButton btn);

	/// <summary>
	/// The touch points of this frame, in the order they began: every finger that is down, plus those that were lifted or
	/// cancelled this frame (<see cref="TouchPoint.Released"/>). Empty without a touch screen. Only valid until the next
	/// frame starts.
	/// </summary>
	/// <remarks>
	/// Touches have a per-frame view only: from FixedUpdate this is the same list as from Update, so a touch that begins
	/// on a frame that runs no fixed step is not seen as <see cref="TouchPoint.Pressed"/> by any fixed step. Read touches
	/// from First, Update or Render. The default implementation (for input states without touch support) is empty.
	/// </remarks>
	ReadOnlySpan<TouchPoint> Touches => [];

	/// <summary>
	/// Finds the touch with <paramref name="id"/> in <see cref="Touches"/>.
	/// </summary>
	/// <returns>True when a touch with that id is down or ended this frame.</returns>
	bool TryGetTouch(int id, out TouchPoint touch)
	{
		foreach (var t in Touches)
		{
			if (t.Id == id)
			{
				touch = t;
				return true;
			}
		}

		touch = default;
		return false;
	}

	/// <summary>Moves the mouse cursor.</summary>
	void SetMousePosition(Vector2 position);
	/// <summary>Moves the mouse cursor.</summary>
	void SetMousePosition(int x, int y);
}
