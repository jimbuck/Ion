namespace Ion.Extensions.UI;

/// <summary>
/// The kind of a node of the UI tree.
/// </summary>
public enum UiNodeKind : byte
{
	/// <summary>A container with a background (<c>Ui.Panel</c>).</summary>
	Panel,
	/// <summary>A transparent container laying out its children left to right (<c>Ui.Row</c>).</summary>
	Row,
	/// <summary>A transparent container laying out its children top to bottom (<c>Ui.Column</c>).</summary>
	Column,
	/// <summary>A container that clips its children and scrolls them (<c>Ui.ScrollView</c>). <see cref="UiNodeInfo.Value"/> is the scroll offset in pixels.</summary>
	ScrollView,
	/// <summary>Static text (<c>Ui.Label</c>).</summary>
	Label,
	/// <summary>A button (<c>Ui.Button</c>). Click activates it.</summary>
	Button,
	/// <summary>A checkbox (<c>Ui.Toggle</c>). <see cref="UiNodeInfo.Value"/> is <c>"true"</c> or <c>"false"</c>; Click flips it.</summary>
	Toggle,
	/// <summary>A slider (<c>Ui.Slider</c>). <see cref="UiNodeInfo.Value"/> is the value, invariant culture.</summary>
	Slider,
	/// <summary>A single-line text field (<c>Ui.TextInput</c>). <see cref="UiNodeInfo.Value"/> is the text.</summary>
	TextInput,
	/// <summary>A single-selection list (<c>Ui.List</c>). <see cref="UiNodeInfo.Value"/> is the selected item's text; its items are <see cref="ListItem"/> children.</summary>
	List,
	/// <summary>An item of a <see cref="List"/>. <see cref="UiNodeInfo.Value"/> is <c>"true"</c> when selected; Click selects it.</summary>
	ListItem,
	/// <summary>Empty space (<c>Ui.Spacer</c>).</summary>
	Spacer,
}

/// <summary>
/// A rectangle in window pixels (origin top-left).
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
	/// <summary>The right edge.</summary>
	public float Right => X + Width;

	/// <summary>The bottom edge.</summary>
	public float Bottom => Y + Height;

	/// <summary>The x coordinate of the center.</summary>
	public float CenterX => X + Width * 0.5f;

	/// <summary>The y coordinate of the center.</summary>
	public float CenterY => Y + Height * 0.5f;

	/// <summary>True when the point lies inside (left and top edges inclusive, right and bottom exclusive).</summary>
	public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// One node of the UI tree as the last UI frame built it.
/// </summary>
/// <param name="Path">
/// The node's path: the segments of its ancestors and its own joined with <c>/</c>. A segment is the widget's explicit key,
/// else its caption (buttons, toggles, sliders, text inputs, lists and list items; a <c>/</c> in it becomes <c>_</c>), else
/// the kind in lower case (<c>panel</c>, <c>row</c>, <c>label</c>, ...). A segment repeated among siblings gets a suffix
/// <c>#2</c>, <c>#3</c>, ... in call order. Paths are stable across frames as long as the same calls are made.
/// </param>
/// <param name="Kind">The widget kind.</param>
/// <param name="Text">The caption or text shown, or null.</param>
/// <param name="Rect">The layout rectangle in window pixels (not clipped; see <paramref name="Visible"/>).</param>
/// <param name="Enabled">False inside a disabled scope: the node ignores pointer, focus and tree commands.</param>
/// <param name="Focusable">Whether the node can take focus (interactive widgets and list items).</param>
/// <param name="Focused">Whether the node has the focus.</param>
/// <param name="Visible">False when scrolled entirely out of an enclosing scroll view.</param>
/// <param name="Value">The widget's value as text (see <see cref="UiNodeKind"/>), or null.</param>
/// <param name="Depth">The nesting depth: 0 for the top-level nodes.</param>
/// <param name="Parent">The index of the parent node in the tree, or -1 for a top-level node.</param>
public readonly record struct UiNodeInfo(
	string Path,
	UiNodeKind Kind,
	string? Text,
	UiRect Rect,
	bool Enabled,
	bool Focusable,
	bool Focused,
	bool Visible,
	string? Value,
	int Depth,
	int Parent);

/// <summary>
/// The inspectable, remotely drivable view of the UI: the nodes the last UI frame built, in pre-order (parents before
/// children, siblings in call order), and commands that act on them by path.
/// </summary>
/// <remarks>
/// <para>
/// The tree is rebuilt at the end of every frame's Update from the widget calls of that frame, so it describes exactly what
/// the game draws and what pointer hit testing uses. Reading members (<see cref="Count"/>, the indexer, <see cref="TryFind"/>,
/// <see cref="FocusedPath"/>) is meant for the game thread; <see cref="Snapshot"/> may be called from any thread.
/// </para>
/// <para>
/// Commands (<see cref="Click"/>, <see cref="SetValue"/>, <see cref="Focus"/>, <see cref="Type"/>, <see cref="Back"/>) are
/// thread-safe. They are checked against the current tree when called (the method returns false, and nothing is queued,
/// when the path does not exist, the node is disabled or the command does not apply to its kind), queued, and applied in
/// order at the start of the next frame's Update, before any widget call, exactly as the corresponding input would be: a
/// clicked button returns true from its <c>Ui.Button</c> call in that frame. The effect is therefore visible in the tree
/// published at the end of that frame.
/// </para>
/// </remarks>
public interface IUiTree
{
	/// <summary>The number of UI frames built so far; the tree describes the last one.</summary>
	long Frame { get; }

	/// <summary>Incremented whenever the set of paths (or their order) changes from one frame to the next.</summary>
	int Version { get; }

	/// <summary>The number of nodes.</summary>
	int Count { get; }

	/// <summary>The node at <paramref name="index"/>, in pre-order.</summary>
	UiNodeInfo this[int index] { get; }

	/// <summary>The path of the focused node, or null.</summary>
	string? FocusedPath { get; }

	/// <summary>Finds the node with <paramref name="path"/> (exact, case-sensitive).</summary>
	bool TryFind(string path, out UiNodeInfo node);

	/// <summary>A copy of every node, safe to call from any thread.</summary>
	UiNodeInfo[] Snapshot();

	/// <summary>
	/// Queues a click on the node: a button reports it as clicked, a toggle flips, a list item is selected, a text input
	/// takes focus and starts editing. Also focuses the node.
	/// </summary>
	bool Click(string path);

	/// <summary>
	/// Queues setting the value of a toggle (<c>true</c>/<c>false</c>), slider (a number, invariant culture, clamped and
	/// snapped to its step), text input (the text, cut to its maximum length) or list (an item's text, or its index).
	/// </summary>
	bool SetValue(string path, string value);

	/// <summary>Queues moving the focus to the node (which must be focusable).</summary>
	bool Focus(string path);

	/// <summary>
	/// Queues typing <paramref name="text"/> into a text input: it takes focus and starts editing, and the text is inserted
	/// at the caret (at the end unless the game's input moved it), as keyboard text input would be.
	/// </summary>
	bool Type(string path, string text);

	/// <summary>
	/// Queues a back action, as gamepad B or Escape: a text input being edited stops editing, otherwise the frame reports
	/// <c>Ui.BackPressed</c>.
	/// </summary>
	void Back();
}
