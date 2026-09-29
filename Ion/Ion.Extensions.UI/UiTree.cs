namespace Ion.Extensions.UI;

internal enum UiCommandKind : byte
{
	Click,
	SetValue,
	Focus,
	Type,
	Back,
}

internal readonly record struct UiCommand(UiCommandKind Kind, string? Path, string? Value);

/// <summary>
/// The <see cref="IUiTree"/> of a <see cref="Ui"/>: the published node list (rebuilt at the end of every frame, under a
/// lock so <see cref="Snapshot"/> and the commands can be used from other threads) and the command queue (drained at the
/// start of the next frame).
/// </summary>
internal sealed class UiTree(Ui ui) : IUiTree
{
	private readonly Lock _sync = new();
	private readonly List<UiCommand> _queue = [];
	private readonly List<UiCommand> _applying = [];
	private UiNodeInfo[] _nodes = new UiNodeInfo[64];
	private int _count;
	private long _frame;
	private int _version;
	private string? _focusedPath;

	public long Frame
	{
		get
		{
			lock (_sync) return _frame;
		}
	}

	public int Version
	{
		get
		{
			lock (_sync) return _version;
		}
	}

	public int Count
	{
		get
		{
			lock (_sync) return _count;
		}
	}

	public UiNodeInfo this[int index]
	{
		get
		{
			lock (_sync)
			{
				ArgumentOutOfRangeException.ThrowIfNegative(index);
				ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
				return _nodes[index];
			}
		}
	}

	public string? FocusedPath
	{
		get
		{
			lock (_sync) return _focusedPath;
		}
	}

	/// <summary>The number of commands waiting for the next frame.</summary>
	public int PendingCommands
	{
		get
		{
			lock (_sync) return _queue.Count;
		}
	}

	public bool TryFind(string path, out UiNodeInfo node)
	{
		ArgumentNullException.ThrowIfNull(path);
		lock (_sync)
		{
			var index = _find(path);
			node = index >= 0 ? _nodes[index] : default;
			return index >= 0;
		}
	}

	public UiNodeInfo[] Snapshot()
	{
		lock (_sync) return _nodes.AsSpan(0, _count).ToArray();
	}

	public bool Click(string path) => _enqueue(UiCommandKind.Click, path, null, static n => n.Kind is UiNodeKind.Button or UiNodeKind.Toggle or UiNodeKind.ListItem or UiNodeKind.TextInput);

	public bool SetValue(string path, string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return _enqueue(UiCommandKind.SetValue, path, value, static n => n.Kind is UiNodeKind.Toggle or UiNodeKind.Slider or UiNodeKind.TextInput or UiNodeKind.List);
	}

	public bool Focus(string path) => _enqueue(UiCommandKind.Focus, path, null, static n => n.Focusable);

	public bool Type(string path, string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return _enqueue(UiCommandKind.Type, path, text, static n => n.Kind == UiNodeKind.TextInput);
	}

	public void Back()
	{
		lock (_sync) _queue.Add(new UiCommand(UiCommandKind.Back, null, null));
	}

	private bool _enqueue(UiCommandKind kind, string path, string? value, Func<UiNodeInfo, bool> applies)
	{
		ArgumentNullException.ThrowIfNull(path);
		lock (_sync)
		{
			var index = _find(path);
			if (index < 0) return false;
			ref readonly var node = ref _nodes[index];
			if (!node.Enabled || !applies(node)) return false;
			if (kind == UiCommandKind.SetValue && node.Kind == UiNodeKind.Toggle && !bool.TryParse(value, out _)) return false;
			if (kind == UiCommandKind.SetValue && node.Kind == UiNodeKind.Slider
				&& !float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
			{
				return false;
			}

			_queue.Add(new UiCommand(kind, path, value));
			return true;
		}
	}

	private int _find(string path)
	{
		for (var i = 0; i < _count; i++)
		{
			if (string.Equals(_nodes[i].Path, path, StringComparison.Ordinal)) return i;
		}

		return -1;
	}

	/// <summary>Applies the queued commands, in order. Called at the start of a frame, before input.</summary>
	internal void Apply()
	{
		lock (_sync)
		{
			if (_queue.Count == 0) return;
			_applying.AddRange(_queue);
			_queue.Clear();
		}

		foreach (var command in _applying) ui.ApplyCommand(command.Kind, command.Path, command.Value);
		_applying.Clear();
	}

	/// <summary>Rebuilds the published nodes from the frame just laid out.</summary>
	internal void Publish()
	{
		var nodes = ui.Nodes;
		var focus = ui.FocusId;
		var focusedPath = default(string);
		lock (_sync)
		{
			var count = Math.Max(0, nodes.Length - 1);
			if (_nodes.Length < count) Array.Resize(ref _nodes, Math.Max(count, _nodes.Length * 2));
			var changed = count != _count;
			for (var i = 1; i < nodes.Length; i++)
			{
				ref readonly var node = ref nodes[i];
				var path = node.State.Path!;
				if (!changed && !ReferenceEquals(_nodes[i - 1].Path, path)) changed = true;
				var focused = node.Id == focus && focus != 0;
				if (focused) focusedPath = path;
				var rect = node.Rect;
				_nodes[i - 1] = new UiNodeInfo(
					path,
					node.Kind,
					node.Text,
					new UiRect(rect.X, rect.Y, rect.Width, rect.Height),
					node.Is(NodeFlags.Enabled),
					node.Is(NodeFlags.Focusable),
					focused,
					node.Is(NodeFlags.Visible),
					node.Value,
					node.Depth,
					node.Parent - 1);
			}

			for (var i = count; i < _count; i++) _nodes[i] = default;
			_count = count;
			_frame = ui.Frame;
			_focusedPath = focusedPath;
			if (changed) _version++;
		}
	}
}
