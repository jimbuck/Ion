﻿namespace Ion;

internal class InputState : IInputState
{
	private readonly Window _window;
	private readonly ILogger _logger;

	private readonly Dictionary<Key, KeyEvent> _keyEvents = new(64);
	private readonly Dictionary<MouseButton, MouseEvent> _mouseEvents = new(13);
	private readonly HashSet<Key> _downKeys = new(8);

	private Veldrid.InputSnapshot? _inputSnapshot;

	public Vector2 MousePosition { get; private set; } = Vector2.Zero;

	public float WheelDelta { get; private set; } = 0;

	public InputState(IWindow window, ILogger<InputState> logger)
	{
		_window = (Window)window;
		_logger = logger;
	}

	public void Step()
	{
		_inputSnapshot = _window.InputSnapshot;

		_keyEvents.Clear();
		_mouseEvents.Clear();

		MousePosition = _inputSnapshot?.MousePosition ?? Vector2.Zero;
		WheelDelta = _inputSnapshot?.WheelDelta ?? 0;

		if (_inputSnapshot == default) return;

		foreach (var k in _inputSnapshot.KeyEvents)
		{
			var key = k.Key;
			_keyEvents[(Key)key] = k;

			if (!k.Down) _downKeys.Remove((Key)key);
			else if (!k.Repeat) _downKeys.Add((Key)key);
		}

		foreach (var m in _inputSnapshot.MouseEvents) _mouseEvents[(MouseButton)m.MouseButton] = m;
	}

	public bool Pressed(MouseButton btn) => _mouseEvents.TryGetValue(btn, out var mouseEvent) && mouseEvent.Down;
	public bool Released(MouseButton btn) => _mouseEvents.TryGetValue(btn, out var mouseEvent) && !mouseEvent.Down;
	public bool Down(MouseButton btn) => _inputSnapshot?.IsMouseDown((Veldrid.MouseButton)btn) ?? false;
	public bool Up(MouseButton btn) => !Down(btn);

	public bool Pressed(Key key) => _keyEvents.TryGetValue(key, out var keyEvent) && keyEvent.Down && !keyEvent.Repeat;
	public bool Pressed(Key key, ModifierKeys modifiers) => _keyEvents.TryGetValue(key, out var keyEvent) && ((keyEvent.Modifiers & modifiers) != ModifierKeys.None) && keyEvent.Down && !keyEvent.Repeat;

	public bool Released(Key key) => _keyEvents.TryGetValue(key, out var keyEvent) && !keyEvent.Down;
	public bool Released(Key key, ModifierKeys modifiers) => _keyEvents.TryGetValue(key, out var keyEvent) && ((keyEvent.Modifiers & modifiers) != ModifierKeys.None) && !keyEvent.Down;
	
	public bool Down(Key key) => _downKeys.Contains(key);
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