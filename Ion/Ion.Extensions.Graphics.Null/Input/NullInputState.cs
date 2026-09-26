using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// An <see cref="IInputState"/> driven by a script instead of a device. Calls such as <see cref="Press(Key, ModifierKeys)"/>,
/// <see cref="Click"/>, <see cref="Type"/> or <see cref="Press(int, GamepadButton)"/> are queued and applied, in order, at the
/// start of the next frame (the First stage), so a key pressed between frames reads as <see cref="TrackedInputState.Pressed(Key)"/>
/// and <see cref="TrackedInputState.Down(Key)"/> for that frame, then only <c>Down</c> until it is released. Registered by
/// <c>AddNullGraphics</c>; resolve it as <see cref="NullInputState"/> to script input.
/// </summary>
/// <remarks>
/// <para>
/// Every scripted event of a frame counts: a key pressed and released before the same frame (or <see cref="Tap"/>) reports
/// both <c>Pressed</c> and <c>Released</c> for that frame and is not <c>Down</c> afterwards. The scripting methods are
/// thread-safe, so a test may script input while the game loop runs on another thread.
/// </para>
/// <para>
/// Edges and deltas follow the stage rules of <see cref="IInputState"/>: FixedUpdate systems see each scripted edge in
/// exactly one fixed step, even on frames that run no fixed step. While an input playback is attached
/// (<c>AddInputPlayback</c>) scripted input is ignored until the recording ends.
/// </para>
/// </remarks>
public sealed class NullInputState : TrackedInputState
{
	private readonly Lock _lock = new();
	private readonly List<InputEvent> _pending = [];
	private InputEvent[] _applying = new InputEvent[16];

	/// <summary>
	/// Creates a scripted input state with its own <see cref="InputTracker"/>.
	/// </summary>
	/// <param name="loop">
	/// The game loop context used to give FixedUpdate systems their own view of edges and deltas. Without it (a bare
	/// instance driven by <see cref="Step"/>) every query uses the per-frame view.
	/// </param>
	public NullInputState(ILoopContext? loop = null) : base(new InputTracker(loop))
	{
	}

	/// <summary>
	/// Creates a scripted input state over <paramref name="tracker"/> (the application's shared tracker, which recording
	/// and playback attach to).
	/// </summary>
	public NullInputState(InputTracker tracker) : base(tracker)
	{
	}

	/// <summary>
	/// Queues a key press (not a repeat) for the next frame.
	/// </summary>
	public void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _queue(InputEvent.ForKey(key, down: true, modifiers: modifiers));

	/// <summary>
	/// Queues a key release for the next frame.
	/// </summary>
	public void Release(Key key, ModifierKeys modifiers = ModifierKeys.None) => _queue(InputEvent.ForKey(key, down: false, modifiers: modifiers));

	/// <summary>
	/// Queues a press and a release of <paramref name="key"/> within the next frame.
	/// </summary>
	public void Tap(Key key, ModifierKeys modifiers = ModifierKeys.None)
	{
		Press(key, modifiers);
		Release(key, modifiers);
	}

	/// <summary>
	/// Queues a key repeat (the key held down long enough to auto-repeat): marks it down without a <c>Pressed</c> edge.
	/// </summary>
	public void Repeat(Key key, ModifierKeys modifiers = ModifierKeys.None) => _queue(InputEvent.ForKey(key, down: true, repeat: true, modifiers: modifiers));

	/// <summary>
	/// Queues a mouse button press for the next frame. The button stays down until <see cref="Release(MouseButton)"/>.
	/// </summary>
	public void Press(MouseButton button) => _queue(InputEvent.ForMouseButton(button, true));

	/// <summary>
	/// Queues a mouse button release for the next frame.
	/// </summary>
	public void Release(MouseButton button) => _queue(InputEvent.ForMouseButton(button, false));

	/// <summary>
	/// Queues a press and a release of <paramref name="button"/> within the next frame, like a quick click.
	/// </summary>
	public void Click(MouseButton button = MouseButton.Left)
	{
		Press(button);
		Release(button);
	}

	/// <summary>
	/// Queues a scroll of <paramref name="delta"/> for the next frame (deltas queued before one frame add up).
	/// </summary>
	public void Scroll(float delta) => _queue(InputEvent.ForWheel(delta));

	/// <summary>
	/// Queues <paramref name="text"/> as text input for the next frame: it becomes <see cref="TrackedInputState.Text"/>
	/// (text queued before one frame is concatenated). Only text: no key events are generated.
	/// </summary>
	public void Type(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		lock (_lock)
		{
			foreach (var c in text) _pending.Add(InputEvent.ForText(c));
		}
	}

