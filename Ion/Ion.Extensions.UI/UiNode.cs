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
	public string? CaretPrefix;
	public int CaretPrefixLength = -1;

	// Values set through the tree, applied by the next widget call.
	public long PendingFrame;
	public bool HasPending;
	public bool PendingBool;
	public float PendingFloat;
	public string? PendingString;

	// Formatted numeric value for the tree.
	public float FormattedNumber = float.NaN;
	public string? FormattedText;

	public void SetText(string value, int maxLength)
	{
		var capacity = Math.Max(maxLength, 1);
		if (Buffer is null || Buffer.Length < capacity) Buffer = new char[capacity];
		Length = Math.Min(value.Length, Buffer.Length);
		value.AsSpan(0, Length).CopyTo(Buffer);
		Caret = Math.Clamp(Caret, 0, Length);
		Text = value;
	}

	public void Insert(ReadOnlySpan<char> text)
	{
		if (Buffer is null) return;
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
		if (Buffer is null || Caret == 0) return;
		Array.Copy(Buffer, Caret, Buffer, Caret - 1, Length - Caret);
		Caret--;
		Length--;
		Edited = true;
	}

	public void Delete()
	{
		if (Buffer is null || Caret >= Length) return;
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
