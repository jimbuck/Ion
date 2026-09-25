using System.Numerics;
using Ion.Extensions.Graphics;

namespace Ion;

/// <summary>
/// Veldrid implementation of <see cref="IInputState"/>. Each <see cref="Step"/> feeds the window's latest
/// input snapshot into an <see cref="InputTracker"/>, which holds the actual state and selects the per-frame or
/// fixed-step view from <see cref="ILoopContext"/> (see <see cref="IInputState"/>).
/// </summary>
internal class InputState : IInputState
{
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly InputTracker _tracker;

	public Vector2 MousePosition => _tracker.MousePosition;

	public float WheelDelta => _tracker.WheelDelta;

	public Vector2 MouseDelta => _tracker.MouseDelta;

	public InputState(Window window, IEventListener events, ILoopContext? loop = null)
	{
		_window = window;
		_events = events;
		_tracker = new InputTracker(loop);
	}

	public void Step()
	{
		var snapshot = _window.InputSnapshot;

		_tracker.BeginFrame();

		if (snapshot is not null)
		{
			_tracker.OnMouseMove(snapshot.MousePosition);
			if (snapshot.WheelDelta != 0) _tracker.OnWheel(snapshot.WheelDelta);

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
