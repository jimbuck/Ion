using System.Globalization;
using System.Numerics;

namespace Ion.Extensions.UI;

public sealed partial class Ui
{
	private enum Nav : byte
	{
		None,
		Up,
		Down,
		Left,
		Right,
		Next,
		Previous,
		Backspace,
		Delete,
		Home,
		End,
	}

	private const int MaxClicks = 16;

	private readonly uint[] _clicked = new uint[MaxClicks];
	private int _clickedCount;
	private uint _hotId;
	private uint _activeId;
	private uint _focusId;
	private uint _editingId;
	private bool _focusSeen;
	private Vector2 _pointer;
	private bool _pointerHeld;
	private Nav _repeatNav;
	private float _repeatTimer;
	private Nav _stickPrevious;

	/// <summary>The id of the widget under the pointer (from the previous frame's layout), or 0.</summary>
	internal uint HotId => _hotId;

	/// <summary>The id of the focused widget, or 0.</summary>
	internal uint FocusId => _focusId;

	private void _processInput(float dt)
	{
		_focusSeen = false;
		var input = _input;

		// Pointer, against the previous frame's layout.
		_pointer = input.MousePosition;
		var hit = _hitTest(_pointer, out var overUi);
		_hotId = hit >= 0 ? _prev[hit].Id : 0;
		IsPointerOverUi = overUi;

		if (input.Pressed(MouseButton.Left))
		{
			_activeId = _hotId;
			if (hit >= 0)
			{
				_setFocus(_hotId, scroll: false);
				if (_prev[hit].Kind == UiNodeKind.TextInput) _startEditing(_prev[hit].State);
			}
			else
			{
				_editingId = 0;
			}
		}

		if (input.Released(MouseButton.Left) && _activeId != 0 && _activeId == _hotId && hit >= 0)
		{
			var kind = _prev[hit].Kind;
			if (kind is UiNodeKind.Button or UiNodeKind.Toggle or UiNodeKind.ListItem) _click(_activeId);
		}

		_pointerHeld = input.Down(MouseButton.Left) || input.Pressed(MouseButton.Left);

		var wheel = input.WheelDelta;
		if (wheel != 0) _scrollAt(_pointer, wheel);

		if (_editingId != 0) _processEditing(dt);
		else _processNavigation(dt);
	}

	private void _endInput()
	{
		if (!_input.Down(MouseButton.Left)) _activeId = 0;
		if (_focusId != 0 && !_focusSeen) _focusId = 0;
		if (_editingId != 0 && _editingId != _focusId) _editingId = 0;
		if (_focusId == 0 && _options.AutoFocus)
		{
			for (var i = 1; i < _count; i++)
			{
				ref var node = ref _nodes[i];
				if (node.Is(NodeFlags.Focusable) && node.Is(NodeFlags.Enabled))
				{
					_focusId = node.Id;
					break;
				}
			}
		}
	}

	private int _hitTest(Vector2 point, out bool overUi)
	{
		overUi = false;
		for (var i = _prevCount - 1; i >= 1; i--)
		{
			ref var node = ref _prev[i];
			if ((node.Flags & (NodeFlags.Focusable | NodeFlags.Blocks)) == 0 || !node.Is(NodeFlags.Visible)) continue;
			if (!_contains(node.Rect, point) || !_contains(node.Clip, point)) continue;
			overUi = true;
			return node.Is(NodeFlags.Focusable) && node.Is(NodeFlags.Enabled) ? i : -1;
		}

		return -1;
	}

	private static bool _contains(in RectangleF rect, Vector2 point) =>
		point.X >= rect.X && point.Y >= rect.Y && point.X < rect.X + rect.Width && point.Y < rect.Y + rect.Height;

	private void _scrollAt(Vector2 point, float wheel)
	{
		for (var i = _prevCount - 1; i >= 1; i--)
		{
			ref var node = ref _prev[i];
			if (!node.Is(NodeFlags.Visible) || !_contains(node.Rect, point) || !_contains(node.Clip, point)) continue;
			for (var j = i; j > 0; j = _prev[j].Parent)
			{
				if (!_prev[j].Is(NodeFlags.Clips)) continue;
				var state = _prev[j].State;
				state.Scroll = Math.Clamp(state.Scroll - wheel * _theme.ScrollSpeed, 0, state.ScrollMax);
				return;
			}

			return;
		}
	}

