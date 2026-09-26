using System.Numerics;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>
/// The immediate-mode UI context. Inject it into a system and call its widget methods from Update steps: every frame the
/// calls describe the whole UI, and interactive widgets report what happened to them (<see cref="Button"/> returns true
/// when clicked, <see cref="Toggle"/> flips its <c>ref bool</c>, ...).
/// </summary>
/// <remarks>
/// <para>
/// The UI system (<c>UseUi</c>) brackets Update with <see cref="BeginFrame"/> and <see cref="EndFrame"/> at
/// <see cref="StageOrder.UiFrame"/> and draws at <see cref="StageOrder.Ui"/> in Render. <see cref="BeginFrame"/> applies
/// queued <see cref="IUiTree"/> commands and reads input (pointer, keyboard, gamepad) against the previous frame's layout,
/// the retained hit-test tree; the widget calls record nodes; <see cref="EndFrame"/> lays them out (flex-style, see
/// <see cref="UiStyle"/>), rebuilds the hit-test tree and publishes the inspectable tree (<see cref="Tree"/>). Widgets
/// therefore react to input one frame after they first appear.
/// </para>
/// <para>
/// Containers (<see cref="Panel"/>, <see cref="Row"/>, <see cref="Column"/>, <see cref="ScrollView"/>) return a
/// <see cref="UiScope"/>: dispose it (<c>using (ui.Panel()) { ... }</c>) or call <see cref="End"/>. A widget's identity
/// comes from its explicit key when given, else from its call site (file and line) and its parent, with repeated calls
/// from one site (a loop) told apart by call order; its path in the tree from the key, else its caption, else its kind.
/// </para>
/// <para>
/// Nothing is allocated per frame once the widgets and their strings have been seen: nodes live in arrays reused every
/// frame, per-widget state is created when a widget first appears, and paths, measured text and formatted values are cached.
/// </para>
/// </remarks>
public sealed partial class Ui
{
	private const uint RootId = 0x510BA1u;
	private const int InitialCapacity = 64;

	private readonly IInputState _input;
	private readonly UiOptions _options;
	private readonly ILogger? _logger;
	private readonly UiTree _tree;
	private readonly UiStrings _strings = new();
	private readonly Dictionary<uint, UiNodeState> _states = [];
	private readonly Dictionary<(uint Parent, string Segment), int> _segments = [];
	private readonly Dictionary<uint, int> _repeats = [];
	private readonly List<uint> _evict = [];
	private readonly Dictionary<string, Vector2> _measured = new(StringComparer.Ordinal);

	private UiNode[] _nodes = new UiNode[InitialCapacity];
	private int _count;
	private UiNode[] _prev = new UiNode[InitialCapacity];
	private int _prevCount;
	private float[] _scratch = new float[InitialCapacity];

	private UiTheme _theme;
	private IFont? _measureFont;
	private long _frame;
	private bool _inFrame;
	private int _current;
	private int _disabled;
	private Vector2 _viewport;

	private string? _lastFile;
	private int _lastFileHash;

	/// <summary>
	/// Creates a UI context reading <paramref name="input"/>. The UI module registers one per application (<c>AddUi</c>);
	/// tests and tools may create their own and drive <see cref="BeginFrame"/>, <see cref="EndFrame"/> and <see cref="Draw"/>.
	/// </summary>
	public Ui(IInputState input, UiOptions? options = null, ILogger<Ui>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(input);
		_input = input;
		_options = options ?? new UiOptions();
		_logger = logger;
		_theme = _options.Theme ?? UiTheme.Default;
		_tree = new UiTree(this);
	}

	/// <summary>The theme; changes apply from the next widget call.</summary>
	public UiTheme Theme
	{
		get => _theme;
		set => _theme = value;
	}

	/// <summary>
	/// The style of the root, the container of the top-level widgets, which always fills the viewport. Default: a column
	/// that stretches its children, at the top left. Set <c>Justify</c> and <c>AlignItems</c> to <c>Center</c> to center a menu.
	/// </summary>
	public UiStyle RootStyle { get; set; }

