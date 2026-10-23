using System.Numerics;
using System.Runtime.CompilerServices;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>
/// The wrapped lines of a text area's text: where each visual line starts and ends, and the x of every caret position
/// within its line. Built from per-character advances (each character measured once and cached), so a layout costs one
/// pass over the text and is rebuilt only when the text, the width or the font changes.
/// </summary>
internal sealed class TextAreaLayout
{
	public string Source = string.Empty;
	public float Width;
	public object? Font;
	public float[] X = [0];
	public int[] Starts = [0];
	public int[] Ends = [0];
	public string[] Texts = [string.Empty];
	public int Count = 1;

	/// <summary>The visual line of caret position <paramref name="index"/>: the last line starting at or before it (the end of a wrapped line is the start of the next).</summary>
	public int LineOf(int index)
	{
		var line = 0;
		for (var i = 1; i < Count; i++)
		{
			if (Starts[i] <= index) line = i;
			else break;
		}

		return line;
	}

	/// <summary>The caret position on <paramref name="line"/> nearest to <paramref name="x"/>.</summary>
	public int IndexAt(int line, float x)
	{
		line = Math.Clamp(line, 0, Count - 1);
		var best = Starts[line];
		for (var i = Starts[line] + 1; i <= Ends[line]; i++)
		{
			if (MathF.Abs(X[i] - x) < MathF.Abs(X[best] - x)) best = i;
		}

		return best;
	}
}

public sealed partial class Ui
{
	/// <summary>The space between a text area's top and its first line.</summary>
	private const float TextAreaTop = 4;

	private readonly Dictionary<char, string> _charStrings = [];

	/// <summary>
	/// Adds a multi-line text field with a caption, <paramref name="lines"/> lines tall: text wraps at the field's width
	/// (at spaces when it can), Enter starts a new line, Up and Down move between lines (keeping the caret's x), Home and End
	/// go to the start and end of a line (with Ctrl, of the text), and the field scrolls to keep the caret in view (the
	/// mouse wheel scrolls it too). Editing starts and stops as for <see cref="TextInput"/>, except that Enter does not stop
	/// it: Escape, Tab, a click elsewhere or gamepad A or B do. Selection, the clipboard and input method compositions work
	/// as in <see cref="TextInput"/>; pasted line breaks are kept. Returns true when <paramref name="value"/> changed.
	/// </summary>
	/// <param name="label">The caption, also the path segment unless <paramref name="key"/> is given.</param>
	/// <param name="value">The text, read and written; lines are separated by <c>\n</c>.</param>
	/// <param name="maxLength">The maximum number of characters, line breaks included.</param>
	/// <param name="lines">The number of lines shown.</param>
	/// <param name="key">The path segment and identity, or null.</param>
	/// <param name="style">Layout.</param>
	/// <param name="file">The call site (filled in by the compiler).</param>
	/// <param name="line">The call site (filled in by the compiler).</param>
	public bool TextArea(string label, ref string value, int maxLength = 1024, int lines = 4, string? key = null, in UiStyle style = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
	{
		if (!_inFrame) _notInFrame();
		maxLength = Math.Max(1, maxLength);
		value ??= string.Empty;
		var id = _id(key, file, line);
		ref var node = ref _add(UiNodeKind.TextArea, id, style, key ?? label, NodeFlags.Focusable | NodeFlags.Blocks);
		var state = node.State;
		state.Multiline = true;
		state.VisibleLines = Math.Max(1, lines);
		var changed = _editText(ref node, ref value, maxLength);
		node.Text = label;
		node.Value = value;
		node.Number = maxLength;
		node.TextSize = MeasureText(label);
		node.LabelWidth = _theme.LabelWidth > 0 ? _theme.LabelWidth : node.TextSize.X;
		node.Measured = new Vector2(node.LabelWidth + _gap + _theme.InputWidth, state.VisibleLines * MeasureText(null).Y + 2 * TextAreaTop);
		return changed;
	}

	/// <summary>The width of one character with the theme's font (measured once per character and font).</summary>
	private float _advance(char c)
	{
		if (c is '\n' or '\r') return 0;
		if (!_charStrings.TryGetValue(c, out var s))
		{
			s = c.ToString();
			_charStrings[c] = s;
		}

		return MeasureText(s).X;
	}

	/// <summary>Where a text area's text goes: its left edge, its first line's top, the wrapping width and the line height.</summary>
	private (float X, float Top, float Width, float LineHeight) _areaGeometry(in UiNode node)
	{
		var fieldX = node.Rect.X + node.LabelWidth + _theme.Gap;
		var fieldWidth = MathF.Max(0, node.Rect.X + node.Rect.Width - fieldX);
		return (fieldX + TextInputPadding, node.Rect.Y + TextAreaTop, MathF.Max(1, fieldWidth - 2 * TextInputPadding), MeasureText(null).Y);
	}

	/// <summary>
	/// The wrapped lines of <paramref name="text"/> at <paramref name="width"/>, cached in the state: a line breaks at a line
	/// break, or before the character that would overflow (after the last space on the line when there is one).
	/// </summary>
	private TextAreaLayout _areaLayout(UiNodeState state, ReadOnlySpan<char> text, float width)
	{
		var layout = state.Area ??= new TextAreaLayout();
		if (layout.Width == width && ReferenceEquals(layout.Font, _theme.Font) && text.SequenceEqual(layout.Source)) return layout;

		var x = new float[text.Length + 1];
		var starts = new List<int>();
		var ends = new List<int>();
		var lineStart = 0;
		var lastBreak = -1;
		var position = 0f;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (c == '\n')
			{
				x[i] = position;
				starts.Add(lineStart);
				ends.Add(i);
				lineStart = i + 1;
				lastBreak = -1;
				position = 0;
				continue;
			}

			var advance = _advance(c);
			if (position + advance > width && i > lineStart)
			{
				var breakAt = lastBreak > lineStart ? lastBreak : i;
				starts.Add(lineStart);
				ends.Add(breakAt);
				lineStart = breakAt;
				lastBreak = -1;
				// Re-measure the carried-over characters from the new line's start.
				position = 0;
				for (var j = breakAt; j < i; j++)
				{
					x[j] = position;
					position += _advance(text[j]);
				}
			}

			x[i] = position;
			position += advance;
			if (c == ' ') lastBreak = i + 1;
		}

		x[text.Length] = position;
		starts.Add(lineStart);
		ends.Add(text.Length);

		layout.Source = new string(text);
		layout.Width = width;
		layout.Font = _theme.Font;
		layout.X = x;
		layout.Starts = [.. starts];
		layout.Ends = [.. ends];
		layout.Count = starts.Count;
		layout.Texts = new string[layout.Count];
		for (var i = 0; i < layout.Count; i++) layout.Texts[i] = layout.Source[layout.Starts[i]..layout.Ends[i]];
		return layout;
	}

