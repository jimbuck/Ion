using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>When the UI shows its built-in on-screen keyboard for a text field being edited.</summary>
public enum OnScreenKeyboardMode
{
	/// <summary>When editing starts from a gamepad (the R36S, a controller on a TV), unless the platform shows its own keyboard (phones). The default.</summary>
	Auto,
	/// <summary>Whenever a text field is edited, unless the platform shows its own keyboard.</summary>
	Always,
	/// <summary>Never (the game provides its own way to enter text).</summary>
	Never,
}

public sealed partial class Ui
{
	/// <summary>The character rows of the built-in keyboard; a last row holds Shift, Space, Backspace and Done.</summary>
	private static readonly string[] KeyRows = ["1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-"];

	/// <summary>The special keys of the last row and the key units each spans (the character rows are 10 units wide).</summary>
	private static readonly (string Label, int Units)[] SpecialKeys = [("Shift", 2), ("Space", 4), ("Del", 2), ("Done", 2)];

	private const int KeyboardRows = 5;
	private const int KeyboardUnits = 10;
	private const float KeyGap = 4;
	private const float KeyHeight = 40;
	private const float KeyboardPadding = 8;

	private bool _keyboardOpen;
	private int _keyRow = 1;
	private int _keyColumn;
	private bool _keyShift;
	private Nav _keyRepeatNav;
	private float _keyRepeatTimer;

	/// <summary>Whether the built-in on-screen keyboard is showing (a text field is being edited with it).</summary>
	public bool IsKeyboardOpen => _keyboardOpen;

	/// <summary>The selected key of the built-in keyboard (row 4 is Shift, Space, Del and Done) and whether Shift is on.</summary>
	internal (int Row, int Column, bool Shift) KeyboardSelection => (_keyRow, _keyColumn, _keyShift);

	/// <summary>Opens the built-in keyboard for the field just put into editing, when the mode asks for it.</summary>
	private void _openKeyboard(bool fromGamepad)
	{
		var mode = _options.OnScreenKeyboard;
		if (mode == OnScreenKeyboardMode.Never || _textInput?.ShowsScreenKeyboard == true) return;
		if (mode == OnScreenKeyboardMode.Auto && !fromGamepad) return;
		if (!_keyboardOpen)
		{
			_keyRow = 1;
			_keyColumn = 0;
			_keyShift = false;
		}

		_keyboardOpen = true;
	}

	/// <summary>
	/// The built-in keyboard's input: the D-pad or left stick moves the selection (wrapping), A types the selected key, B
	/// deletes, X types a space, Y toggles Shift, the shoulder buttons move the caret, Start finishes; a click or tap types a
	/// key. Returns whether the pointer press was used (so the UI behind does not get it).
	/// </summary>
	private bool _processKeyboard(float dt)
	{
		if (!_keyboardOpen) return false;
		if (_editingId == 0 || !_states.TryGetValue(_editingId, out var state))
		{
			_keyboardOpen = false;
			return false;
		}

		var input = _input;
		var usedPointer = false;
		if (input.Pressed(MouseButton.Left) && _keyAt(_pointer, out var row, out var column))
		{
			_keyRow = row;
			_keyColumn = column;
			_pressKey(state);
			usedPointer = true;
		}
		else if (input.Pressed(MouseButton.Left) && _contains(_keyboardRect(), _pointer))
		{
			usedPointer = true;
		}

		var (pressed, held) = _navigation(editing: false, keys: false);
		var fire = _keyRepeatOf(pressed, held, dt);
		switch (fire)
		{
			case Nav.Left: _keyColumn = (_keyColumn - 1 + _rowLength(_keyRow)) % _rowLength(_keyRow); break;
			case Nav.Right: _keyColumn = (_keyColumn + 1) % _rowLength(_keyRow); break;
			case Nav.Up or Nav.Down:
				{
					var unit = _unitOf(_keyRow, _keyColumn);
					_keyRow = (_keyRow + (fire == Nav.Down ? 1 : -1) + KeyboardRows) % KeyboardRows;
					_keyColumn = _columnAt(_keyRow, unit);
					break;
				}
		}

		if (_gamepadPressed(GamepadButton.A)) _pressKey(state);
		if (_gamepadPressed(GamepadButton.B)) state.Backspace();
		if (_gamepadPressed(GamepadButton.X)) state.Insert(" ");
		if (_gamepadPressed(GamepadButton.Y)) _keyShift = !_keyShift;
		if (_gamepadPressed(GamepadButton.LeftShoulder)) state.MoveCaret(state.Caret - 1, extend: false);
		if (_gamepadPressed(GamepadButton.RightShoulder)) state.MoveCaret(state.Caret + 1, extend: false);
		if (_gamepadPressed(GamepadButton.Start)) _finishKeyboard();
		return usedPointer;
	}

	/// <summary>Types the selected key.</summary>
	private void _pressKey(UiNodeState state)
	{
		if (_keyRow < KeyRows.Length)
		{
			var c = KeyRows[_keyRow][_keyColumn];
			Span<char> one = [_keyShift ? char.ToUpperInvariant(c) : c];
			state.Insert(one);
			// Shift applies to one character, as on a phone.
			_keyShift = false;
			return;
		}

		switch (_keyColumn)
		{
			case 0: _keyShift = !_keyShift; break;
			case 1: state.Insert(" "); break;
			case 2: state.Backspace(); break;
			default: _finishKeyboard(); break;
		}
	}

