using System.Numerics;
using System.Runtime.CompilerServices;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

public sealed partial class Ui
{
	// Containers ------------------------------------------------------------------------------------------------------

	/// <summary>
	/// Opens a panel: a container with a background (<see cref="UiTheme.PanelBackground"/> or <see cref="UiTheme.PanelSkin"/>)
	/// and the theme's padding, laying out its children along <see cref="UiStyle.Direction"/> (a column by default). Pointer
	/// input over a panel does not reach widgets drawn under it.
	/// </summary>
	/// <param name="key">The panel's path segment and identity, or null for <c>panel</c> and its call site.</param>
	/// <param name="style">Layout and look.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public UiScope Panel(string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
		_open(UiNodeKind.Panel, key, style, file, line);

	/// <summary>Opens a transparent container laying out its children left to right.</summary>
	/// <inheritdoc cref="Panel" path="/param"/>
	public UiScope Row(string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		var s = style;
		s.Direction = UiDirection.Row;
		return _open(UiNodeKind.Row, key, s, file, line);
	}

	/// <summary>Opens a transparent container laying out its children top to bottom.</summary>
	/// <inheritdoc cref="Panel" path="/param"/>
	public UiScope Column(string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		var s = style;
		s.Direction = UiDirection.Column;
		return _open(UiNodeKind.Column, key, s, file, line);
	}

	/// <summary>
	/// Opens a scroll view: its children are clipped to its rectangle and scroll along its main axis (the wheel over it, or
	/// moving the focus to a widget outside it). Give it a size (<see cref="UiStyle.Height"/>, <see cref="UiStyle.MaxHeight"/>
	/// or <see cref="UiStyle.Grow"/>), otherwise it grows to its content and never scrolls. Its tree value is the offset.
	/// </summary>
	/// <inheritdoc cref="Panel" path="/param"/>
	public UiScope ScrollView(string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		var scope = _open(UiNodeKind.ScrollView, key, style, file, line, NodeFlags.Clips);
		ref var node = ref _nodes[_current];
		node.Value = _format(node.State, node.State.Scroll);
		return scope;
	}

	/// <summary>
	/// Disables the widgets created until the scope is disposed (when <paramref name="disabled"/> is true): they are drawn
	/// dimmed and ignore pointer, focus and tree commands.
	/// </summary>
	public UiScope Disabled(bool disabled = true)
	{
		if (!disabled) return default;
		if (!_inFrame) _notInFrame();
		_disabled++;
		return new UiScope(this, -1, true);
	}

	// Widgets ---------------------------------------------------------------------------------------------------------

