using System.Numerics;
using Ion.Extensions.Graphics;

namespace Ion;

/// <summary>
/// Veldrid implementation of <see cref="IInputState"/>. Each <see cref="Step"/> feeds the window's latest
/// input snapshot into an <see cref="InputTracker"/>, which holds the actual state.
/// </summary>
internal class InputState : IInputState
{
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly InputTracker _tracker = new();

	public Vector2 MousePosition { get; private set; } = Vector2.Zero;

	public float WheelDelta { get; private set; } = 0;

	public InputState(Window window, IEventListener events)
	{
		_window = window;
		_events = events;
	}

	public void Step()
	{
		var snapshot = _window.InputSnapshot;

		_tracker.BeginFrame();

		MousePosition = snapshot?.MousePosition ?? Vector2.Zero;
		WheelDelta = snapshot?.WheelDelta ?? 0;

		if (snapshot is not null)
		{
			var keyEvents = snapshot.KeyEvents;
			for (var i = 0; i < keyEvents.Count; i++)
			{
				var k = keyEvents[i];
				_tracker.OnKey((Key)k.Key, k.Down, k.Repeat, (ModifierKeys)k.Modifiers);
			}

			var mouseEvents = snapshot.MouseEvents;
			for (var i = 0; i < mouseEvents.Count; i++)
			{
				var m = mouseEvents[i];
				_tracker.OnMouseButton((MouseButton)m.MouseButton, m.Down);
			}
		}

		// Key up events that happen while another window has focus never reach us, so forget held keys.
		if (_events.On<WindowFocusLostEvent>()) _tracker.ReleaseAll();
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

	public void SetMousePosition(Vector2 position)
	{
		_window.Sdl2Window?.SetMousePosition(position);
	}

	public void SetMousePosition(int x, int y)
	{
		_window.Sdl2Window?.SetMousePosition(x, y);
	}
}
