using System.Globalization;
using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

[Flags]
internal enum NodeFlags : ushort
{
	None = 0,
	Enabled = 1 << 0,
	Focusable = 1 << 1,
	Container = 1 << 2,
	Clips = 1 << 3,
	Visible = 1 << 4,
	Blocks = 1 << 5,
	On = 1 << 6,
}

/// <summary>One node of a UI frame: what the widget call recorded, then what layout computed.</summary>
internal struct UiNode
{
	public uint Id;
	public UiNodeKind Kind;
	public NodeFlags Flags;
	public int Parent;
	public int FirstChild;
	public int LastChild;
	public int NextSibling;
	public int ChildCount;
	public int Depth;
	public UiStyle Style;
	public UiNodeState State;
	public string? Text;
	public string? Value;
	public float Number;
	public float TextScale;
	public Color? TextColor;
	public Vector2 TextSize;
	/// <summary>The width of the caption part (sliders, text inputs).</summary>
	public float LabelWidth;
	/// <summary>Extra space at the start of the main axis before the children (a list's caption).</summary>
	public float Inset;
	public Vector2 Measured;
	public RectangleF Rect;
	public RectangleF Clip;

	public readonly bool Is(NodeFlags flag) => (Flags & flag) != 0;
}

/// <summary>Per-widget state kept across frames, keyed by the widget id.</summary>
internal sealed class UiNodeState(uint id)
{
	public readonly uint Id = id;
	public long LastFrame;

	/// <summary>The node's index in the frame it was last recorded in.</summary>
	public int Index;

	// Path cache: recomputed only when an input changes.
	public string? Path;
	public string? SegmentSource;
	public string? ParentPath;
	public int Duplicate;

	// Scroll views.
	public float Scroll;
	public float ScrollMax;

	// Sliders: the track of the last layout, for dragging.
	public float TrackX;
	public float TrackWidth;
	public int PendingSteps;

	// Text inputs.
	public char[]? Buffer;
	public int Length;
	public int Caret;
	public string? Text;
	public bool Edited;

	// Values set through the tree, applied by the next widget call.
	public long PendingFrame;
	public bool HasPending;
	public bool PendingBool;
	public float PendingFloat;
	public string? PendingString;

	// Formatted numeric value for the tree.
	public float FormattedNumber = float.NaN;
	public string? FormattedText;

	// The selection of a text input: from the anchor to the caret (none when the anchor is -1 or at the caret).
	public int Anchor = -1;

	// The x offset of every caret position of the text in WidthsSource (Widths[i]: the width of the first i characters).
	public float[]? Widths;
	public string? WidthsSource;
	public object? WidthsFont;

	// The text shown while an input method composes: the text with the composition at the caret, and its widths.
	public string? Composed;
	public string? ComposedFor;
	public string? ComposedComposition;
	public int ComposedCaret = -1;
	public float[]? ComposedWidths;
	public object? ComposedFont;

	public bool HasSelection => Anchor >= 0 && Anchor != Caret;

	public int SelectionStart => HasSelection ? Math.Min(Anchor, Caret) : Caret;

	public int SelectionEnd => HasSelection ? Math.Max(Anchor, Caret) : Caret;

	public ReadOnlySpan<char> SelectedText => Buffer is null ? default : Buffer.AsSpan(SelectionStart, SelectionEnd - SelectionStart);

	public void SetText(string value, int maxLength)
	{
		var capacity = Math.Max(maxLength, 1);
		if (Buffer is null || Buffer.Length < capacity) Buffer = new char[capacity];
		Length = Math.Min(value.Length, Buffer.Length);
		value.AsSpan(0, Length).CopyTo(Buffer);
		Caret = Math.Clamp(Caret, 0, Length);
		Anchor = -1;
		Text = value;
	}

	/// <summary>Moves the caret to <paramref name="to"/>; with <paramref name="extend"/> the selection grows from where the caret was (shift held), otherwise it is dropped.</summary>
	public void MoveCaret(int to, bool extend)
	{
		to = Math.Clamp(to, 0, Length);
		if (extend)
		{
			if (Anchor < 0) Anchor = Caret;
		}
		else
		{
			Anchor = -1;
		}

		Caret = to;
	}

	public void Select(int start, int end)
	{
		Anchor = Math.Clamp(start, 0, Length);
		Caret = Math.Clamp(end, 0, Length);
	}

	public void SelectAll() => Select(0, Length);