	private void _processNavigation(float dt)
	{
		var input = _input;
		var (pressed, held) = _navigation(editing: false);
		var fire = _repeat(pressed, held, dt);

		var activate = input.Pressed(Key.Enter) || input.Pressed(Key.KeypadEnter) || input.Pressed(Key.Space) || _gamepadPressed(GamepadButton.A);
		if (input.Pressed(Key.Escape) || _gamepadPressed(GamepadButton.B)) BackPressed = true;

		if (fire != Nav.None)
		{
			var focused = _prevIndex(_focusId);
			if (focused < 0)
			{
				_focusFirst();
			}
			else if (_prev[focused].Kind == UiNodeKind.Slider && fire is Nav.Left or Nav.Right)
			{
				if (_prev[focused].Is(NodeFlags.Enabled))
				{
					var state = _prev[focused].State;
					state.PendingSteps += fire == Nav.Left ? -1 : 1;
					state.PendingFrame = _frame;
				}
			}
			else if (fire is Nav.Next or Nav.Previous)
			{
				_moveLinear(focused, fire == Nav.Next);
			}
			else if (fire is Nav.Up or Nav.Down or Nav.Left or Nav.Right)
			{
				_moveSpatial(focused, fire);
			}
		}

		if (activate)
		{
			var focused = _prevIndex(_focusId);
			if (focused < 0) _focusFirst();
			else _activate(focused);
		}
	}

	private void _processEditing(float dt)
	{
		var input = _input;
		if (!_states.TryGetValue(_editingId, out var state))
		{
			_editingId = 0;
			return;
		}

		var text = input.Text;
		if (!text.IsEmpty) state.Insert(text);

		var (pressed, held) = _navigation(editing: true);
		switch (_repeat(pressed, held, dt))
		{
			case Nav.Left: state.Caret = Math.Max(0, state.Caret - 1); break;
			case Nav.Right: state.Caret = Math.Min(state.Length, state.Caret + 1); break;
			case Nav.Home: state.Caret = 0; break;
			case Nav.End: state.Caret = state.Length; break;
			case Nav.Backspace: state.Backspace(); break;
			case Nav.Delete: state.Delete(); break;
			case Nav.Up or Nav.Down:
				{
					var focused = _prevIndex(_focusId);
					_editingId = 0;
					if (focused >= 0) _moveSpatial(focused, pressed == Nav.None ? held : pressed);
					break;
				}
			case Nav.Next or Nav.Previous:
				{
					var focused = _prevIndex(_focusId);
					_editingId = 0;
					if (focused >= 0) _moveLinear(focused, (pressed == Nav.None ? held : pressed) == Nav.Next);
					break;
				}
		}

		if (input.Pressed(Key.Enter) || input.Pressed(Key.KeypadEnter) || input.Pressed(Key.Escape)
			|| _gamepadPressed(GamepadButton.A) || _gamepadPressed(GamepadButton.B))
		{
			_editingId = 0;
		}
	}

	private (Nav Pressed, Nav Held) _navigation(bool editing)
	{
		var input = _input;
		var shift = (input.Modifiers & ModifierKeys.Shift) != 0;
		var pressed = Nav.None;
		var held = Nav.None;

		if (input.Pressed(Key.Up)) pressed = Nav.Up;
		else if (input.Pressed(Key.Down)) pressed = Nav.Down;
		else if (input.Pressed(Key.Left)) pressed = Nav.Left;
		else if (input.Pressed(Key.Right)) pressed = Nav.Right;
		else if (input.Pressed(Key.Tab)) pressed = shift ? Nav.Previous : Nav.Next;
		else if (editing && input.Pressed(Key.BackSpace)) pressed = Nav.Backspace;
		else if (editing && input.Pressed(Key.Delete)) pressed = Nav.Delete;
		else if (editing && input.Pressed(Key.Home)) pressed = Nav.Home;
		else if (editing && input.Pressed(Key.End)) pressed = Nav.End;

		if (input.Down(Key.Up)) held = Nav.Up;
		else if (input.Down(Key.Down)) held = Nav.Down;
		else if (input.Down(Key.Left)) held = Nav.Left;
		else if (input.Down(Key.Right)) held = Nav.Right;
		else if (input.Down(Key.Tab)) held = shift ? Nav.Previous : Nav.Next;
		else if (editing && input.Down(Key.BackSpace)) held = Nav.Backspace;
		else if (editing && input.Down(Key.Delete)) held = Nav.Delete;

		if (editing) return (pressed, held);

		// Gamepads: the D-pad, and the left stick past the threshold (its crossing counts as a press).
		var stick = Nav.None;
		var pads = input.Gamepads;
		for (var i = 0; i < pads.Count; i++)
		{
			var pad = pads[i];
			if (_options.Gamepad >= 0 && pad.Index != _options.Gamepad) continue;
			if (pressed == Nav.None)
			{
				if (pad.Pressed(GamepadButton.DPadUp)) pressed = Nav.Up;
				else if (pad.Pressed(GamepadButton.DPadDown)) pressed = Nav.Down;
				else if (pad.Pressed(GamepadButton.DPadLeft)) pressed = Nav.Left;
				else if (pad.Pressed(GamepadButton.DPadRight)) pressed = Nav.Right;
			}

			if (held == Nav.None)
			{
				if (pad.Down(GamepadButton.DPadUp)) held = Nav.Up;
				else if (pad.Down(GamepadButton.DPadDown)) held = Nav.Down;
				else if (pad.Down(GamepadButton.DPadLeft)) held = Nav.Left;
				else if (pad.Down(GamepadButton.DPadRight)) held = Nav.Right;
			}

			if (stick == Nav.None) stick = _stickDirection(pad.LeftStick);
		}

		if (stick != Nav.None && stick != _stickPrevious && pressed == Nav.None) pressed = stick;
		if (held == Nav.None) held = stick;
		_stickPrevious = stick;
		return (pressed, held);
	}

