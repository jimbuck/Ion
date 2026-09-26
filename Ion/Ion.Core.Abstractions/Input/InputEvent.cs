using System.Numerics;

namespace Ion;

/// <summary>
/// Receives raw input events in the order they happened. <see cref="InputTracker"/> implements it (a backend feeds its
/// device events into one), and so does an <see cref="IInputRecorder"/> (which the tracker feeds with every event it
/// applies). <see cref="InputPlayer"/> replays a recording into any sink.
/// </summary>
public interface IInputEventSink
{
	/// <summary>A key went down (<paramref name="repeat"/> for auto-repeat while held) or up, with the modifiers held at the time.</summary>
	void OnKey(Key key, bool down, bool repeat, ModifierKeys modifiers);

	/// <summary>A mouse button went down or up.</summary>
	void OnMouseButton(MouseButton button, bool down);

	/// <summary>The mouse moved to <paramref name="position"/> (window coordinates).</summary>
	void OnMouseMove(Vector2 position);

	/// <summary>The wheel moved by <paramref name="delta"/>.</summary>
	void OnWheel(float delta);

	/// <summary>A character of text input (after keyboard layout and IME processing).</summary>
	void OnText(char character);

	/// <summary>A gamepad was connected to or disconnected from slot <paramref name="index"/>.</summary>
	void OnGamepadConnected(int index, bool connected);

	/// <summary>A gamepad button went down or up.</summary>
	void OnGamepadButton(int index, GamepadButton button, bool down);

	/// <summary>A gamepad axis moved to <paramref name="value"/> (raw, before the dead zone).</summary>
	void OnGamepadAxis(int index, GamepadAxis axis, float value);

	/// <summary>Every held key and mouse button was released without an edge (the window lost focus).</summary>
	void ReleaseAll();

	/// <summary>
	/// A touch began, moved, ended or was cancelled at <paramref name="position"/> (window coordinates). The default
	/// implementation ignores it, for sinks written before touch input existed.
	/// </summary>
	/// <param name="id">The touch's id, unique among the touches down at the same time.</param>
	/// <param name="phase"><see cref="TouchPhase.Began"/>, <see cref="TouchPhase.Moved"/>, <see cref="TouchPhase.Ended"/> or <see cref="TouchPhase.Canceled"/>.</param>
	/// <param name="position">The position in window coordinates.</param>
	void OnTouch(int id, TouchPhase phase, Vector2 position)
	{
	}
}

/// <summary>
/// Observes the input events an <see cref="InputTracker"/> applies, frame by frame. Set it as
/// <see cref="InputTracker.Recorder"/>; <see cref="InputRecorder"/> writes them to a stream.
/// </summary>
public interface IInputRecorder : IInputEventSink
{
	/// <summary>Called at the start of every input frame, before that frame's events.</summary>
	void BeginFrame(uint frame);
}

/// <summary>
/// Supplies the input events of each frame instead of the device. Set it as <see cref="InputTracker.Playback"/>; while
/// <see cref="IsPlaying"/> is true the tracker ignores device events and applies only what <see cref="Play"/> feeds it.
/// </summary>
public interface IInputPlayback
{
	/// <summary>True until the end of the recording has been played.</summary>
	bool IsPlaying { get; }

	/// <summary>Applies the events recorded for <paramref name="frame"/> (and any earlier ones not applied yet) to <paramref name="sink"/>.</summary>
	void Play(uint frame, IInputEventSink sink);
}

/// <summary>
/// Attaches something (a recorder, a player) to the application's <see cref="InputTracker"/> when it is created.
/// Register implementations as <see cref="IInputTrackerHook"/> singletons; see
/// <see cref="InputServiceCollectionExtensions.AddInputRecording"/>.
/// </summary>
public interface IInputTrackerHook
{
	/// <summary>Attaches to <paramref name="tracker"/>.</summary>
	void Attach(InputTracker tracker);
}

/// <summary>
/// The kind of an <see cref="InputEvent"/>.
/// </summary>
public enum InputEventKind : byte
{
	/// <summary>Not an event.</summary>
	None = 0,
	/// <summary><see cref="IInputEventSink.OnKey"/>.</summary>
	Key,
	/// <summary><see cref="IInputEventSink.OnMouseButton"/>.</summary>
	MouseButton,
	/// <summary><see cref="IInputEventSink.OnMouseMove"/>.</summary>
	MouseMove,
	/// <summary><see cref="IInputEventSink.OnWheel"/>.</summary>
	Wheel,
	/// <summary><see cref="IInputEventSink.OnText"/>.</summary>
	Text,
	/// <summary><see cref="IInputEventSink.OnGamepadConnected"/>.</summary>
	GamepadConnection,
	/// <summary><see cref="IInputEventSink.OnGamepadButton"/>.</summary>
	GamepadButton,
	/// <summary><see cref="IInputEventSink.OnGamepadAxis"/>.</summary>
	GamepadAxis,
	/// <summary><see cref="IInputEventSink.ReleaseAll"/>.</summary>
	ReleaseAll,
	/// <summary><see cref="IInputEventSink.OnTouch"/>.</summary>
	Touch,
}