	/// <summary>Adds text. Its path segment is <paramref name="key"/> or <c>label</c> (the text may change without changing the path).</summary>
	/// <param name="text">The text.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="scale">The text scale (2 for a title).</param>
	/// <param name="color">The color, or null for the theme's.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public void Label(string text, string? key = null, float scale = 1f, Color? color = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.Label, id, style, key, NodeFlags.None);
		node.Text = text;
		node.TextScale = scale;
		node.TextColor = color;
		node.TextSize = MeasureText(text, scale);
		node.Measured = node.TextSize;
	}

	/// <summary>
	/// Adds text given as characters (for example formatted into a <c>stackalloc</c> buffer): the string is interned, so
	/// text that repeats (a score, a counter) costs no allocation.
	/// </summary>
	/// <inheritdoc cref="Label(string, string?, float, Color?, in UiStyle, string, int)" path="/param"/>
	public void Label(ReadOnlySpan<char> text, string? key = null, float scale = 1f, Color? color = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
		Label(_strings.Intern(text), key, scale, color, style, file, line);

	/// <summary>
	/// Adds a button. Returns true in the frame it is clicked: pointer pressed and released over it, the activate input
	/// (Enter, Space, gamepad A) while it has the focus, or <see cref="IUiTree.Click"/>.
	/// </summary>
	/// <param name="text">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool Button(string text, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.Button, id, style, key ?? text, NodeFlags.Focusable | NodeFlags.Blocks);
		node.Text = text;
		node.TextSize = MeasureText(text);
		node.Measured = new Vector2(node.TextSize.X + 2 * _theme.ButtonPadding, MathF.Max(_theme.ControlHeight, node.TextSize.Y + 8));
		return node.Is(NodeFlags.Enabled) && _wasClicked(id);
	}

	/// <summary>
	/// Adds a checkbox with a caption. Clicking or activating it flips <paramref name="value"/>; <see cref="IUiTree.SetValue"/>
	/// sets it. Returns true when <paramref name="value"/> changed this frame.
	/// </summary>
	/// <param name="text">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="value">The state, read and written.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool Toggle(string text, ref bool value, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.Toggle, id, style, key ?? text, NodeFlags.Focusable | NodeFlags.Blocks);
		var state = node.State;
		var old = value;
		if (node.Is(NodeFlags.Enabled))
		{
			if (state.HasPending) value = state.PendingBool;
			if (_wasClicked(id)) value = !value;
		}

		state.HasPending = false;
		node.Text = text;
		node.Value = value ? "true" : "false";
		if (value) node.Flags |= NodeFlags.On;
		node.TextSize = MeasureText(text);
		node.Measured = new Vector2(_theme.ToggleSize + _gap + node.TextSize.X, MathF.Max(_theme.ControlHeight, node.TextSize.Y));
		return value != old;
	}

	/// <summary>
	/// Adds a slider with a caption and its value. Dragging with the pointer, Left/Right (keys, D-pad, stick) while it has
	/// the focus, or <see cref="IUiTree.SetValue"/> change <paramref name="value"/>, which is kept within
	/// [<paramref name="min"/>, <paramref name="max"/>] and snapped to <paramref name="step"/>. Returns true when it changed.
	/// </summary>
	/// <param name="text">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="value">The value, read and written.</param>
	/// <param name="min">The minimum.</param>
	/// <param name="max">The maximum.</param>
	/// <param name="step">The step (0: continuous; keyboard and gamepad then move by a twentieth of the range).</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool Slider(string text, ref float value, float min = 0f, float max = 1f, float step = 0f, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		if (max < min) (min, max) = (max, min);
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.Slider, id, style, key ?? text, NodeFlags.Focusable | NodeFlags.Blocks);
		var state = node.State;
		var old = value;
		if (node.Is(NodeFlags.Enabled))
		{
			if (state.HasPending) value = state.PendingFloat;
			if (state.PendingSteps != 0) value += state.PendingSteps * (step > 0 ? step : (max - min) / 20f);
			if (_activeId == id && _pointerHeld && state.TrackWidth > 0)
			{
				value = min + Math.Clamp((_pointer.X - state.TrackX) / state.TrackWidth, 0f, 1f) * (max - min);
			}
		}

		state.HasPending = false;
		state.PendingSteps = 0;
		if (float.IsNaN(value)) value = min;
		if (step > 0) value = min + MathF.Round((value - min) / step) * step;
		value = Math.Clamp(value, min, max);

		node.Text = text;
		node.Number = max > min ? (value - min) / (max - min) : 0f;
		node.Value = _format(state, value);
		node.TextSize = MeasureText(text);
		node.LabelWidth = _theme.LabelWidth > 0 ? _theme.LabelWidth : node.TextSize.X;
		node.Measured = new Vector2(node.LabelWidth + _gap + _theme.SliderWidth + _gap + _theme.ValueWidth, MathF.Max(_theme.ControlHeight, node.TextSize.Y));
		return value != old;
	}

	/// <summary>
	/// Adds a single-line text field with a caption. Clicking it, or activating it while it has the focus, starts editing:
	/// typed text is inserted at the caret, Backspace/Delete/Left/Right/Home/End edit, and Enter, Escape, gamepad A or B, or
	/// clicking elsewhere stop. <see cref="IUiTree.SetValue"/> replaces the text and <see cref="IUiTree.Type"/> types into it.
	/// Returns true when <paramref name="value"/> changed (a new string is allocated per edit, never per frame).
	/// </summary>
	/// <param name="label">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="value">The text, read and written.</param>
	/// <param name="maxLength">The maximum number of characters.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool TextInput(string label, ref string value, int maxLength = 64, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		maxLength = Math.Max(1, maxLength);
		value ??= string.Empty;
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.TextInput, id, style, key ?? label, NodeFlags.Focusable | NodeFlags.Blocks);
		var state = node.State;
		var old = value;

		if (state.Buffer is null || state.Buffer.Length < maxLength || (!state.Edited && !ReferenceEquals(state.Text, value)))
		{
			state.SetText(value, maxLength);
		}

		if (node.Is(NodeFlags.Enabled))
		{
			if (state.HasPending && state.PendingString is { } pending)
			{
				value = pending.Length > maxLength ? pending[..maxLength] : pending;
				state.SetText(value, maxLength);
				state.Caret = state.Length;
				state.Edited = false;
			}

			if (state.Edited)
			{
				var span = state.Buffer.AsSpan(0, state.Length);
				if (!span.SequenceEqual(value)) value = new string(span);
				state.Text = value;
			}
		}

		state.Edited = false;
		state.HasPending = false;
		state.PendingString = null;
		node.Text = label;
		node.Value = value;
		node.Number = maxLength;
		node.TextSize = MeasureText(label);
		node.LabelWidth = _theme.LabelWidth > 0 ? _theme.LabelWidth : node.TextSize.X;
		node.Measured = new Vector2(node.LabelWidth + _gap + _theme.InputWidth, MathF.Max(_theme.ControlHeight, node.TextSize.Y + 8));
		return !ReferenceEquals(old, value) && !string.Equals(old, value, StringComparison.Ordinal);
	}

	/// <summary>
	/// Adds a single-selection list: a caption and one focusable item per entry of <paramref name="items"/> (children of the
	/// list in the tree, their path segment is their text). Clicking or activating an item, or <see cref="IUiTree.SetValue"/>
	/// on the list (an item's text or index), selects it. Returns true when <paramref name="selected"/> changed. Put it in a
	/// <see cref="ScrollView"/> for long lists.
	/// </summary>
	/// <param name="label">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="items">The entries.</param>
	/// <param name="selected">The selected index (-1 for none), read and written.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout of the list (a column).</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool List(string label, ReadOnlySpan<string> items, ref int selected, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(key, file, line);
		var s = style;
		s.Direction = UiDirection.Column;
		ref var list = ref _add(UiNodeKind.List, id, s, key ?? label, NodeFlags.Container);
		var index = _count - 1;
		var state = list.State;
		var old = selected;
		list.Text = label;
		list.TextSize = MeasureText(label);
		list.Inset = list.TextSize.Y + _gap;

		if (list.Is(NodeFlags.Enabled) && state.HasPending && state.PendingString is { } pending)
		{
			if (int.TryParse(pending, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) && (uint)number < (uint)items.Length)
			{
				selected = number;
			}
			else
			{
				for (var i = 0; i < items.Length; i++)
				{
					if (string.Equals(items[i], pending, StringComparison.Ordinal))
					{
						selected = i;
						break;
					}
				}
			}
		}

		state.HasPending = false;
		state.PendingString = null;

		var parent = _current;
		_current = index;
		var itemHeight = _theme.ControlHeight;
		for (var i = 0; i < items.Length; i++)
		{
			var itemId = _unique((uint)HashCode.Combine(id, i + 1));
			ref var item = ref _add(UiNodeKind.ListItem, itemId, default, items[i], NodeFlags.Focusable | NodeFlags.Blocks);
			item.Text = items[i];
			item.TextSize = MeasureText(items[i]);
			item.Measured = new Vector2(item.TextSize.X + 2 * _theme.ButtonPadding, MathF.Max(itemHeight, item.TextSize.Y));
			if (item.Is(NodeFlags.Enabled) && _wasClicked(itemId)) selected = i;
		}

		_current = parent;
		for (int c = _nodes[index].FirstChild, i = 0; c >= 0; c = _nodes[c].NextSibling, i++)
		{
			ref var item = ref _nodes[c];
			var on = i == selected;
			item.Value = on ? "true" : "false";
			if (on) item.Flags |= NodeFlags.On;
		}

		_nodes[index].Value = (uint)selected < (uint)items.Length ? items[selected] : null;
		_nodes[index].Number = selected;
		return selected != old;
	}

	/// <summary>
	/// Adds empty space: <paramref name="size"/> pixels along the container's main axis, or (0, the default) a spacer that
	/// grows to take the free space.
	/// </summary>
	public void Spacer(float size = 0f, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(null, file, line);
		var row = _nodes[_current].Style.Direction == UiDirection.Row;
		var style = new UiStyle { Grow = size > 0 ? 0 : 1 };
		ref var node = ref _add(UiNodeKind.Spacer, id, style, null, NodeFlags.None);
		node.Measured = row ? new Vector2(size, 0) : new Vector2(0, size);
	}

	private float _gap => _theme.Gap;

	private string _format(UiNodeState state, float value)
	{
		if (state.FormattedText is null || state.FormattedNumber != value)
		{
			state.FormattedNumber = value;
			state.FormattedText = _strings.Format(value);
		}

		return state.FormattedText;
	}

	private bool _wasClicked(uint id)
	{
		for (var i = 0; i < _clickedCount; i++)
		{
			if (_clicked[i] == id) return true;
		}

		return false;
	}
}