	/// <summary>The inspectable tree of the last frame, and the command queue (the same instance is registered as <see cref="IUiTree"/>).</summary>
	public IUiTree Tree => _tree;

	/// <summary>The number of frames begun.</summary>
	public long Frame => _frame;

	/// <summary>
	/// True when back (gamepad B, Escape, or <see cref="IUiTree.Back"/>) was pressed this frame and no widget used it (a
	/// text input being edited uses it to stop editing). Screens use it to return to the previous screen.
	/// </summary>
	public bool BackPressed { get; private set; }

	/// <summary>True when the pointer is over an interactive widget or a panel (games can then ignore the click).</summary>
	public bool IsPointerOverUi { get; private set; }

	/// <summary>True while a text input is being edited (keyboard text goes to it).</summary>
	public bool IsEditingText => _editingId != 0;

	/// <summary>The path of the focused widget, or null.</summary>
	public string? FocusedPath => _focusId != 0 && _states.TryGetValue(_focusId, out var state) ? state.Path : null;

	/// <summary>The size of the viewport of the current frame (the window).</summary>
	public Vector2 Viewport => _viewport;

	/// <summary>The number of nodes the current (or last) frame recorded, the root excluded.</summary>
	public int NodeCount => Math.Max(_count - 1, 0);

	/// <summary>
	/// Starts a UI frame: applies the queued <see cref="IUiTree"/> commands, reads input against the previous frame's
	/// layout (after <paramref name="deltaSeconds"/> for key repeat) and opens the root, which fills
	/// <paramref name="viewport"/>. Called by the UI system at the start of Update.
	/// </summary>
	public void BeginFrame(float deltaSeconds, Vector2 viewport)
	{
		if (_inFrame) EndFrame();
		_frame++;
		(_prev, _nodes) = (_nodes, _prev);
		_prevCount = _count;
		_count = 0;
		_viewport = viewport;
		_segments.Clear();
		_repeats.Clear();
		BackPressed = false;
		_clickedCount = 0;

		_tree.Apply();
		_processInput(deltaSeconds);

		_inFrame = true;
		_disabled = 0;
		ref var root = ref _add(UiNodeKind.Column, RootId, RootStyle, null, NodeFlags.Container | NodeFlags.Enabled | NodeFlags.Visible);
		root.Depth = -1;
		root.Rect = new RectangleF(0, 0, viewport.X, viewport.Y);
		root.Clip = root.Rect;
		_current = 0;
	}

	/// <summary>
	/// Ends the frame: closes any container left open (logging a warning), lays the nodes out, updates the focus and
	/// publishes the tree. Called by the UI system at the end of Update (in a <c>finally</c>, so also after a step threw).
	/// </summary>
	public void EndFrame()
	{
		if (!_inFrame) return;
		while (_current > 0)
		{
			_logger?.LogWarning("UI container {Path} was not closed; closing it at the end of the frame.", _nodes[_current].State.Path);
			_current = _nodes[_current].Parent;
		}

		_disabled = 0;
		_inFrame = false;
		_layout();
		_endInput();
		_tree.Publish();
		_evictStates();
	}

	/// <summary>
	/// Closes the innermost open container (<see cref="Panel"/>, <see cref="Row"/>, <see cref="Column"/>,
	/// <see cref="ScrollView"/>): the alternative to disposing its <see cref="UiScope"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">No container is open.</exception>
	public void End()
	{
		if (!_inFrame || _current == 0) throw new InvalidOperationException("End was called without an open container.");
		_current = _nodes[_current].Parent;
	}

	internal void CloseScope(int node, bool disabledScope)
	{
		if (disabledScope)
		{
			if (_disabled > 0) _disabled--;
			return;
		}

		if (!_inFrame) return;
		if (_current != node)
		{
			throw new InvalidOperationException($"UI scopes closed out of order: closing '{_nodes[node].State?.Path}' while '{_nodes[_current].State?.Path}' is open.");
		}

		_current = _nodes[node].Parent;
	}

