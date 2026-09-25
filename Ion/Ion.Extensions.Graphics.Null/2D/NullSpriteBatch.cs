using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The kind of an <see cref="ISpriteBatch"/> call recorded by <see cref="NullSpriteBatch"/>.
/// </summary>
public enum SpriteBatchCommandKind
{
	/// <summary>A textured <c>Draw</c>.</summary>
	Sprite,
	/// <summary>A <c>DrawString</c>.</summary>
	String,
	/// <summary>A <c>DrawRect</c>.</summary>
	Rect,
	/// <summary>A <c>DrawPoint</c>.</summary>
	Point,
	/// <summary>A <c>DrawLine</c>.</summary>
	Line,
}

/// <summary>
/// One <see cref="ISpriteBatch"/> call recorded by <see cref="NullSpriteBatch"/>.
/// </summary>
/// <param name="Kind">Which method was called.</param>
/// <param name="Position">Top-left of the destination (the start point for lines, the text position for strings).</param>
/// <param name="Size">Size of the destination; for lines the vector from start to end; for strings the measured text size times scale.</param>
/// <param name="Color">The color argument as passed (<c>default</c> means untinted).</param>
/// <param name="Rotation">The rotation argument (0 for points; the angle for lines drawn by angle).</param>
/// <param name="Depth">The depth argument.</param>
/// <param name="Texture">The texture for <see cref="SpriteBatchCommandKind.Sprite"/>, otherwise null.</param>
/// <param name="Font">The font for <see cref="SpriteBatchCommandKind.String"/>, otherwise null.</param>
/// <param name="Text">The text for <see cref="SpriteBatchCommandKind.String"/>, otherwise null.</param>
public readonly record struct SpriteBatchCommand(
	SpriteBatchCommandKind Kind,
	Vector2 Position,
	Vector2 Size,
	Color Color,
	float Rotation,
	float Depth,
	ITexture2D? Texture = null,
	IFont? Font = null,
	string? Text = null);

/// <summary>
/// What an <see cref="ISpriteBatch"/> was asked to draw during one frame.
/// </summary>
public interface ISpriteBatchStats
{
	/// <summary>
	/// The 0-based index of the frame (the number of frames completed before it).
	/// </summary>
	long Frame { get; }

	/// <summary>
	/// Every draw call of the frame, of any kind.
	/// </summary>
	int DrawCalls { get; }

	/// <summary>
	/// Textured <c>Draw</c> calls.
	/// </summary>
	int Sprites { get; }

	/// <summary>
	/// <c>DrawString</c> calls.
	/// </summary>
	int Strings { get; }

	/// <summary>
	/// <c>DrawRect</c> calls.
	/// </summary>
	int Rects { get; }

	/// <summary>
	/// <c>DrawPoint</c> calls.
	/// </summary>
	int Points { get; }

	/// <summary>
	/// <c>DrawLine</c> calls.
	/// </summary>
	int Lines { get; }

	/// <summary>
	/// The last <see cref="NullSpriteBatch.CommandCapacity"/> calls of the frame, oldest first.
	/// </summary>
	IReadOnlyList<SpriteBatchCommand> Commands { get; }
}

/// <summary>
/// An <see cref="ISpriteBatch"/> that draws nothing and records per-frame statistics, for servers, CI and tests.
/// The sprite batch system calls <see cref="Begin"/> before and <see cref="End"/> after each Render stage; after a frame,
/// <see cref="LastFrame"/> holds what was drawn during it. Registered by <c>AddNullGraphics</c>; resolve it as
/// <see cref="NullSpriteBatch"/> to read the statistics.
/// </summary>
public sealed class NullSpriteBatch : ISpriteBatch, ISpriteBatchStatistics
{
	/// <summary>
	/// The default for <see cref="CommandCapacity"/>.
	/// </summary>
	public const int DefaultCommandCapacity = 256;

	private sealed class FrameStats(long frame) : ISpriteBatchStats
	{
		public readonly List<SpriteBatchCommand> CommandList = [];

