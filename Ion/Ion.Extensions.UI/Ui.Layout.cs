using System.Numerics;
using System.Runtime.CompilerServices;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

public sealed partial class Ui
{
	private bool[] _frozen = new bool[InitialCapacity];

	/// <summary>
	/// Lays out the recorded nodes: measures bottom-up (children have higher indices than their parent, so a reverse walk
	/// sees every child before its parent), then places top-down (a forward walk places a parent before its children).
	/// </summary>
	private void _layout()
	{
		if (_frozen.Length < _nodes.Length) _frozen = new bool[_nodes.Length];
		if (_scratch.Length < _nodes.Length) _scratch = new float[_nodes.Length];

		for (var i = _count - 1; i >= 1; i--)
		{
			ref var node = ref _nodes[i];
			if (node.Is(NodeFlags.Container)) _measure(ref node);
			_constrain(ref node.Measured, node.Style);
		}

		ref var root = ref _nodes[0];
		root.Rect = new RectangleF(0, 0, _viewport.X, _viewport.Y);
		root.Clip = root.Rect;
		root.Flags |= NodeFlags.Visible;

		for (var i = 0; i < _count; i++)
		{
			ref var node = ref _nodes[i];
			if (node.FirstChild >= 0) _arrange(i);
			if (node.Kind == UiNodeKind.Slider)
			{
				node.State.TrackX = node.Rect.X + node.LabelWidth + _theme.Gap;
				node.State.TrackWidth = MathF.Max(0, node.Rect.Width - node.LabelWidth - _theme.ValueWidth - 2 * _theme.Gap);
			}
		}
	}

	private UiThickness _padding(in UiNode node)
	{
		var padding = node.Style.Padding ?? (node.Kind == UiNodeKind.Panel ? new UiThickness(_theme.PanelPadding) : default);
		return node.Inset > 0 ? padding with { Top = padding.Top + node.Inset } : padding;
	}

	private float _gapOf(in UiNode node) => node.Style.Gap ?? (node.Kind == UiNodeKind.List ? MathF.Round(_theme.Gap * 0.25f) : _theme.Gap);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float _main(Vector2 v, bool row) => row ? v.X : v.Y;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float _cross(Vector2 v, bool row) => row ? v.Y : v.X;

	private static void _constrain(ref Vector2 size, in UiStyle style)
	{
		if (style.Width > 0) size.X = style.Width;
		if (style.Height > 0) size.Y = style.Height;
		if (style.MaxWidth > 0) size.X = MathF.Min(size.X, style.MaxWidth);
		if (style.MaxHeight > 0) size.Y = MathF.Min(size.Y, style.MaxHeight);
		size.X = MathF.Max(size.X, style.MinWidth);
		size.Y = MathF.Max(size.Y, style.MinHeight);
	}

	private void _measure(ref UiNode node)
	{
		var row = node.Style.Direction == UiDirection.Row;
		var padding = _padding(node);
		var gap = _gapOf(node);
		var padMain = row ? padding.Horizontal : padding.Vertical;
		var padCross = row ? padding.Vertical : padding.Horizontal;

		var limit = row ? (node.Style.Width > 0 ? node.Style.Width : node.Style.MaxWidth) : (node.Style.Height > 0 ? node.Style.Height : node.Style.MaxHeight);
		float main = 0, cross = 0;
		if (node.Style.Wrap && limit > 0 && !node.Is(NodeFlags.Clips))
		{
			limit -= padMain;
			float lineMain = 0, lineCross = 0;
			var inLine = 0;
			var lines = 0;
			for (var c = node.FirstChild; c >= 0; c = _nodes[c].NextSibling)
			{
				var size = _nodes[c].Measured;
				var childMain = _main(size, row);
				if (inLine > 0 && lineMain + gap + childMain > limit)
				{
					main = MathF.Max(main, lineMain);
					cross += (lines++ > 0 ? gap : 0) + lineCross;
					lineMain = lineCross = 0;
					inLine = 0;
				}

				lineMain += (inLine++ > 0 ? gap : 0) + childMain;
				lineCross = MathF.Max(lineCross, _cross(size, row));
			}

			if (inLine > 0)
			{
				main = MathF.Max(main, lineMain);
				cross += (lines > 0 ? gap : 0) + lineCross;
			}
		}
		else
		{
			var n = 0;
			for (var c = node.FirstChild; c >= 0; c = _nodes[c].NextSibling)
			{
				var size = _nodes[c].Measured;
				main += _main(size, row);
				cross = MathF.Max(cross, _cross(size, row));
				n++;
			}

			if (n > 1) main += gap * (n - 1);
		}

		// A list's caption width counts towards its cross size.
		if (node.Kind == UiNodeKind.List) cross = MathF.Max(cross, node.TextSize.X);
		node.Measured = row ? new Vector2(main + padMain, cross + padCross) : new Vector2(cross + padCross, main + padMain);
	}