	/// <summary>The layout of a text area's own text (no composition) at its current width.</summary>
	private TextAreaLayout _areaLayoutOf(in UiNode node)
	{
		var state = node.State;
		var text = state.Buffer is null ? default : state.Buffer.AsSpan(0, state.Length);
		return _areaLayout(state, text, _areaGeometry(node).Width);
	}

	/// <summary>The caret position of a text area (of the previous layout) under <paramref name="point"/>.</summary>
	private int _areaIndexAt(int index, Vector2 point)
	{
		ref var node = ref _prev[index];
		var (x, top, _, lineHeight) = _areaGeometry(node);
		var layout = _areaLayoutOf(node);
		var line = node.State.ScrollLine + (int)MathF.Floor((point.Y - top) / MathF.Max(1, lineHeight));
		return layout.IndexAt(line, point.X - x);
	}

	/// <summary>
	/// Up or Down in a text area: the caret goes to the nearest position on the line above or below at the x it had when
	/// vertical movement began (to the text's start or end past the first or last line).
	/// </summary>
	private void _areaMoveVertical(int index, bool down, bool extend)
	{
		ref var node = ref _prev[index];
		var state = node.State;
		var layout = _areaLayoutOf(node);
		var line = layout.LineOf(state.Caret);
		if (float.IsNaN(state.DesiredX)) state.DesiredX = layout.X[Math.Clamp(state.Caret, 0, layout.X.Length - 1)];
		var desired = state.DesiredX;
		var target = down ? line + 1 : line - 1;
		var to = target < 0 ? 0 : target >= layout.Count ? state.Length : layout.IndexAt(target, desired);
		state.MoveCaret(to, extend);
		state.DesiredX = desired;
	}

	/// <summary>Home or End in a text area: the start or end of the caret's visual line.</summary>
	private void _areaLineEdge(int index, bool end, bool extend)
	{
		ref var node = ref _prev[index];
		var state = node.State;
		var layout = _areaLayoutOf(node);
		var line = layout.LineOf(state.Caret);
		state.MoveCaret(end ? layout.Ends[line] : layout.Starts[line], extend);
	}

	/// <summary>Scrolls a text area by <paramref name="lines"/> (positive: down), within its text.</summary>
	private void _areaScroll(in UiNode node, int lines)
	{
		var state = node.State;
		var layout = _areaLayoutOf(node);
		state.ScrollLine = Math.Clamp(state.ScrollLine + lines, 0, Math.Max(0, layout.Count - state.VisibleLines));
	}