	/// <summary>Removes the selected text, leaving the caret where it started. Returns false when nothing was selected.</summary>
	public bool DeleteSelection()
	{
		if (Buffer is null || !HasSelection) return false;
		var start = SelectionStart;
		var end = SelectionEnd;
		Array.Copy(Buffer, end, Buffer, start, Length - end);
		Length -= end - start;
		Caret = start;
		Anchor = -1;
		Edited = true;
		return true;
	}

	/// <summary>The kind of a character for word movement: 0 space, 1 a word character (letter, digit, underscore), 2 anything else.</summary>
	private static int _kind(char c) => char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;

	/// <summary>Where Ctrl+Left goes from <paramref name="from"/>: back over spaces, then to the start of the word (or run of punctuation) before them.</summary>
	public int WordStart(int from)
	{
		if (Buffer is null) return 0;
		var i = Math.Clamp(from, 0, Length);
		while (i > 0 && _kind(Buffer[i - 1]) == 0) i--;
		if (i == 0) return 0;
		var kind = _kind(Buffer[i - 1]);
		while (i > 0 && _kind(Buffer[i - 1]) == kind) i--;
		return i;
	}

	/// <summary>Where Ctrl+Right goes from <paramref name="from"/>: over spaces, then to the end of the word (or run of punctuation) after them.</summary>
	public int WordEnd(int from)
	{
		if (Buffer is null) return 0;
		var i = Math.Clamp(from, 0, Length);
		while (i < Length && _kind(Buffer[i]) == 0) i++;
		if (i == Length) return Length;
		var kind = _kind(Buffer[i]);
		while (i < Length && _kind(Buffer[i]) == kind) i++;
		return i;
	}

	/// <summary>The run of one kind of character (a word, spaces, punctuation) at <paramref name="index"/>: what a double click selects.</summary>
	public (int Start, int End) WordAt(int index)
	{
		if (Buffer is null || Length == 0) return (0, 0);
		// The character after the caret position, or before it at a word's end (a click on the right half of a word's last
		// character lands on the position after it, and should still pick the word).
		var at = Math.Clamp(index, 0, Length - 1);
		if (at > 0 && _kind(Buffer[at]) != 1 && _kind(Buffer[at - 1]) == 1) at--;
		var kind = _kind(Buffer[at]);
		var start = at;
		var end = at + 1;
		while (start > 0 && _kind(Buffer[start - 1]) == kind) start--;
		while (end < Length && _kind(Buffer[end]) == kind) end++;
		return (start, end);
	}

	public void Insert(ReadOnlySpan<char> text)
	{
		if (Buffer is null || text.IsEmpty) return;
		DeleteSelection();
		foreach (var c in text)
		{
			if (char.IsControl(c) || Length >= Buffer.Length) continue;
			Array.Copy(Buffer, Caret, Buffer, Caret + 1, Length - Caret);
			Buffer[Caret++] = c;
			Length++;
			Edited = true;
		}
	}

	public void Backspace()
	{
		if (DeleteSelection() || Buffer is null || Caret == 0) return;
		Array.Copy(Buffer, Caret, Buffer, Caret - 1, Length - Caret);
		Caret--;
		Length--;
		Edited = true;
	}

	public void Delete()
	{
		if (DeleteSelection() || Buffer is null || Caret >= Length) return;
		Array.Copy(Buffer, Caret + 1, Buffer, Caret, Length - Caret - 1);
		Length--;
		Edited = true;
	}
}

/// <summary>
/// Interns short strings built from spans (formatted numbers, span labels), so repeating content costs a lookup and no
/// allocation. Bounded: cleared when it grows past <see cref="Limit"/> entries.
/// </summary>
internal sealed class UiStrings
{
	public const int Limit = 4096;

	private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

	public UiStrings() => _lookup = _strings.GetAlternateLookup<ReadOnlySpan<char>>();

	public int Count => _strings.Count;

	public string Intern(ReadOnlySpan<char> text)
	{
		if (text.IsEmpty) return string.Empty;
		if (_lookup.TryGetValue(text, out var existing)) return existing;
		if (_strings.Count >= Limit) _strings.Clear();
		var created = text.ToString();
		_strings[created] = created;
		return created;
	}

	public string Format(float value)
	{
		Span<char> buffer = stackalloc char[32];
		return value.TryFormat(buffer, out var written, "0.###", CultureInfo.InvariantCulture) ? Intern(buffer[..written]) : value.ToString(CultureInfo.InvariantCulture);
	}
}
