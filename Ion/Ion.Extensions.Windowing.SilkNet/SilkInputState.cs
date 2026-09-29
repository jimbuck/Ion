using System.Numerics;

using Silk.NET.Input;
using Silk.NET.Windowing;

using Ion.Extensions.Graphics;

using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Ion.Extensions.Windowing;

/// <summary>
/// Silk.NET implementation of <see cref="IInputState"/>: keyboard, mouse, text and gamepads from <c>Silk.NET.Input</c>,
/// fed into the application's shared <see cref="InputTracker"/>.
/// </summary>
/// <remarks>
/// <para>
/// Silk.NET raises its input callbacks inside <c>DoEvents()</c>, which the window system calls at
/// <see cref="StageOrder.Window"/>, before the input system begins the tracker's frame at <see cref="StageOrder.Input"/>.
/// The callbacks therefore only queue <see cref="InputEvent"/> values; <see cref="Step"/> begins the tracker's frame and
/// applies them in order. Nothing is allocated per event.
/// </para>
/// <para>
/// Gamepads: GLFW exposes 16 slots up front and SDL adds devices as they connect; both are mapped onto the tracker's
/// first <see cref="InputTracker.MaxGamepads"/> slots by device index, with connection events. Silk.NET's own dead zone
/// is turned off because the tracker applies <see cref="InputConfig.GamepadDeadZone"/>.
/// </para>
/// <para>
/// Silk.NET does not flag key repeats, so a key down for a key that is already down is reported as a repeat.
/// </para>
/// <para>
/// Touch: on the SDL platform, SDL's finger events are mapped to <see cref="IInputState.Touches"/> (see
/// <see cref="SdlTouchSource"/>); GLFW has no touch input.
/// </para>
/// </remarks>
public sealed class SilkInputState : TrackedInputState, IDisposable
{
	private const int KeyCount = (int)Key.LastKey + 1;

	private readonly SilkWindow _window;
	private readonly List<InputEvent> _queue = new(256);
	private readonly bool[] _down = new bool[KeyCount];
	private readonly HashSet<IGamepad> _hooked = [];
	private EventReader<WindowFocusLostEvent> _focusLost;
	private IInputContext? _input;

	// Touch events come from SDL's event watch, possibly on another thread (Android), so they have their own locked queue.
	private readonly Lock _touchLock = new();
	private readonly List<InputEvent> _touchQueue = new(32);
	private SdlTouchSource? _touch;

	/// <summary>Creates the input state; it attaches to the window when the window is created.</summary>
	public SilkInputState(SilkWindow window, IEvents events, InputTracker tracker) : base(tracker)
	{
		_window = window;
		_focusLost = events.Reader<WindowFocusLostEvent>();
		_window.Created += Attach;
		_window.CursorStateChanged += _applyCursor;
	}

	/// <summary>The Silk.NET input context, or null before the window exists.</summary>
	public IInputContext? Context => _input;

	/// <summary>The number of events queued since the last <see cref="Step"/>.</summary>
	public int PendingEvents => _queue.Count;

	/// <summary>Creates the input context for <paramref name="view"/> and subscribes to its devices.</summary>
	internal void Attach(IView view)
	{
		if (_input is not null) return;

		_input = view.CreateInput();
		foreach (var keyboard in _input.Keyboards) _hookKeyboard(keyboard);
		foreach (var mouse in _input.Mice) _hookMouse(mouse);
		foreach (var gamepad in _input.Gamepads) _hookGamepad(gamepad);
		_input.ConnectionChanged += _onConnectionChanged;
		_applyCursor();

		// Touch screens: SDL only (GLFW has no touch events).
		if (_window.Platform == WindowPlatform.Sdl)
		{
			_touch = new SdlTouchSource(Silk.NET.SDL.SdlProvider.SDL.Value, () => _window.Size, EnqueueTouch);
		}
	}

	/// <summary>
	/// Begins the tracker's frame and applies the events queued since the previous call. Called by the input system at the
	/// start of every frame (First, <see cref="StageOrder.Input"/>).
	/// </summary>
	public void Step()
	{
		var tracker = Tracker;
		tracker.BeginFrame();

		for (var i = 0; i < _queue.Count; i++) _queue[i].ApplyTo(tracker);
		_queue.Clear();

		lock (_touchLock)
		{
			for (var i = 0; i < _touchQueue.Count; i++) _touchQueue[i].ApplyTo(tracker);
			_touchQueue.Clear();
		}

		// Key up events that happen while another window has focus never reach us, so forget held keys.
		if (_focusLost.Read().Length > 0)
		{
			tracker.ReleaseAll();
			Array.Clear(_down);
		}
	}