		public long Frame { get; } = frame;
		public int DrawCalls { get; set; }
		public int Sprites { get; set; }
		public int Strings { get; set; }
		public int Rects { get; set; }
		public int Points { get; set; }
		public int Lines { get; set; }
		public int Glyphs { get; set; }
		public IReadOnlyList<SpriteBatchCommand> Commands => CommandList;
	}

	private static readonly FrameStats _noFrame = new(-1);

	private FrameStats _current = new(0);
	private FrameStats _last = _noFrame;

	/// <summary>
	/// How many of the most recent calls each frame keeps in <see cref="ISpriteBatchStats.Commands"/>. 0 keeps none (counts
	/// only). Defaults to <see cref="DefaultCommandCapacity"/>.
	/// </summary>
	public int CommandCapacity { get; set; } = DefaultCommandCapacity;

	/// <summary>
	/// The number of frames ended with <see cref="End"/>.
	/// </summary>
	public long FramesCompleted { get; private set; }

	/// <summary>
	/// The frame being recorded (since the last <see cref="Begin"/>).
	/// </summary>
	public ISpriteBatchStats CurrentFrame => _current;

	/// <summary>
	/// The last completed frame. Before the first <see cref="End"/> it is empty with a <see cref="ISpriteBatchStats.Frame"/> of -1.
	/// </summary>
	public ISpriteBatchStats LastFrame => _last;

	/// <summary>
	/// <see cref="LastFrame"/> as metrics: every call is a draw call (nothing is batched), every sprite, rectangle, point,
	/// line and non-whitespace glyph is a quad of two triangles.
	/// </summary>
	public SpriteBatchStatistics LastFrameStatistics
	{
		get
		{
			var last = _last;
			var quads = last.Sprites + last.Rects + last.Points + last.Lines + last.Glyphs;
			return new SpriteBatchStatistics(last.Frame, last.DrawCalls, quads, quads * 2);
		}
	}

	/// <summary>
	/// Starts recording a new frame, discarding anything drawn since the last <see cref="End"/>.
	/// </summary>
	public void Begin()
	{
		_current = new FrameStats(FramesCompleted);
	}

	/// <summary>
	/// Completes the frame: it becomes <see cref="LastFrame"/> and a new one starts.
	/// </summary>
	public void End()
	{
		_last = _current;
		FramesCompleted++;
		_current = new FrameStats(FramesCompleted);
	}

	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		_current.Sprites++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Sprite, destinationRectangle.Location, destinationRectangle.Size, color, rotation, depth, Texture: texture));
	}

	public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		_current.Sprites++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Sprite, position, size, color, rotation, depth, Texture: texture));
	}

	public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None)
	{
		_current.Strings++;
		foreach (var c in text) if (!char.IsWhiteSpace(c)) _current.Glyphs++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.String, textPosition, font.MeasureString(text) * scale, color, rotation, depth, Font: font, Text: text));
	}

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0)
	{
		_current.Rects++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Rect, destinationRectangle.Location, destinationRectangle.Size, color, rotation, depth));
	}

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0)
	{
		_current.Rects++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Rect, position, size, color, rotation, depth));
	}

	public void DrawPoint(Color color, Vector2 position, float depth = 0)
	{
		DrawPoint(color, position, Vector2.One, depth);
	}

	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0)
	{
		_current.Points++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Point, position, size, color, 0, depth));
	}

	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1, float depth = 0)
	{
		_current.Lines++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Line, pointA, pointB - pointA, color, 0, depth));
	}

	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0)
	{
		_current.Lines++;
		_record(new SpriteBatchCommand(SpriteBatchCommandKind.Line, start, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length, color, angle, depth));
	}

	private void _record(in SpriteBatchCommand command)
	{
		_current.DrawCalls++;

		var capacity = CommandCapacity;
		if (capacity <= 0) return;

		var commands = _current.CommandList;
		if (commands.Count >= capacity) commands.RemoveRange(0, commands.Count - capacity + 1);
		commands.Add(command);
	}
}