/// <summary>
/// One raw input event as a value: what <see cref="IInputEventSink"/> receives, for queues and recordings.
/// </summary>
/// <param name="Kind">The kind of event.</param>
/// <param name="Code">The <see cref="Key"/>, <see cref="MouseButton"/>, <see cref="GamepadButton"/>, <see cref="GamepadAxis"/>, <see cref="TouchPhase"/> or text character.</param>
/// <param name="Index">The gamepad slot, for gamepad events, or the touch id, for touch events.</param>
/// <param name="Down">Down (or connected) versus up (or disconnected).</param>
/// <param name="Repeat">Whether a key down is an auto-repeat.</param>
/// <param name="Modifiers">The modifiers held with a key event.</param>
/// <param name="Value">The mouse or touch position, the wheel delta (in X) or the axis value (in X).</param>
public readonly record struct InputEvent(
	InputEventKind Kind,
	int Code = 0,
	int Index = 0,
	bool Down = false,
	bool Repeat = false,
	ModifierKeys Modifiers = ModifierKeys.None,
	Vector2 Value = default)
{
	/// <summary>A key event.</summary>
	public static InputEvent ForKey(Key key, bool down, bool repeat = false, ModifierKeys modifiers = ModifierKeys.None) => new(InputEventKind.Key, (int)key, Down: down, Repeat: repeat, Modifiers: modifiers);

	/// <summary>A mouse button event.</summary>
	public static InputEvent ForMouseButton(MouseButton button, bool down) => new(InputEventKind.MouseButton, (int)button, Down: down);

	/// <summary>A mouse move event.</summary>
	public static InputEvent ForMouseMove(Vector2 position) => new(InputEventKind.MouseMove, Value: position);

	/// <summary>A wheel event.</summary>
	public static InputEvent ForWheel(float delta) => new(InputEventKind.Wheel, Value: new Vector2(delta, 0));

	/// <summary>A text input event.</summary>
	public static InputEvent ForText(char character) => new(InputEventKind.Text, character);

	/// <summary>A gamepad connection event.</summary>
	public static InputEvent ForGamepadConnection(int index, bool connected) => new(InputEventKind.GamepadConnection, Index: index, Down: connected);

	/// <summary>A gamepad button event.</summary>
	public static InputEvent ForGamepadButton(int index, GamepadButton button, bool down) => new(InputEventKind.GamepadButton, (int)button, index, down);

	/// <summary>A gamepad axis event.</summary>
	public static InputEvent ForGamepadAxis(int index, GamepadAxis axis, float value) => new(InputEventKind.GamepadAxis, (int)axis, index, Value: new Vector2(value, 0));

	/// <summary>A release-all (focus loss) event.</summary>
	public static InputEvent ForReleaseAll() => new(InputEventKind.ReleaseAll);

	/// <summary>A touch event.</summary>
	public static InputEvent ForTouch(int id, TouchPhase phase, Vector2 position) => new(InputEventKind.Touch, (int)phase, id, Value: position);

	/// <summary>Delivers this event to <paramref name="sink"/>.</summary>
	public void ApplyTo(IInputEventSink sink)
	{
		switch (Kind)
		{
			case InputEventKind.Key: sink.OnKey((Key)Code, Down, Repeat, Modifiers); break;
			case InputEventKind.MouseButton: sink.OnMouseButton((MouseButton)Code, Down); break;
			case InputEventKind.MouseMove: sink.OnMouseMove(Value); break;
			case InputEventKind.Wheel: sink.OnWheel(Value.X); break;
			case InputEventKind.Text: sink.OnText((char)Code); break;
			case InputEventKind.GamepadConnection: sink.OnGamepadConnected(Index, Down); break;
			case InputEventKind.GamepadButton: sink.OnGamepadButton(Index, (GamepadButton)Code, Down); break;
			case InputEventKind.GamepadAxis: sink.OnGamepadAxis(Index, (GamepadAxis)Code, Value.X); break;
			case InputEventKind.ReleaseAll: sink.ReleaseAll(); break;
			case InputEventKind.Touch: sink.OnTouch(Index, (TouchPhase)Code, Value); break;
		}
	}
}