	/// <inheritdoc/>
	public override void SetMousePosition(Vector2 position)
	{
		if (_input is null) return;
		foreach (var mouse in _input.Mice) mouse.Position = position;
	}

	/// <summary>Disposes the input context.</summary>
	public void Dispose()
	{
		_window.Created -= Attach;
		_window.CursorStateChanged -= _applyCursor;
		_touch?.Dispose();
		_touch = null;
		_input?.Dispose();
		_input = null;
	}

	/// <summary>Queues an event as if it came from a device (for tests and tools).</summary>
	internal void Enqueue(in InputEvent e) => _queue.Add(e);

	/// <summary>Queues a touch event; thread-safe (SDL may deliver touches on another thread).</summary>
	internal void EnqueueTouch(InputEvent e)
	{
		lock (_touchLock) _touchQueue.Add(e);
	}

	private void _applyCursor()
	{
		if (_input is null) return;
		var mode = _window.IsMouseGrabbed ? CursorMode.Disabled : _window.IsCursorVisible ? CursorMode.Normal : CursorMode.Hidden;
		foreach (var mouse in _input.Mice) mouse.Cursor.CursorMode = mode;
	}

	private void _onConnectionChanged(IInputDevice device, bool connected)
	{
		switch (device)
		{
			case IKeyboard keyboard when connected: _hookKeyboard(keyboard); break;
			case IMouse mouse when connected: _hookMouse(mouse); break;
			case IGamepad gamepad:
				if (connected) _hookGamepad(gamepad);
				if ((uint)gamepad.Index < InputTracker.MaxGamepads) _queue.Add(InputEvent.ForGamepadConnection(gamepad.Index, connected));
				break;
		}
	}

	private void _hookKeyboard(IKeyboard keyboard)
	{
		keyboard.KeyDown -= _onKeyDown;
		keyboard.KeyUp -= _onKeyUp;
		keyboard.KeyChar -= _onKeyChar;
		keyboard.KeyDown += _onKeyDown;
		keyboard.KeyUp += _onKeyUp;
		keyboard.KeyChar += _onKeyChar;
	}

	private void _hookMouse(IMouse mouse)
	{
		mouse.MouseDown -= _onMouseDown;
		mouse.MouseUp -= _onMouseUp;
		mouse.MouseMove -= _onMouseMove;
		mouse.Scroll -= _onScroll;
		mouse.MouseDown += _onMouseDown;
		mouse.MouseUp += _onMouseUp;
		mouse.MouseMove += _onMouseMove;
		mouse.Scroll += _onScroll;
	}

	private void _hookGamepad(IGamepad gamepad)
	{
		if (!_hooked.Add(gamepad)) return;
		gamepad.Deadzone = new Deadzone(0, DeadzoneMethod.Traditional);
		gamepad.ButtonDown += _onButtonDown;
		gamepad.ButtonUp += _onButtonUp;
		gamepad.ThumbstickMoved += _onThumbstick;
		gamepad.TriggerMoved += _onTrigger;
		if (gamepad.IsConnected && (uint)gamepad.Index < InputTracker.MaxGamepads) _queue.Add(InputEvent.ForGamepadConnection(gamepad.Index, true));
	}

	private void _onKeyDown(IKeyboard keyboard, SilkKey key, int scancode) => KeyDown(key);

	private void _onKeyUp(IKeyboard keyboard, SilkKey key, int scancode) => KeyUp(key);

	/// <summary>Queues a key down, as a repeat when the key is already down, with the modifiers held now.</summary>
	internal void KeyDown(SilkKey key)
	{
		var ionKey = MapKey(key);
		if (ionKey == Key.Unknown) return;
		var repeat = _down[(int)ionKey];
		_down[(int)ionKey] = true;
		_queue.Add(InputEvent.ForKey(ionKey, true, repeat, _modifiers()));
	}

	/// <summary>Queues a key up with the modifiers held now.</summary>
	internal void KeyUp(SilkKey key)
	{
		var ionKey = MapKey(key);
		if (ionKey == Key.Unknown) return;
		_down[(int)ionKey] = false;
		_queue.Add(InputEvent.ForKey(ionKey, false, false, _modifiers()));
	}

