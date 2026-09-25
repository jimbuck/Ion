using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// An <see cref="IInputState"/> driven by a script instead of a device. Calls such as <see cref="Press(Key, ModifierKeys)"/>
/// or <see cref="Click"/> are queued and applied, in order, at the start of the next frame (the First stage), so a key
/// pressed between frames reads as <see cref="Pressed(Key)"/> and <see cref="Down(Key)"/> for that frame, then only
/// <see cref="Down(Key)"/> until it is released. Registered by <c>AddNullGraphics</c>; resolve it as
/// <see cref="NullInputState"/> to script input.
/// </summary>
/// <remarks>
/// Every scripted event of a frame counts: a key pressed and released before the same frame (or <see cref="Tap"/>) reports
/// both <c>Pressed</c> and <c>Released</c> for that frame and is not <c>Down</c> afterwards. The scripting methods are
/// thread-safe, so a test may script input while the game loop runs on another thread.
/// </remarks>
public sealed class NullInputState : IInputState
{
	private enum PendingKind { Key, Button, MousePosition, Wheel, ReleaseAll }

	private readonly record struct Pending(PendingKind Kind, Key Key = default, MouseButton Button = default, bool Down = false, ModifierKeys Modifiers = ModifierKeys.None, Vector2 Value = default);

	private readonly Lock _lock = new();
	private readonly List<Pending> _pending = [];
	private readonly InputTracker _tracker = new();

	public Vector2 MousePosition { get; private set; } = Vector2.Zero;

	public float WheelDelta { get; private set; }

	/// <summary>
	/// Queues a key press (not a repeat) for the next frame.
	/// </summary>
	public void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _queue(new Pending(PendingKind.Key, Key: key, Down: true, Modifiers: modifiers));

	/// <summary>
	/// Queues a key release for the next frame.
	/// </summary>
	public void Release(Key key, ModifierKeys modifiers = ModifierKeys.None) => _queue(new Pending(PendingKind.Key, Key: key, Down: false, Modifiers: modifiers));

	/// <summary>
	/// Queues a press and a release of <paramref name="key"/> within the next frame.
	/// </summary>
	public void Tap(Key key, ModifierKeys modifiers = ModifierKeys.None)
	{
		Press(key, modifiers);
		Release(key, modifiers);
	}

	/// <summary>
	/// Queues a mouse button press for the next frame. The button stays down until <see cref="Release(MouseButton)"/>.
	/// </summary>
	public void Press(MouseButton button) => _queue(new Pending(PendingKind.Button, Button: button, Down: true));

	/// <summary>
	/// Queues a mouse button release for the next frame.
	/// </summary>
	public void Release(MouseButton button) => _queue(new Pending(PendingKind.Button, Button: button, Down: false));

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
	public void Scroll(float delta) => _queue(new Pending(PendingKind.Wheel, Value: new Vector2(delta, 0)));

	/// <summary>
	/// Queues releasing every held key and button without a <c>Released</c> edge, as when the window loses focus.
	/// </summary>
	public void ReleaseAll() => _queue(new Pending(PendingKind.ReleaseAll));

	/// <summary>
	/// Moves the mouse at the start of the next frame, like the Veldrid backend warping the cursor.
	/// </summary>
	public void SetMousePosition(Vector2 position) => _queue(new Pending(PendingKind.MousePosition, Value: position));

	/// <inheritdoc cref="SetMousePosition(Vector2)"/>
	public void SetMousePosition(int x, int y) => SetMousePosition(new Vector2(x, y));

	/// <summary>
	/// Starts a new input frame: clears last frame's edges and applies the queued script. Called by the input system in
	/// the First stage; tests that drive the input state without a game loop may call it directly.
	/// </summary>
	public void Step()
	{
		_tracker.BeginFrame();
		WheelDelta = 0;

		Pending[] pending;
		lock (_lock)
		{
			if (_pending.Count == 0) return;
			pending = [.. _pending];
			_pending.Clear();
		}

		foreach (var e in pending)
		{
			switch (e.Kind)
			{
				case PendingKind.Key:
					_tracker.OnKey(e.Key, e.Down, repeat: false, e.Modifiers);
					break;
				case PendingKind.Button:
					_tracker.OnMouseButton(e.Button, e.Down);
					break;
				case PendingKind.MousePosition:
					MousePosition = e.Value;
					break;
				case PendingKind.Wheel:
					WheelDelta += e.Value.X;
					break;
				case PendingKind.ReleaseAll:
					_tracker.ReleaseAll();
					break;
			}
		}
	}

	public bool Pressed(MouseButton btn) => _tracker.Pressed(btn);
	public bool Released(MouseButton btn) => _tracker.Released(btn);
	public bool Down(MouseButton btn) => _tracker.Down(btn);
	public bool Up(MouseButton btn) => !Down(btn);

	public bool Pressed(Key key) => _tracker.Pressed(key);
	public bool Pressed(Key key, ModifierKeys modifiers) => _tracker.Pressed(key, modifiers);

	public bool Released(Key key) => _tracker.Released(key);
	public bool Released(Key key, ModifierKeys modifiers) => _tracker.Released(key, modifiers);

	public bool Down(Key key) => _tracker.Down(key);
	public bool Up(Key key) => !Down(key);

	private void _queue(Pending pending)
	{
		lock (_lock) _pending.Add(pending);
	}
}