	private void _arrange(int index)
	{
		ref var parent = ref _nodes[index];
		var row = parent.Style.Direction == UiDirection.Row;
		var padding = _padding(parent);
		var gap = _gapOf(parent);
		var content = new RectangleF(
			parent.Rect.X + padding.Left,
			parent.Rect.Y + padding.Top,
			MathF.Max(0, parent.Rect.Width - padding.Horizontal),
			MathF.Max(0, parent.Rect.Height - padding.Vertical));
		var mainAvailable = row ? content.Width : content.Height;
		var crossAvailable = row ? content.Height : content.Width;
		var clips = parent.Is(NodeFlags.Clips);
		var childClip = clips ? RectangleF.Intersect(parent.Clip, parent.Rect) : parent.Clip;
		var wrap = parent.Style.Wrap && !clips;
		var justify = parent.Style.Justify;
		var alignItems = parent.Style.AlignItems;

		var scroll = 0f;
		if (clips)
		{
			float extent = 0;
			var n = 0;
			for (var c = parent.FirstChild; c >= 0; c = _nodes[c].NextSibling, n++) extent += _main(_nodes[c].Measured, row);
			if (n > 1) extent += gap * (n - 1);
			var state = parent.State;
			state.ScrollMax = MathF.Max(0, extent - mainAvailable);
			state.Scroll = Math.Clamp(state.Scroll, 0, state.ScrollMax);
			scroll = state.Scroll;
			parent.Value = _format(state, scroll);
		}

		var crossPosition = 0f;
		var next = parent.FirstChild;
		while (next >= 0)
		{
			// Collect one line (every child when not wrapping).
			var start = next;
			var count = 0;
			float used = 0, lineCross = 0, grow = 0;
			var k = next;
			while (k >= 0)
			{
				ref var child = ref _nodes[k];
				var childMain = _main(child.Measured, row);
				if (wrap && count > 0 && used + gap + childMain > mainAvailable) break;
				used += (count > 0 ? gap : 0) + childMain;
				lineCross = MathF.Max(lineCross, _cross(child.Measured, row));
				grow += child.Style.Grow;
				_scratch[k] = childMain;
				_frozen[k] = false;
				count++;
				k = child.NextSibling;
			}

			next = k;
			if (!wrap) lineCross = crossAvailable;

			// Grow: the free space is shared by grow factors; a child reaching its maximum keeps it and the rest is shared again.
			var free = mainAvailable - used;
			if (free > 0 && grow > 0)
			{
				for (var round = 0; round <= count && free > 0.001f && grow > 0; round++)
				{
					var clamped = false;
					for (var c = start; c != next; c = _nodes[c].NextSibling)
					{
						ref var child = ref _nodes[c];
						if (_frozen[c] || child.Style.Grow <= 0) continue;
						var max = row ? child.Style.MaxWidth : child.Style.MaxHeight;
						if (max > 0 && _scratch[c] + free * child.Style.Grow / grow > max)
						{
							free -= max - _scratch[c];
							grow -= child.Style.Grow;
							_scratch[c] = max;
							_frozen[c] = true;
							clamped = true;
						}
					}

					if (clamped) continue;
					for (var c = start; c != next; c = _nodes[c].NextSibling)
					{
						ref var child = ref _nodes[c];
						if (!_frozen[c] && child.Style.Grow > 0) _scratch[c] += free * child.Style.Grow / grow;
					}

					free = 0;
				}
			}

			// Justify what is left.
			var offset = 0f;
			var spacing = 0f;
			if (free > 0)
			{
				switch (justify)
				{
					case UiJustify.Center: offset = free * 0.5f; break;
					case UiJustify.End: offset = free; break;
					case UiJustify.SpaceBetween when count > 1: spacing = free / (count - 1); break;
					case UiJustify.SpaceAround: spacing = free / count; offset = spacing * 0.5f; break;
				}
			}

			var position = offset - scroll;
			for (var c = start; c != next; c = _nodes[c].NextSibling)
			{
				ref var child = ref _nodes[c];
				var mainSize = _scratch[c];
				var align = child.Style.AlignSelf == UiAlignSelf.Auto ? alignItems : (UiAlign)(child.Style.AlignSelf - 1);
				var fixedCross = row ? child.Style.Height : child.Style.Width;
				float crossSize;
				if (align == UiAlign.Stretch && fixedCross <= 0)
				{
					crossSize = lineCross;
					var max = row ? child.Style.MaxHeight : child.Style.MaxWidth;
					var min = row ? child.Style.MinHeight : child.Style.MinWidth;
					if (max > 0) crossSize = MathF.Min(crossSize, max);
					crossSize = MathF.Max(crossSize, min);
				}
				else
				{
					crossSize = _cross(child.Measured, row);
				}

				var crossOffset = align switch
				{
					UiAlign.Center => (lineCross - crossSize) * 0.5f,
					UiAlign.End => lineCross - crossSize,
					_ => 0f,
				};

				child.Rect = row
					? new RectangleF(content.X + position, content.Y + crossPosition + crossOffset, mainSize, crossSize)
					: new RectangleF(content.X + crossPosition + crossOffset, content.Y + position, crossSize, mainSize);
				child.Clip = childClip;
				if (_intersects(child.Rect, childClip)) child.Flags |= NodeFlags.Visible;
				position += mainSize + gap + spacing;
			}

			crossPosition += lineCross + gap;
		}
	}

	private static bool _intersects(in RectangleF a, in RectangleF b) =>
		a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
