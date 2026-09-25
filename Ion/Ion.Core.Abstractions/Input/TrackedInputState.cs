using System.Numerics;

namespace Ion;

/// <summary>
/// Base class for <see cref="IInputState"/> implementations: every query is answered by an <see cref="InputTracker"/>,
/// which the backend feeds once per frame. A backend only adds the device plumbing and <see cref="SetMousePosition(Vector2)"/>.
/// </summary>
public abstract class TrackedInputState : IInputState
{
	/// <summary>
	/// Creates an input state over <paramref name="tracker"/>.
	/// </summary>
	protected TrackedInputState(InputTracker tracker)
	{
		ArgumentNullException.ThrowIfNull(tracker);
		Tracker = tracker;
	}

	/// <summary>
	/// The tracker holding the state. Feed it with <see cref="InputTracker.BeginFrame"/> and the <see cref="IInputEventSink"/>
	/// methods.
	/// </summary>
	public InputTracker Tracker { get; }

	/// <inheritdoc/>
	public Vector2 MousePosition => Tracker.MousePosition;

	/// <inheritdoc/>
	public float WheelDelta => Tracker.WheelDelta;

	/// <inheritdoc/>
	public Vector2 MouseDelta => Tracker.MouseDelta;

	/// <inheritdoc/>
	public ReadOnlySpan<char> Text => Tracker.Text;

	/// <inheritdoc/>
	public ModifierKeys Modifiers => Tracker.Modifiers;

	/// <inheritdoc/>
	public IReadOnlyList<IGamepadState> Gamepads => Tracker.Gamepads;

	/// <inheritdoc/>
	public IGamepadState Gamepad(int index) => Tracker.Gamepad(index);

	/// <inheritdoc/>
	public bool Down(Key key) => Tracker.Down(key);

	/// <inheritdoc/>
	public bool Down(MouseButton btn) => Tracker.Down(btn);

	/// <inheritdoc/>
	public bool Pressed(Key key) => Tracker.Pressed(key);

	/// <inheritdoc/>
	public bool Pressed(Key key, ModifierKeys modifiers) => Tracker.Pressed(key, modifiers);

	/// <inheritdoc/>
	public bool Pressed(MouseButton btn) => Tracker.Pressed(btn);

	/// <inheritdoc/>
	public bool Released(Key key) => Tracker.Released(key);

	/// <inheritdoc/>
	public bool Released(Key key, ModifierKeys modifiers) => Tracker.Released(key, modifiers);

	/// <inheritdoc/>
	public bool Released(MouseButton btn) => Tracker.Released(btn);

	/// <inheritdoc/>
	public bool Up(Key key) => !Tracker.Down(key);

	/// <inheritdoc/>
	public bool Up(MouseButton btn) => !Tracker.Down(btn);

	/// <inheritdoc/>
	public abstract void SetMousePosition(Vector2 position);

	/// <inheritdoc/>
	public void SetMousePosition(int x, int y) => SetMousePosition(new Vector2(x, y));
}