	/// <summary>After this frame's layout: scrolls the text area being edited so its caret is visible, when the caret or the text changed.</summary>
	private void _followAreaCaret()
	{
		if (_editingId == 0) return;
		for (var i = 1; i < _count; i++)
		{
			ref var node = ref _nodes[i];
			if (node.Id != _editingId) continue;
			if (node.Kind != UiNodeKind.TextArea) return;
			var state = node.State;
			var layout = _areaLayoutOf(node);
			var caret = Math.Clamp(state.Caret, 0, state.Length);
			if (state.FollowCaret == caret && ReferenceEquals(state.FollowSource, layout.Source)) return;
			state.FollowCaret = caret;
			state.FollowSource = layout.Source;
			var line = layout.LineOf(caret);
			if (line < state.ScrollLine) state.ScrollLine = line;
			else if (line >= state.ScrollLine + state.VisibleLines) state.ScrollLine = line - state.VisibleLines + 1;
			state.ScrollLine = Math.Clamp(state.ScrollLine, 0, Math.Max(0, layout.Count - state.VisibleLines));
			return;
		}
	}

	/// <summary>Where a text area's caret is drawn (composition included): its x, its line's top and the line height.</summary>
	private (float X, float Y, float Height) _areaCaretGeometry(ref UiNode node)
	{
		var state = node.State;
		var (x, top, width, lineHeight) = _areaGeometry(node);
		var composition = node.Id == _editingId ? _input.Composition : string.Empty;
		TextAreaLayout layout;
		int caret;
		if (composition.Length > 0)
		{
			layout = _areaLayout(state, _composedText(state, composition), width);
			caret = state.Caret + _input.CompositionCursor;
		}
		else
		{
			layout = _areaLayoutOf(node);
			caret = state.Caret;
		}

		caret = Math.Clamp(caret, 0, layout.X.Length - 1);
		var line = layout.LineOf(caret);
		return (x + layout.X[caret], top + (line - state.ScrollLine) * lineHeight, lineHeight);
	}

	private void _drawTextArea(ISpriteBatch batch, ref UiNode node, Color textColor)
	{
		var theme = _theme;
		var rect = node.Rect;
		var state = node.State;
		_text(batch, node.Text, new Vector2(rect.X, rect.Y + TextAreaTop), textColor, 1f);
		var fieldX = rect.X + node.LabelWidth + theme.Gap;
		batch.DrawRect(theme.InputBackground, new RectangleF(fieldX, rect.Y, MathF.Max(0, rect.X + rect.Width - fieldX), rect.Height));

		var (x, top, width, lineHeight) = _areaGeometry(node);
		var editing = node.Id == _editingId;
		var composition = editing ? _input.Composition : string.Empty;
		var composing = composition.Length > 0;
		var layout = composing
			? _areaLayout(state, _composedText(state, composition), width)
			: _areaLayout(state, state.Buffer is null ? default : state.Buffer.AsSpan(0, state.Length), width);
		var first = Math.Clamp(state.ScrollLine, 0, Math.Max(0, layout.Count - 1));
		var last = Math.Min(layout.Count, first + state.VisibleLines);

		// A highlight or underline over [from, to) of the text, one rectangle per visual line it touches.
		void Span(int from, int to, Color color, bool underline)
		{
			for (var line = first; line < last; line++)
			{
				var start = Math.Max(from, layout.Starts[line]);
				var end = Math.Min(to, layout.Ends[line]);
				if (end < start || (end == start && from != to)) continue;
				var y = top + (line - first) * lineHeight;
				var left = layout.X[start];
				var right = layout.X[end];
				// A selected line break shows as a sliver, so empty selected lines are visible.
				if (!underline && to > layout.Ends[line] && layout.Ends[line] < layout.Source.Length) right += 4;
				if (right <= left) continue;
				batch.DrawRect(color, underline ? new RectangleF(x + left, y + lineHeight - 2, right - left, 1) : new RectangleF(x + left, y, right - left, lineHeight));
			}
		}

		if (editing && !composing && state.HasSelection) Span(state.SelectionStart, state.SelectionEnd, theme.TextSelection, underline: false);
		for (var line = first; line < last; line++) _text(batch, layout.Texts[line], new Vector2(x, top + (line - first) * lineHeight), textColor, 1f);
		if (composing) Span(state.Caret, state.Caret + composition.Length, textColor, underline: true);

		if (editing)
		{
			var (caretX, caretY, height) = _areaCaretGeometry(ref node);
			if (caretY >= top - 0.5f && caretY < top + state.VisibleLines * lineHeight - 0.5f) batch.DrawRect(theme.Accent, new RectangleF(caretX, caretY, 2, height));
		}
	}
}