	private Nav _stickDirection(Vector2 stick)
	{
		var threshold = _theme.StickThreshold > 0 ? _theme.StickThreshold : 0.5f;
		if (MathF.Abs(stick.X) >= MathF.Abs(stick.Y))
		{
			if (stick.X <= -threshold) return Nav.Left;
			if (stick.X >= threshold) return Nav.Right;
		}
		else
		{
			if (stick.Y <= -threshold) return Nav.Up;
			if (stick.Y >= threshold) return Nav.Down;
		}

		return Nav.None;
	}

	private Nav _repeat(Nav pressed, Nav held, float dt)
	{
		if (pressed != Nav.None)
		{
			_repeatNav = pressed;
			_repeatTimer = _theme.RepeatDelay;
			return pressed;
		}

		if (held == Nav.None || held != _repeatNav)
		{
			_repeatNav = Nav.None;
			return Nav.None;
		}

		_repeatTimer -= dt;
		if (_repeatTimer > 0) return Nav.None;
		var interval = _theme.RepeatInterval > 0 ? _theme.RepeatInterval : 0.05f;
		_repeatTimer += interval;
		if (_repeatTimer <= 0) _repeatTimer = interval;
		return held;
	}

	private bool _gamepadPressed(GamepadButton button)
	{
		var pads = _input.Gamepads;
		for (var i = 0; i < pads.Count; i++)
		{
			var pad = pads[i];
			if (_options.Gamepad >= 0 && pad.Index != _options.Gamepad) continue;
			if (pad.Pressed(button)) return true;
		}

		return false;
	}

	// Focus -----------------------------------------------------------------------------------------------------------

	private int _prevIndex(uint id)
	{
		if (id == 0 || !_states.TryGetValue(id, out var state)) return -1;
		var index = state.Index;
		return index > 0 && index < _prevCount && _prev[index].Id == id ? index : -1;
	}

	private static bool _focusable(in UiNode node) => node.Is(NodeFlags.Focusable) && node.Is(NodeFlags.Enabled);

	private void _focusFirst()
	{
		for (var i = 1; i < _prevCount; i++)
		{
			if (_focusable(_prev[i]))
			{
				_setFocus(_prev[i].Id, scroll: true);
				return;
			}
		}
	}

	private void _setFocus(uint id, bool scroll)
	{
		if (id != _editingId) _editingId = 0;
		_focusId = id;
		if (scroll) _scrollIntoView(_prevIndex(id));
	}

	private void _moveLinear(int from, bool forward)
	{
		var count = _prevCount - 1;
		for (var step = 1; step <= count; step++)
		{
			var i = forward ? from + step : from - step;
			i = ((i - 1) % count + count) % count + 1;
			if (i != from && _focusable(_prev[i]))
			{
				_setFocus(_prev[i].Id, scroll: true);
				return;
			}
		}
	}

