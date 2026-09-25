using System.Numerics;
using Ion.Extensions.Graphics;

namespace Ion;

/// <summary>
/// Veldrid implementation of <see cref="IInputState"/>. Each <see cref="Step"/> feeds the window's latest input snapshot
/// (key, mouse button, wheel, mouse position and text events) into the application's <see cref="InputTracker"/>, which
/// holds the actual state and selects the per-frame or fixed-step view from <see cref="ILoopContext"/> (see
/// <see cref="IInputState"/>).
/// </summary>
/// <remarks>
/// Veldrid's SDL2 input snapshot carries no game controller events, so this backend reports no gamepads
/// (<see cref="IInputState.Gamepads"/> is empty); the Silk.NET backend of Stage 4 will feed real gamepads into the same
/// tracker.
/// </remarks>
internal sealed class InputState : TrackedInputState
{
	private readonly Window _window;
	private EventReader<WindowFocusLostEvent> _focusLost;

	public InputState(Window window, IEvents events, InputTracker tracker) : base(tracker)
	{
		_window = window;
		_focusLost = events.Reader<WindowFocusLostEvent>();
	}

	public InputState(Window window, IEvents events, ILoopContext? loop = null) : this(window, events, new InputTracker(loop))
	{
	}

	public void Step()
	{
		var snapshot = _window.InputSnapshot;
		var tracker = Tracker;

		tracker.BeginFrame();

		if (snapshot is not null)
		{
			tracker.OnMouseMove(snapshot.MousePosition);
			if (snapshot.WheelDelta != 0) tracker.OnWheel(snapshot.WheelDelta);

			var keyEvents = snapshot.KeyEvents;
			for (var i = 0; i < keyEvents.Count; i++)
			{
				var k = keyEvents[i];
				tracker.OnKey((Key)k.Key, k.Down, k.Repeat, (ModifierKeys)k.Modifiers);
			}

			var mouseEvents = snapshot.MouseEvents;
			for (var i = 0; i < mouseEvents.Count; i++)
			{
				var m = mouseEvents[i];
				tracker.OnMouseButton((MouseButton)m.MouseButton, m.Down);
			}

			var chars = snapshot.KeyCharPresses;
			for (var i = 0; i < chars.Count; i++) tracker.OnText(chars[i]);
		}

		// Key up events that happen while another window has focus never reach us, so forget held keys.
		if (_focusLost.Read().Length > 0) tracker.ReleaseAll();
	}

	public override void SetMousePosition(Vector2 position)
	{
		_window.Sdl2Window?.SetMousePosition(position);
	}
}
