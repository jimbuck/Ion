using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

public sealed partial class Ui
{
	/// <summary>
	/// Submits the last laid-out frame to <paramref name="spriteBatch"/>: solid rectangles, nine-slices and text in tree
	/// order (parents under children, later siblings over earlier ones), each scroll view's content in a segment with its
	/// clip rectangle as scissor, the focus outline over the focused widget. Called by the UI system at
	/// <see cref="StageOrder.Ui"/> in Render; draws inside a segment of its own (default options: pixel space, submission order).
	/// </summary>
	public void Draw(ISpriteBatch spriteBatch)
	{
		ArgumentNullException.ThrowIfNull(spriteBatch);
		if (_inFrame || _count <= 1) return;
		spriteBatch.Begin(default);
		try
		{
			for (var c = _nodes[0].FirstChild; c >= 0; c = _nodes[c].NextSibling) _drawNode(spriteBatch, c);
		}
		finally
		{
			spriteBatch.End();
		}
	}

	private void _drawNode(ISpriteBatch batch, int index)
	{
		ref var node = ref _nodes[index];
		if (node.Is(NodeFlags.Visible)) _drawSelf(batch, ref node);

		if (node.FirstChild >= 0)
		{
			if (node.Is(NodeFlags.Clips))
			{
				var clip = RectangleF.Intersect(node.Clip, node.Rect);
				if (clip.Width > 0 && clip.Height > 0)
				{
					var x0 = (int)MathF.Floor(clip.X);
					var y0 = (int)MathF.Floor(clip.Y);
					var scissor = new Rectangle(x0, y0, (int)MathF.Ceiling(clip.X + clip.Width) - x0, (int)MathF.Ceiling(clip.Y + clip.Height) - y0);
					batch.Begin(new SpriteBatchOptions { Scissor = scissor });
					try
					{
						for (var c = node.FirstChild; c >= 0; c = _nodes[c].NextSibling) _drawNode(batch, c);
					}
					finally
					{
						batch.End();
					}

					_drawScrollBar(batch, ref _nodes[index]);
				}
			}
			else
			{
				for (var c = node.FirstChild; c >= 0; c = _nodes[c].NextSibling) _drawNode(batch, c);
			}
		}
	}