	private void _moveSpatial(int from, Nav direction)
	{
		ref var current = ref _prev[from];
		var a = current.Rect;
		var best = -1;
		var bestScore = float.MaxValue;
		for (var i = 1; i < _prevCount; i++)
		{
			if (i == from || !_focusable(_prev[i])) continue;
			var b = _prev[i].Rect;
			float primary, secondary;
			// Only widgets entirely beyond the current one's edge (1 px of overlap allowed) are candidates.
			switch (direction)
			{
				case Nav.Down:
					if (b.Y < a.Y + a.Height - 1) continue;
					primary = MathF.Max(0, b.Y - (a.Y + a.Height));
					secondary = _gapBetween(a.X, a.X + a.Width, b.X, b.X + b.Width);
					break;
				case Nav.Up:
					if (b.Y + b.Height > a.Y + 1) continue;
					primary = MathF.Max(0, a.Y - (b.Y + b.Height));
					secondary = _gapBetween(a.X, a.X + a.Width, b.X, b.X + b.Width);
					break;
				case Nav.Right:
					if (b.X < a.X + a.Width - 1) continue;
					primary = MathF.Max(0, b.X - (a.X + a.Width));
					secondary = _gapBetween(a.Y, a.Y + a.Height, b.Y, b.Y + b.Height);
					break;
				default:
					if (b.X + b.Width > a.X + 1) continue;
					primary = MathF.Max(0, a.X - (b.X + b.Width));
					secondary = _gapBetween(a.Y, a.Y + a.Height, b.Y, b.Y + b.Height);
					break;
			}

			// Overlapping on the other axis matters most: a widget straight below beats a nearer one off to the side.
			var score = primary + secondary * 4f;
			if (score < bestScore)
			{
				bestScore = score;
				best = i;
			}
		}

		if (best >= 0) _setFocus(_prev[best].Id, scroll: true);
	}

	private static float _gapBetween(float a0, float a1, float b0, float b1) => MathF.Max(0, MathF.Max(b0 - a1, a0 - b1));

	private void _scrollIntoView(int index)
	{
		if (index <= 0) return;
		var target = _prev[index].Rect;
		for (var j = _prev[index].Parent; j > 0; j = _prev[j].Parent)
		{
			ref var view = ref _prev[j];
			if (!view.Is(NodeFlags.Clips)) continue;
			var state = view.State;
			var row = view.Style.Direction == UiDirection.Row;
			var start = row ? view.Rect.X : view.Rect.Y;
			var end = row ? view.Rect.X + view.Rect.Width : view.Rect.Y + view.Rect.Height;
			var t0 = row ? target.X : target.Y;
			var t1 = row ? target.X + target.Width : target.Y + target.Height;
			if (t0 < start) state.Scroll -= start - t0;
			else if (t1 > end) state.Scroll += t1 - end;
			state.Scroll = Math.Clamp(state.Scroll, 0, state.ScrollMax);
			return;
		}
	}

	private void _activate(int index)
	{
		ref var node = ref _prev[index];
		if (!node.Is(NodeFlags.Enabled)) return;
		switch (node.Kind)
		{
			case UiNodeKind.Button or UiNodeKind.Toggle or UiNodeKind.ListItem:
				_click(node.Id);
				break;
			case UiNodeKind.TextInput:
				_startEditing(node.State);
				break;
		}
	}

	private void _click(uint id)
	{
		if (_clickedCount < MaxClicks && !_wasClicked(id)) _clicked[_clickedCount++] = id;
	}

	private void _startEditing(UiNodeState state)
	{
		_focusId = state.Id;
		_editingId = state.Id;
		state.Caret = state.Length;
	}

	// Tree commands ---------------------------------------------------------------------------------------------------

	internal void ApplyCommand(UiCommandKind kind, string? path, string? value)
	{
		if (kind == UiCommandKind.Back)
		{
			if (_editingId != 0) _editingId = 0;
			else BackPressed = true;
			return;
		}

		var index = _findPrev(path);
		if (index < 0) return;
		ref var node = ref _prev[index];
		if (!node.Is(NodeFlags.Enabled)) return;
		var state = node.State;
		state.PendingFrame = _frame;

		switch (kind)
		{
			case UiCommandKind.Click:
				if (node.Is(NodeFlags.Focusable)) _setFocus(node.Id, scroll: true);
				_activate(index);
				break;
			case UiCommandKind.Focus:
				if (node.Is(NodeFlags.Focusable)) _setFocus(node.Id, scroll: true);
				break;
			case UiCommandKind.Type:
				_setFocus(node.Id, scroll: true);
				if (_editingId != node.Id) _startEditing(state);
				state.Insert(value);
				break;
			case UiCommandKind.SetValue:
				switch (node.Kind)
				{
					case UiNodeKind.Toggle when bool.TryParse(value, out var on):
						state.PendingBool = on;
						state.HasPending = true;
						break;
					case UiNodeKind.Slider when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
						state.PendingFloat = number;
						state.HasPending = true;
						break;
					case UiNodeKind.TextInput or UiNodeKind.List:
						state.PendingString = value ?? string.Empty;
						state.HasPending = true;
						break;
				}

				break;
		}
	}

	private int _findPrev(string? path)
	{
		if (path is null) return -1;
		for (var i = 1; i < _prevCount; i++)
		{
			if (string.Equals(_prev[i].State.Path, path, StringComparison.Ordinal)) return i;
		}

		return -1;
	}
}