	/// <summary>
	/// Queues releasing every held key and button without a <c>Released</c> edge, as when the window loses focus.
	/// </summary>
	public void ReleaseAll() => _queue(InputEvent.ForReleaseAll());

	/// <summary>
	/// Queues connecting a gamepad to slot <paramref name="index"/> (0 to <see cref="InputTracker.MaxGamepads"/> minus one).
	/// Scripting a button or axis of a slot connects it too.
	/// </summary>
	public void ConnectGamepad(int index = 0) => _queue(InputEvent.ForGamepadConnection(index, true));

	/// <summary>
	/// Queues disconnecting the gamepad in slot <paramref name="index"/>: its buttons are released without edges and its
	/// axes return to zero.
	/// </summary>
	public void DisconnectGamepad(int index = 0) => _queue(InputEvent.ForGamepadConnection(index, false));

	/// <summary>
	/// Queues a gamepad button press for the next frame.
	/// </summary>
	public void Press(int gamepad, GamepadButton button) => _queue(InputEvent.ForGamepadButton(gamepad, button, true));

	/// <summary>
	/// Queues a gamepad button release for the next frame.
	/// </summary>
	public void Release(int gamepad, GamepadButton button) => _queue(InputEvent.ForGamepadButton(gamepad, button, false));

	/// <summary>
	/// Queues a press and a release of a gamepad button within the next frame.
	/// </summary>
	public void Tap(int gamepad, GamepadButton button)
	{
		Press(gamepad, button);
		Release(gamepad, button);
	}

	/// <summary>
	/// Queues setting a gamepad axis to <paramref name="value"/> (raw, before the dead zone) for the next frame. It keeps
	/// that value until set again.
	/// </summary>
	public void SetAxis(int gamepad, GamepadAxis axis, float value) => _queue(InputEvent.ForGamepadAxis(gamepad, axis, value));

	/// <summary>
	/// Queues setting both axes of the left stick.
	/// </summary>
	public void SetLeftStick(int gamepad, Vector2 value)
	{
		SetAxis(gamepad, GamepadAxis.LeftX, value.X);
		SetAxis(gamepad, GamepadAxis.LeftY, value.Y);
	}

	/// <summary>
	/// Queues setting both axes of the right stick.
	/// </summary>
	public void SetRightStick(int gamepad, Vector2 value)
	{
		SetAxis(gamepad, GamepadAxis.RightX, value.X);
		SetAxis(gamepad, GamepadAxis.RightY, value.Y);
	}

	/// <summary>
	/// Queues a finger going down at <paramref name="position"/> (window coordinates) for the next frame.
	/// </summary>
	/// <param name="id">The touch id, unique among the scripted touches that are down.</param>
	/// <param name="position">Where the finger goes down.</param>
	public void TouchDown(int id, Vector2 position) => _queue(InputEvent.ForTouch(id, TouchPhase.Began, position));

	/// <summary>
	/// Queues moving the finger <paramref name="id"/> to <paramref name="position"/> for the next frame.
	/// </summary>
	public void TouchMove(int id, Vector2 position) => _queue(InputEvent.ForTouch(id, TouchPhase.Moved, position));

	/// <summary>
	/// Queues lifting the finger <paramref name="id"/> at <paramref name="position"/> for the next frame.
	/// </summary>
	public void TouchUp(int id, Vector2 position) => _queue(InputEvent.ForTouch(id, TouchPhase.Ended, position));

	/// <summary>
	/// Queues a quick tap at <paramref name="position"/>: the finger goes down and up within the next frame, which reports
	/// one touch that is both <see cref="TouchPoint.Pressed"/> and <see cref="TouchPoint.Released"/>.
	/// </summary>
	public void TouchTap(int id, Vector2 position)
	{
		TouchDown(id, position);
		TouchUp(id, position);
	}

	/// <summary>
	/// Moves the mouse at the start of the next frame, like a windowed backend warping the cursor. The movement counts
	/// towards <see cref="TrackedInputState.MouseDelta"/>.
	/// </summary>
	public override void SetMousePosition(Vector2 position) => _queue(InputEvent.ForMouseMove(position));

	/// <summary>
	/// Starts a new input frame: clears last frame's edges and applies the queued script. Called by the input system in
	/// the First stage; tests that drive the input state without a game loop may call it directly.
	/// </summary>
	public void Step()
	{
		Tracker.BeginFrame();

		int count;
		lock (_lock)
		{
			count = _pending.Count;
			if (count == 0) return;
			if (_applying.Length < count) _applying = new InputEvent[Math.Max(count, _applying.Length * 2)];
			_pending.CopyTo(_applying);
			_pending.Clear();
		}

		for (var i = 0; i < count; i++) _applying[i].ApplyTo(Tracker);
	}

	private void _queue(InputEvent e)
	{
		lock (_lock) _pending.Add(e);
	}
}