	private void _onKeyChar(IKeyboard keyboard, char character) => _queue.Add(InputEvent.ForText(character));

	private void _onMouseDown(IMouse mouse, SilkMouseButton button)
	{
		if (MapMouseButton(button) is { } b) _queue.Add(InputEvent.ForMouseButton(b, true));
	}

	private void _onMouseUp(IMouse mouse, SilkMouseButton button)
	{
		if (MapMouseButton(button) is { } b) _queue.Add(InputEvent.ForMouseButton(b, false));
	}

	private void _onMouseMove(IMouse mouse, Vector2 position) => _queue.Add(InputEvent.ForMouseMove(position));

	private void _onScroll(IMouse mouse, ScrollWheel wheel)
	{
		if (wheel.Y != 0) _queue.Add(InputEvent.ForWheel(wheel.Y));
	}

	private void _onButtonDown(IGamepad gamepad, Button button)
	{
		if ((uint)gamepad.Index < InputTracker.MaxGamepads && MapGamepadButton(button.Name) is { } b)
			_queue.Add(InputEvent.ForGamepadButton(gamepad.Index, b, true));
	}

	private void _onButtonUp(IGamepad gamepad, Button button)
	{
		if ((uint)gamepad.Index < InputTracker.MaxGamepads && MapGamepadButton(button.Name) is { } b)
			_queue.Add(InputEvent.ForGamepadButton(gamepad.Index, b, false));
	}

	private void _onThumbstick(IGamepad gamepad, Thumbstick stick)
	{
		if ((uint)gamepad.Index >= InputTracker.MaxGamepads || stick.Index > 1) return;
		var (x, y) = stick.Index == 0 ? (GamepadAxis.LeftX, GamepadAxis.LeftY) : (GamepadAxis.RightX, GamepadAxis.RightY);
		_queue.Add(InputEvent.ForGamepadAxis(gamepad.Index, x, stick.X));
		_queue.Add(InputEvent.ForGamepadAxis(gamepad.Index, y, stick.Y));
	}

	private void _onTrigger(IGamepad gamepad, Trigger trigger)
	{
		if ((uint)gamepad.Index >= InputTracker.MaxGamepads || trigger.Index > 1) return;
		var axis = trigger.Index == 0 ? GamepadAxis.LeftTrigger : GamepadAxis.RightTrigger;
		// GLFW passes the raw gamepad axis through, in [-1, 1] with -1 at rest; SDL reports [0, 1]. Ion's triggers are [0, 1].
		var value = _window.Platform == WindowPlatform.Glfw ? (trigger.Position + 1f) * 0.5f : trigger.Position;
		_queue.Add(InputEvent.ForGamepadAxis(gamepad.Index, axis, Math.Clamp(value, 0f, 1f)));
	}

	private ModifierKeys _modifiers()
	{
		var modifiers = ModifierKeys.None;
		if (_down[(int)Key.ShiftLeft] || _down[(int)Key.ShiftRight]) modifiers |= ModifierKeys.Shift;
		if (_down[(int)Key.ControlLeft] || _down[(int)Key.ControlRight]) modifiers |= ModifierKeys.Control;
		if (_down[(int)Key.AltLeft] || _down[(int)Key.AltRight]) modifiers |= ModifierKeys.Alt;
		if (_down[(int)Key.WinLeft] || _down[(int)Key.WinRight]) modifiers |= ModifierKeys.Gui;
		return modifiers;
	}

	/// <summary>Maps a Silk.NET mouse button to Ion's (<c>Button4</c> and up become <see cref="MouseButton.Button1"/> and up).</summary>
	internal static MouseButton? MapMouseButton(SilkMouseButton button) => button switch
	{
		SilkMouseButton.Left => MouseButton.Left,
		SilkMouseButton.Middle => MouseButton.Middle,
		SilkMouseButton.Right => MouseButton.Right,
		>= SilkMouseButton.Button4 and <= SilkMouseButton.Button12 => MouseButton.Button1 + (button - SilkMouseButton.Button4),
		_ => null,
	};

