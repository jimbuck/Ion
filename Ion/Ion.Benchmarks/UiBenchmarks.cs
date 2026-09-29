using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Rendering2D;
using Ion.Extensions.UI;

namespace Ion.Benchmarks;

/// <summary>
/// CPU cost of a 500-widget UI frame (Ion.Extensions.UI), no GPU: 125 cells in a wrapping row, each a column with a label,
/// a button and a toggle or a slider (500 nodes plus the panel and the row), on a 1920x1080 viewport.
/// <list type="bullet">
/// <item><c>Build_Layout</c>: <see cref="Ui.BeginFrame"/> (input against the retained hit-test tree), the 500 widget
/// calls, and <see cref="Ui.EndFrame"/> (flex layout, focus, tree publication).</item>
/// <item><c>Build_Layout_Draw</c>: the same plus <see cref="Ui.Draw"/> into the 2D renderer's <see cref="SpriteBatch"/>
/// (its instance recording, segment sort and draw ranges; no upload). Text goes through the null backend's font, whose
/// strings the benchmark's batch records as one quad per character, the cost the real renderer pays per cached glyph.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class UiBenchmarks
{
	private const int Cells = 125;

	private Ui _ui = null!;
	private NullInputState _input = null!;
	private SpriteBatch _spriteBatch = null!;
	private GlyphQuadBatch _batch = null!;
	private readonly bool[] _toggles = new bool[Cells];
	private readonly float[] _sliders = new float[Cells];
	private int _clicks;

	[GlobalSetup]
	public void Setup()
	{
		_input = new NullInputState();
		_ui = new Ui(_input, new UiOptions { AutoFocus = true });
		_ui.Theme = UiTheme.Default with { Font = new NullFontSet("bench", []).CreateStyle(16) };
		_spriteBatch = new SpriteBatch(new SpriteBatchBenchmarks.NoGpuFrame());
		_batch = new GlyphQuadBatch(_spriteBatch);
		_input.SetMousePosition(new Vector2(400, 300));
		_input.Step();

		// Warm up: states, paths, measured text and arrays reach their steady size.
		for (var i = 0; i < 10; i++) Build_Layout_Draw();
	}

	[Benchmark(Baseline = true)]
	public int Build_Layout()
	{
		_ui.BeginFrame(1f / 60f, new Vector2(1920, 1080));
		Build();
		_ui.EndFrame();
		return _ui.NodeCount;
	}

	[Benchmark]
	public int Build_Layout_Draw()
	{
		_ui.BeginFrame(1f / 60f, new Vector2(1920, 1080));
		Build();
		_ui.EndFrame();
		_spriteBatch.Begin();
		_ui.Draw(_batch);
		_spriteBatch.End();
		return _spriteBatch.Batcher.Count;
	}

	private void Build()
	{
		var ui = _ui;
		using (ui.Panel("grid"))
		{
			using (ui.Row("cells", new UiStyle { Wrap = true, Gap = 6 }))
			{
				for (var i = 0; i < Cells; i++)
				{
					using (ui.Column(null, new UiStyle { Width = 148, Gap = 4 }))
					{
						ui.Label("Setting");
						if (ui.Button("Apply")) _clicks++;
						if ((i & 1) == 0) ui.Toggle("Enabled", ref _toggles[i]);
						else ui.Slider("Level", ref _sliders[i]);
					}
				}
			}
		}
	}

	/// <summary>Forwards to a sprite batch, drawing each string as one quad per character (the null font draws nothing).</summary>
	private sealed class GlyphQuadBatch(SpriteBatch inner) : ISpriteBatch
	{
		public void Begin(SpriteBatchOptions options = default) => inner.Begin(options);

		public void End() => inner.End();

		public void SetRenderTarget(ITexture? target, Color? clearColor = null) => inner.SetRenderTarget(target, clearColor);

		public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0) => inner.DrawRect(color, destinationRectangle, origin, rotation, depth);

		public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0) => inner.DrawRect(color, position, size, origin, rotation, depth);

		public void DrawPoint(Color color, Vector2 position, float depth = 0) => inner.DrawPoint(color, position, depth);

		public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0) => inner.DrawPoint(color, position, size, depth);

		public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1, float depth = 0) => inner.DrawLine(color, pointA, pointB, thickness, depth);

		public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0) => inner.DrawLine(color, start, length, angle, thickness, depth);

		public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None)
		{
			var advance = font.FontSize * 0.5f * scale;
			var size = new Vector2(advance, font.FontSize * scale);
			for (var i = 0; i < text.Length; i++) inner.DrawRect(color, textPosition + new Vector2(i * advance, 0), size, depth: depth);
		}

		public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) =>
			inner.Draw(texture, destinationRectangle, sourceRectangle, color, origin, rotation, depth, options);

		public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) =>
			inner.Draw(texture, position, size, sourceRectangle, color, origin, rotation, depth, options);
	}
}