	internal ReadOnlySpan<UiNode> Nodes => _nodes.AsSpan(0, _count);

	internal UiStrings Strings => _strings;

	internal IReadOnlyDictionary<uint, UiNodeState> States => _states;

	// Identity -------------------------------------------------------------------------------------------------------

	private uint _id(string? key, string file, int line)
	{
		var parent = _nodes[_current].Id;
		int hash;
		if (key is not null)
		{
			hash = HashCode.Combine(parent, key.GetHashCode());
		}
		else
		{
			if (!ReferenceEquals(file, _lastFile))
			{
				_lastFile = file;
				_lastFileHash = file.GetHashCode();
			}

			hash = HashCode.Combine(parent, _lastFileHash, line);
		}

		return _unique((uint)hash);
	}

	private uint _unique(uint id)
	{
		if (id == 0) id = 1;
		if (!_states.TryGetValue(id, out var state) || state.LastFrame != _frame) return id;

		// A repeated identity in one frame (a loop at one call site): the n-th repeat gets the n-th id of a fixed sequence
		// derived from the first, found in constant time through the per-frame repeat count.
		ref var repeats = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_repeats, id, out _);
		while (true)
		{
			var candidate = (uint)HashCode.Combine(id, ++repeats, 0x2545F491);
			if (candidate == 0) continue;
			if (!_states.TryGetValue(candidate, out state) || state.LastFrame != _frame) return candidate;
		}
	}

	// Recording ------------------------------------------------------------------------------------------------------

	private ref UiNode _add(UiNodeKind kind, uint id, in UiStyle style, string? segment, NodeFlags flags)
	{
		if (!_inFrame) _notInFrame();
		if (_count == _nodes.Length)
		{
			Array.Resize(ref _nodes, _nodes.Length * 2);
			Array.Resize(ref _scratch, _nodes.Length);
		}

		var index = _count++;
		ref var node = ref _nodes[index];
		node = default;
		node.Id = id;
		node.Kind = kind;
		node.Style = style;
		node.FirstChild = node.LastChild = node.NextSibling = -1;
		node.Parent = -1;
		node.TextScale = 1f;

		if (!_states.TryGetValue(id, out var state))
		{
			state = new UiNodeState(id);
			_states.Add(id, state);
		}

		state.LastFrame = _frame;
		state.Index = index;
		if (state.PendingFrame != _frame)
		{
			// Values set by the tree or by navigation apply only in the frame they were queued for.
			state.HasPending = false;
			state.PendingString = null;
			state.PendingSteps = 0;
		}

		node.State = state;

		if (index > 0)
		{
			ref var parent = ref _nodes[_current];
			node.Parent = _current;
			node.Depth = parent.Depth + 1;
			if (parent.LastChild >= 0) _nodes[parent.LastChild].NextSibling = index;
			else parent.FirstChild = index;
			parent.LastChild = index;
			parent.ChildCount++;
			if (_disabled == 0 && parent.Is(NodeFlags.Enabled)) flags |= NodeFlags.Enabled;
			_path(state, parent, segment ?? KindName(kind));
			if (id == _focusId && (flags & NodeFlags.Focusable) != 0 && (flags & NodeFlags.Enabled) != 0) _focusSeen = true;
		}

		node.Flags = flags;
		return ref node;
	}

	private void _path(UiNodeState state, in UiNode parent, string source)
	{
		ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_segments, (parent.Id, source), out _);
		var duplicate = ++count;
		var parentPath = parent.State.Path;
		if (state.Path is not null && state.Duplicate == duplicate && ReferenceEquals(state.ParentPath, parentPath) && string.Equals(state.SegmentSource, source, StringComparison.Ordinal))
		{
			return;
		}

		var segment = source.Contains('/') ? source.Replace('/', '_') : source;
		if (segment.Length == 0) segment = "_";
		if (duplicate > 1) segment = string.Concat(segment, "#", duplicate.ToString(System.Globalization.CultureInfo.InvariantCulture));
		state.Path = parentPath is null ? segment : string.Concat(parentPath, "/", segment);
		state.ParentPath = parentPath;
		state.SegmentSource = source;
		state.Duplicate = duplicate;
	}

	private static string KindName(UiNodeKind kind) => kind switch
	{
		UiNodeKind.Panel => "panel",
		UiNodeKind.Row => "row",
		UiNodeKind.Column => "column",
		UiNodeKind.ScrollView => "scroll",
		UiNodeKind.Label => "label",
		UiNodeKind.Button => "button",
		UiNodeKind.Toggle => "toggle",
		UiNodeKind.Slider => "slider",
		UiNodeKind.TextInput => "input",
		UiNodeKind.List => "list",
		UiNodeKind.ListItem => "item",
		_ => "spacer",
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void _notInFrame() => throw new InvalidOperationException("UI widgets can only be called between BeginFrame and EndFrame: from an Update step, with UseUi() in the schedule.");

	private UiScope _open(UiNodeKind kind, string? key, in UiStyle style, string file, int line, NodeFlags extra = NodeFlags.None)
	{
		if (!_inFrame) _notInFrame();
		var id = _id(key, file, line);
		var flags = NodeFlags.Container | extra;
		if (kind == UiNodeKind.Panel || style.Background is not null) flags |= NodeFlags.Blocks;
		_add(kind, id, style, key, flags);
		_current = _count - 1;
		return new UiScope(this, _current, false);
	}

	// Text measurement -----------------------------------------------------------------------------------------------

	/// <summary>The size of <paramref name="text"/> at <paramref name="scale"/> with the theme's font: its width, and the font's line height.</summary>
	public Vector2 MeasureText(string? text, float scale = 1f)
	{
		var font = _theme.Font;
		if (font is null)
		{
			var size = _theme.FallbackFontSize > 0 ? _theme.FallbackFontSize : 16f;
			return new Vector2((text?.Length ?? 0) * size * 0.5f, size) * scale;
		}

		if (!ReferenceEquals(font, _measureFont))
		{
			_measured.Clear();
			_measureFont = font;
		}

		if (string.IsNullOrEmpty(text)) return new Vector2(0, font.LineHeight) * scale;
		ref var slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_measured, text, out var exists);
		if (!exists)
		{
			if (_measured.Count > UiStrings.Limit)
			{
				_measured.Clear();
				slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_measured, text, out _);
			}

			slot = new Vector2(font.MeasureString(text).X, font.LineHeight);
		}

		return slot * scale;
	}

	private void _evictStates()
	{
		// States of widgets unseen for a while are dropped once there are clearly more states than widgets.
		if (_states.Count <= 2 * _count + 64) return;
		_evict.Clear();
		foreach (var (id, state) in _states)
		{
			if (_frame - state.LastFrame > 120 && id != _focusId && id != _editingId) _evict.Add(id);
		}

		foreach (var id in _evict) _states.Remove(id);
		_evict.Clear();
	}
}

/// <summary>
/// An open container or disabled scope. Dispose it to close it (<c>using (ui.Panel()) { ... }</c>); for containers,
/// <see cref="Ui.End"/> closes it too. A default instance does nothing.
/// </summary>
public readonly struct UiScope : IDisposable
{
	private readonly Ui? _ui;
	private readonly int _node;
	private readonly bool _disabled;

	internal UiScope(Ui ui, int node, bool disabled)
	{
		_ui = ui;
		_node = node;
		_disabled = disabled;
	}

	/// <summary>Closes the scope.</summary>
	/// <exception cref="InvalidOperationException">An inner container is still open.</exception>
	public void Dispose() => _ui?.CloseScope(_node, _disabled);
}