	/// <summary>Maps a Silk.NET gamepad button name to Ion's SDL-layout <see cref="GamepadButton"/>.</summary>
	internal static GamepadButton? MapGamepadButton(ButtonName name) => name switch
	{
		ButtonName.A => GamepadButton.A,
		ButtonName.B => GamepadButton.B,
		ButtonName.X => GamepadButton.X,
		ButtonName.Y => GamepadButton.Y,
		ButtonName.LeftBumper => GamepadButton.LeftShoulder,
		ButtonName.RightBumper => GamepadButton.RightShoulder,
		ButtonName.Back => GamepadButton.Back,
		ButtonName.Start => GamepadButton.Start,
		ButtonName.Home => GamepadButton.Guide,
		ButtonName.LeftStick => GamepadButton.LeftStick,
		ButtonName.RightStick => GamepadButton.RightStick,
		ButtonName.DPadUp => GamepadButton.DPadUp,
		ButtonName.DPadRight => GamepadButton.DPadRight,
		ButtonName.DPadDown => GamepadButton.DPadDown,
		ButtonName.DPadLeft => GamepadButton.DPadLeft,
		_ => null,
	};

	/// <summary>Maps a Silk.NET (GLFW layout) key to Ion's <see cref="Key"/>; unmapped keys give <see cref="Key.Unknown"/>.</summary>
	internal static Key MapKey(SilkKey key) => key switch
	{
		>= SilkKey.A and <= SilkKey.Z => Key.A + (key - SilkKey.A),
		>= SilkKey.Number0 and <= SilkKey.Number9 => Key.Number0 + (key - SilkKey.Number0),
		>= SilkKey.F1 and <= SilkKey.F25 => Key.F1 + (key - SilkKey.F1),
		>= SilkKey.Keypad0 and <= SilkKey.Keypad9 => Key.Keypad0 + (key - SilkKey.Keypad0),
		SilkKey.Space => Key.Space,
		SilkKey.Apostrophe => Key.Quote,
		SilkKey.Comma => Key.Comma,
		SilkKey.Minus => Key.Minus,
		SilkKey.Period => Key.Period,
		SilkKey.Slash => Key.Slash,
		SilkKey.Semicolon => Key.Semicolon,
		SilkKey.Equal => Key.Plus,
		SilkKey.LeftBracket => Key.BracketLeft,
		SilkKey.BackSlash => Key.BackSlash,
		SilkKey.RightBracket => Key.BracketRight,
		SilkKey.GraveAccent => Key.Grave,
		SilkKey.World1 => Key.NonUSBackSlash,
		SilkKey.Escape => Key.Escape,
		SilkKey.Enter => Key.Enter,
		SilkKey.Tab => Key.Tab,
		SilkKey.Backspace => Key.BackSpace,
		SilkKey.Insert => Key.Insert,
		SilkKey.Delete => Key.Delete,
		SilkKey.Right => Key.Right,
		SilkKey.Left => Key.Left,
		SilkKey.Down => Key.Down,
		SilkKey.Up => Key.Up,
		SilkKey.PageUp => Key.PageUp,
		SilkKey.PageDown => Key.PageDown,
		SilkKey.Home => Key.Home,
		SilkKey.End => Key.End,
		SilkKey.CapsLock => Key.CapsLock,
		SilkKey.ScrollLock => Key.ScrollLock,
		SilkKey.NumLock => Key.NumLock,
		SilkKey.PrintScreen => Key.PrintScreen,
		SilkKey.Pause => Key.Pause,
		SilkKey.KeypadDecimal => Key.KeypadDecimal,
		SilkKey.KeypadDivide => Key.KeypadDivide,
		SilkKey.KeypadMultiply => Key.KeypadMultiply,
		SilkKey.KeypadSubtract => Key.KeypadSubtract,
		SilkKey.KeypadAdd => Key.KeypadAdd,
		SilkKey.KeypadEnter => Key.KeypadEnter,
		SilkKey.ShiftLeft => Key.ShiftLeft,
		SilkKey.ControlLeft => Key.ControlLeft,
		SilkKey.AltLeft => Key.AltLeft,
		SilkKey.SuperLeft => Key.WinLeft,
		SilkKey.ShiftRight => Key.ShiftRight,
		SilkKey.ControlRight => Key.ControlRight,
		SilkKey.AltRight => Key.AltRight,
		SilkKey.SuperRight => Key.WinRight,
		SilkKey.Menu => Key.Menu,
		_ => Key.Unknown,
	};
}