	private void _drawSelf(ISpriteBatch batch, ref UiNode node)
	{
		ref readonly var theme = ref _theme;
		var rect = node.Rect;
		var enabled = node.Is(NodeFlags.Enabled);
		var textColor = enabled ? theme.Text : theme.TextDisabled;
		var centerY = rect.Y + rect.Height * 0.5f;

		switch (node.Kind)
		{
			case UiNodeKind.Panel:
				{
					var background = node.Style.Background ?? theme.PanelBackground;
					if (theme.PanelSkin is { } skin) DrawNineSlice(batch, skin, rect, background);
					else batch.DrawRect(background, rect);
					break;
				}

			case UiNodeKind.Row or UiNodeKind.Column or UiNodeKind.ScrollView:
				if (node.Style.Background is { } fill) batch.DrawRect(fill, rect);
				break;

			case UiNodeKind.List:
				{
					if (node.Style.Background is { } fill2) batch.DrawRect(fill2, rect);
					var top = node.Style.Padding?.Top ?? 0;
					var left = node.Style.Padding?.Left ?? 0;
					_text(batch, node.Text, new Vector2(rect.X + left, rect.Y + top), textColor, 1f);
					break;
				}

			case UiNodeKind.Label:
				_text(batch, node.Text, new Vector2(rect.X, centerY - node.TextSize.Y * 0.5f), node.TextColor ?? textColor, node.TextScale);
				break;

			case UiNodeKind.Button:
				{
					Color background;
					if (!enabled) background = theme.ButtonDisabled;
					else if (node.Id == _activeId && node.Id == _hotId) background = theme.ButtonPressed;
					else if (node.Id == _hotId) background = theme.ButtonHover;
					else background = theme.Button;
					if (node.Style.Background is { } custom && enabled) background = custom;
					if (theme.ButtonSkin is { } skin) DrawNineSlice(batch, skin, rect, background);
					else batch.DrawRect(background, rect);
					_text(batch, node.Text, new Vector2(rect.X + (rect.Width - node.TextSize.X) * 0.5f, centerY - node.TextSize.Y * 0.5f), textColor, 1f);
					break;
				}

			case UiNodeKind.Toggle:
				{
					var size = theme.ToggleSize;
					var box = new RectangleF(rect.X, centerY - size * 0.5f, size, size);
					batch.DrawRect(node.Id == _hotId && enabled ? theme.ButtonHover : theme.Button, box);
					if (node.Is(NodeFlags.On))
					{
						var inset = MathF.Max(2, MathF.Round(size * 0.2f));
						batch.DrawRect(enabled ? theme.Accent : theme.TextDisabled, new RectangleF(box.X + inset, box.Y + inset, size - 2 * inset, size - 2 * inset));
					}

					_text(batch, node.Text, new Vector2(rect.X + size + theme.Gap, centerY - node.TextSize.Y * 0.5f), textColor, 1f);
					break;
				}

			case UiNodeKind.Slider:
				{
					_text(batch, node.Text, new Vector2(rect.X, centerY - node.TextSize.Y * 0.5f), textColor, 1f);
					var trackX = node.State.TrackX;
					var trackWidth = node.State.TrackWidth;
					var trackHeight = MathF.Max(2, MathF.Round(theme.ControlHeight * 0.15f));
					batch.DrawRect(theme.Track, new RectangleF(trackX, centerY - trackHeight * 0.5f, trackWidth, trackHeight));
					var filled = trackWidth * node.Number;
					batch.DrawRect(enabled ? theme.Accent : theme.TextDisabled, new RectangleF(trackX, centerY - trackHeight * 0.5f, filled, trackHeight));
					var thumbHeight = MathF.Round(theme.ControlHeight * 0.6f);
					batch.DrawRect(node.Id == _hotId || node.Id == _activeId ? theme.Text : theme.Thumb,
						new RectangleF(trackX + filled - theme.ThumbWidth * 0.5f, centerY - thumbHeight * 0.5f, theme.ThumbWidth, thumbHeight));
					if (node.Value is { } value)
					{
						var size = MeasureText(value);
						_text(batch, value, new Vector2(trackX + trackWidth + theme.Gap, centerY - size.Y * 0.5f), textColor, 1f);
					}

					break;
				}

			case UiNodeKind.TextInput:
				{
					_text(batch, node.Text, new Vector2(rect.X, centerY - node.TextSize.Y * 0.5f), textColor, 1f);
					var fieldX = rect.X + node.LabelWidth + theme.Gap;
					var field = new RectangleF(fieldX, rect.Y, MathF.Max(0, rect.X + rect.Width - fieldX), rect.Height);
					batch.DrawRect(theme.InputBackground, field);
					var textHeight = MeasureText(null).Y;
					var textY = centerY - textHeight * 0.5f;
					_text(batch, node.Value, new Vector2(field.X + 6, textY), textColor, 1f);
					if (node.Id == _editingId)
					{
						var caretX = field.X + 6 + _caretOffset(node.State);
						batch.DrawRect(theme.Accent, new RectangleF(caretX, textY, 2, textHeight));
					}

					break;
				}

			case UiNodeKind.ListItem:
				{
					if (node.Is(NodeFlags.On)) batch.DrawRect(theme.ListItemSelected, rect);
					else if (node.Id == _hotId && enabled) batch.DrawRect(theme.ListItemHover, rect);
					_text(batch, node.Text, new Vector2(rect.X + theme.ButtonPadding, centerY - node.TextSize.Y * 0.5f), textColor, 1f);
					break;
				}
		}

		if (node.Id == _focusId && theme.FocusThickness > 0)
		{
			var t = theme.FocusThickness;
			batch.DrawRectOutline(theme.FocusOutline, new RectangleF(rect.X - t, rect.Y - t, rect.Width + 2 * t, rect.Height + 2 * t), t);
		}
	}

