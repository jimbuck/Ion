using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

using Xunit;

namespace Ion.Extensions.UI.Tests;

/// <summary>
/// Drives a <see cref="Ui"/> without a game loop: scripted input (<see cref="NullInputState"/>), the null backend's
/// monospaced font (8 px per character, 16 px lines at size 16) and a recording sprite batch.
/// </summary>
internal sealed class UiHarness
{
	public const float FrameSeconds = 1f / 60f;

	public UiHarness(float width = 800, float height = 600, UiOptions? options = null)
	{
		Viewport = new Vector2(width, height);
		Input = new NullInputState();
		Font = new NullFontSet("test", []).CreateStyle(16);
		Ui = new Ui(Input, options);
		Ui.Theme = UiTheme.Default with { Font = Font };
	}

	public Vector2 Viewport { get; set; }

	public NullInputState Input { get; }

	public IFont Font { get; }

	public Ui Ui { get; }

	public IUiTree Tree => Ui.Tree;

	public RecordingBatch Batch { get; } = new();

	/// <summary>Runs one frame: input step, BeginFrame, <paramref name="build"/>, EndFrame, Draw.</summary>
	public void Frame(Action<Ui> build)
	{
		Input.Step();
		Ui.BeginFrame(FrameSeconds, Viewport);
		build(Ui);
		Ui.EndFrame();
		Batch.Clear();
		Ui.Draw(Batch);
	}

	/// <summary>Runs <paramref name="frames"/> frames of the same UI.</summary>
	public void Frames(int frames, Action<Ui> build)
	{
		for (var i = 0; i < frames; i++) Frame(build);
	}

	public UiNodeInfo Node(string path)
	{
		Assert.True(Tree.TryFind(path, out var node), $"No node '{path}'. Paths: {string.Join(", ", Tree.Snapshot().Select(n => n.Path))}");
		return node;
	}

	/// <summary>Moves the pointer to the center of <paramref name="path"/> and clicks (press and release in one frame).</summary>
	public void ClickAt(string path)
	{
		var rect = Node(path).Rect;
		Input.SetMousePosition(new Vector2(rect.CenterX, rect.CenterY));
		Input.Click();
	}
}

/// <summary>A sprite batch that records rectangles, sprites, strings and segment options.</summary>
internal sealed class RecordingBatch : ISpriteBatch
{
	public readonly List<(string Kind, RectangleF Rect, Color Color, string? Text, RectangleF Source)> Draws = [];
	public readonly List<SpriteBatchOptions> Segments = [];
	public int Depth;

	public void Clear()
	{
		Draws.Clear();
		Segments.Clear();
	}

	public IEnumerable<string> Strings => Draws.Where(d => d.Kind == "string").Select(d => d.Text!);

	public void Begin(SpriteBatchOptions options = default)
	{
		Segments.Add(options);
		Depth++;
	}

	public void End() => Depth--;

	public void SetRenderTarget(ITexture? target, Color? clearColor = null) { }

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0) =>
		Draws.Add(("rect", destinationRectangle, color, null, default));

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0) =>
		Draws.Add(("rect", new RectangleF(position, size), color, null, default));

	public void DrawPoint(Color color, Vector2 position, float depth = 0) { }

	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0) { }

	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1, float depth = 0) { }

	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0) { }

	public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None) =>
		Draws.Add(("string", new RectangleF(textPosition, font.MeasureString(text) * scale), color, text, default));

	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) =>
		Draws.Add(("sprite", destinationRectangle, color, null, sourceRectangle));

	public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) =>
		Draws.Add(("sprite", new RectangleF(position, size), color, null, sourceRectangle));
}

/// <summary>A sprite batch that only counts calls (allocation-free).</summary>
internal sealed class CountingBatch : ISpriteBatch
{
	public int Calls;

	public void Begin(SpriteBatchOptions options = default) => Calls++;

	public void End() => Calls++;

	public void SetRenderTarget(ITexture? target, Color? clearColor = null) { }

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0) => Calls++;

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0) => Calls++;

	public void DrawPoint(Color color, Vector2 position, float depth = 0) => Calls++;

	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0) => Calls++;

	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1, float depth = 0) => Calls++;

	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0) => Calls++;

	public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None) => Calls++;

	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) => Calls++;

	public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) => Calls++;
}

/// <summary>A texture with a size and nothing else, for nine-slice tests.</summary>
internal sealed class FakeTexture(uint width, uint height) : ITexture2D
{
	public nint Id => 1;

	public string Name => "fake";

	public uint Width => width;

	public uint Height => height;

	public uint MipLevels => 1;

	public void Dispose() { }
}