	private void _finishKeyboard()
	{
		_keyboardOpen = false;
		_editingId = 0;
	}

	private static int _rowLength(int row) => row < KeyRows.Length ? KeyboardUnits : SpecialKeys.Length;

	/// <summary>The first key unit a key covers (special keys span several).</summary>
	private static int _unitOf(int row, int column)
	{
		if (row < KeyRows.Length) return column;
		var unit = 0;
		for (var i = 0; i < column; i++) unit += SpecialKeys[i].Units;
		return unit;
	}

	/// <summary>The key of <paramref name="row"/> that covers <paramref name="unit"/>.</summary>
	private static int _columnAt(int row, int unit)
	{
		if (row < KeyRows.Length) return Math.Clamp(unit, 0, KeyboardUnits - 1);
		var start = 0;
		for (var i = 0; i < SpecialKeys.Length; i++)
		{
			start += SpecialKeys[i].Units;
			if (unit < start) return i;
		}

		return SpecialKeys.Length - 1;
	}

	private Nav _keyRepeatOf(Nav pressed, Nav held, float dt)
	{
		if (pressed != Nav.None)
		{
			_keyRepeatNav = pressed;
			_keyRepeatTimer = _theme.RepeatDelay;
			return pressed;
		}

		if (held == Nav.None || held != _keyRepeatNav)
		{
			_keyRepeatNav = Nav.None;
			return Nav.None;
		}

		_keyRepeatTimer -= dt;
		if (_keyRepeatTimer > 0) return Nav.None;
		var interval = _theme.RepeatInterval > 0 ? _theme.RepeatInterval : 0.05f;
		_keyRepeatTimer += interval;
		if (_keyRepeatTimer <= 0) _keyRepeatTimer = interval;
		return held;
	}

	/// <summary>The keyboard's panel: centered at the bottom of the viewport, keys up to 48 pixels wide.</summary>
	private RectangleF _keyboardRect()
	{
		var unit = _keyUnitWidth();
		var width = KeyboardUnits * unit + (KeyboardUnits - 1) * KeyGap + 2 * KeyboardPadding;
		var height = KeyboardRows * KeyHeight + (KeyboardRows - 1) * KeyGap + 2 * KeyboardPadding;
		return new RectangleF(MathF.Round((_viewport.X - width) * 0.5f), _viewport.Y - height, width, height);
	}

	private float _keyUnitWidth() => MathF.Max(16, MathF.Min(48, (_viewport.X - 2 * KeyboardPadding - (KeyboardUnits - 1) * KeyGap) / KeyboardUnits));

	/// <summary>The rectangle of a key.</summary>
	private RectangleF _keyRect(int row, int column)
	{
		var panel = _keyboardRect();
		var unit = _keyUnitWidth();
		var units = row < KeyRows.Length ? 1 : SpecialKeys[column].Units;
		var start = _unitOf(row, column);
		var x = panel.X + KeyboardPadding + start * (unit + KeyGap);
		var y = panel.Y + KeyboardPadding + row * (KeyHeight + KeyGap);
		return new RectangleF(x, y, units * unit + (units - 1) * KeyGap, KeyHeight);
	}

	private bool _keyAt(Vector2 point, out int row, out int column)
	{
		for (row = 0; row < KeyboardRows; row++)
		{
			for (column = 0; column < _rowLength(row); column++)
			{
				if (_contains(_keyRect(row, column), point)) return true;
			}
		}

		row = column = -1;
		return false;
	}

	private void _drawKeyboard(ISpriteBatch batch)
	{
		var theme = _theme;
		batch.DrawRect(theme.PanelBackground, _keyboardRect());
		for (var row = 0; row < KeyboardRows; row++)
		{
			for (var column = 0; column < _rowLength(row); column++)
			{
				var rect = _keyRect(row, column);
				var selected = row == _keyRow && column == _keyColumn;
				var on = row == KeyRows.Length && column == 0 && _keyShift;
				batch.DrawRect(selected ? theme.ButtonHover : on ? theme.ButtonPressed : theme.Button, rect);
				if (selected && theme.FocusThickness > 0)
				{
					var t = theme.FocusThickness;
					batch.DrawRectOutline(theme.FocusOutline, new RectangleF(rect.X - t, rect.Y - t, rect.Width + 2 * t, rect.Height + 2 * t), t);
				}

				var label = row < KeyRows.Length ? _keyLabel(KeyRows[row][column]) : SpecialKeys[column].Label;
				var size = MeasureText(label);
				_text(batch, label, new Vector2(rect.X + (rect.Width - size.X) * 0.5f, rect.Y + (rect.Height - size.Y) * 0.5f), theme.Text, 1f);
			}
		}
	}

	/// <summary>A key's label, upper case while Shift is on (single-character strings, cached).</summary>
	private string _keyLabel(char c)
	{
		var shown = _keyShift ? char.ToUpperInvariant(c) : c;
		if (!_charStrings.TryGetValue(shown, out var s))
		{
			s = shown.ToString();
			_charStrings[shown] = s;
		}

		return s;
	}
}