	private void _drawScrollBar(ISpriteBatch batch, ref UiNode node)
	{
		var state = node.State;
		if (state.ScrollMax <= 0 || _theme.ScrollBarWidth <= 0) return;
		var rect = node.Rect;
		var width = _theme.ScrollBarWidth;
		if (node.Style.Direction == UiDirection.Row)
		{
			var length = rect.Width * rect.Width / (rect.Width + state.ScrollMax);
			var x = rect.X + (rect.Width - length) * (state.Scroll / state.ScrollMax);
			batch.DrawRect(_theme.ScrollBar, new RectangleF(x, rect.Y + rect.Height - width - 2, length, width));
		}
		else
		{
			var length = rect.Height * rect.Height / (rect.Height + state.ScrollMax);
			var y = rect.Y + (rect.Height - length) * (state.Scroll / state.ScrollMax);
			batch.DrawRect(_theme.ScrollBar, new RectangleF(rect.X + rect.Width - width - 2, y, width, length));
		}
	}

	private float _caretOffset(UiNodeState state)
	{
		if (state.Caret <= 0 || state.Buffer is null) return 0;
		var prefix = state.Buffer.AsSpan(0, Math.Min(state.Caret, state.Length));
		// The prefix string is rebuilt only when the text before the caret changed (an edit or a caret move).
		if (state.CaretPrefix is null || !prefix.SequenceEqual(state.CaretPrefix)) state.CaretPrefix = new string(prefix);
		return MeasureText(state.CaretPrefix).X;
	}

	private void _text(ISpriteBatch batch, string? text, Vector2 position, Color color, float scale)
	{
		if (_theme.Font is not { } font || string.IsNullOrEmpty(text)) return;
		batch.DrawString(font, text, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color, scale: scale);
	}

	/// <summary>
	/// Draws <paramref name="skin"/> stretched over <paramref name="rect"/> as nine parts, tinted with <paramref name="color"/>:
	/// the corners at their size (<see cref="UiNineSlice.Border"/> times <see cref="UiNineSlice.Scale"/>, shrunk when the
	/// rectangle is smaller), the edges stretched along one axis, the center along both.
	/// </summary>
	public static void DrawNineSlice(ISpriteBatch spriteBatch, in UiNineSlice skin, RectangleF rect, Color color)
	{
		ArgumentNullException.ThrowIfNull(spriteBatch);
		var texture = skin.Texture;
		float tw = texture.Width, th = texture.Height;
		var border = skin.Border;
		var scale = skin.Scale > 0 ? skin.Scale : 1f;

		var left = border.Left * scale;
		var right = border.Right * scale;
		var top = border.Top * scale;
		var bottom = border.Bottom * scale;
		if (left + right > rect.Width && left + right > 0)
		{
			var k = rect.Width / (left + right);
			left *= k;
			right *= k;
		}

		if (top + bottom > rect.Height && top + bottom > 0)
		{
			var k = rect.Height / (top + bottom);
			top *= k;
			bottom *= k;
		}

		Span<float> sx = [0, border.Left, tw - border.Right, tw];
		Span<float> sy = [0, border.Top, th - border.Bottom, th];
		Span<float> dx = [rect.X, rect.X + left, rect.X + rect.Width - right, rect.X + rect.Width];
		Span<float> dy = [rect.Y, rect.Y + top, rect.Y + rect.Height - bottom, rect.Y + rect.Height];
		for (var row = 0; row < 3; row++)
		{
			for (var column = 0; column < 3; column++)
			{
				var dw = dx[column + 1] - dx[column];
				var dh = dy[row + 1] - dy[row];
				var sw = sx[column + 1] - sx[column];
				var sh = sy[row + 1] - sy[row];
				if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) continue;
				spriteBatch.Draw(texture, new RectangleF(dx[column], dy[row], dw, dh), new RectangleF(sx[column], sy[row], sw, sh), color);
			}
		}
	}
}
