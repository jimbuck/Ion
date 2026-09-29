using System.Diagnostics;
using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

namespace Ion.Extensions.Metrics;

/// <summary>
/// The <see cref="IMetricsOverlaySource"/>: keeps the last frame's stats (without allocating) and builds the text lines
/// when they are read, at most every <c>Ion:Metrics:OverlayRefreshSeconds</c>.
/// </summary>
internal sealed class MetricsOverlay(IOptionsMonitor<MetricsConfig> config) : IMetricsOverlaySource
{
	private readonly List<string> _lines = [];
	private FrameStats _stats;
	private double _frameMsSum;
	private int _frames;
	private IReadOnlyList<MetricsInstrument> _instruments = [];
	private long _built;
	private bool _dirty;

	public int Version { get; private set; }

	public IReadOnlyList<string> Lines
	{
		get
		{
			if (_dirty && Stopwatch.GetElapsedTime(_built).TotalSeconds >= config.CurrentValue.OverlayRefreshSeconds) Build();
			return _lines;
		}
	}

	public void OnFrame(in FrameStats stats, IReadOnlyList<MetricsInstrument> instruments)
	{
		_stats = stats;
		_frameMsSum += stats.FrameMs;
		_frames++;
		_instruments = instruments;
		_dirty = true;
	}

	private void Build()
	{
		var s = _stats;
		var avg = _frames > 0 ? _frameMsSum / _frames : 0;
		var c = CultureInfo.InvariantCulture;

		_lines.Clear();
		_lines.Add(string.Format(c, "{0:0} fps  {1:0.00} ms  (work {2:0.00} ms)", avg > 0 ? 1000 / avg : 0, avg, s.WorkMs));
		_lines.Add(string.Format(c, "draws {0}  sprites {1}  tris {2}", s.DrawCalls, s.Sprites, s.Triangles));
		_lines.Add(string.Format(c, "entities {0}  events {1}  fixed {2}", s.Entities, s.EventsEmitted, s.FixedSteps));
		_lines.Add(string.Format(c, "alloc {0} B  gc {1}/{2}/{3}", s.AllocatedBytes, s.Gc0, s.Gc1, s.Gc2));

		foreach (var instrument in _instruments)
		{
			_lines.Add(instrument switch
			{
				MetricsCounter counter => string.Format(c, "{0} {1}", counter.Name, counter.Value),
				MetricsGauge gauge => string.Format(c, "{0} {1:0.###}", gauge.Name, gauge.Value),
				MetricsHistogram histogram => string.Format(c, "{0} n={1} mean={2:0.###}", histogram.Name, histogram.Count, histogram.Mean),
				_ => instrument.Name,
			});
		}

		_frameMsSum = 0;
		_frames = 0;
		_built = Stopwatch.GetTimestamp();
		_dirty = false;
		Version++;
	}
}

/// <summary>
/// Draws the <see cref="IMetricsOverlaySource"/> lines with the application's <see cref="ISpriteBatch"/> (any backend)
/// when <c>Ion:Metrics:Overlay</c> is on and <c>Ion:Metrics:OverlayFont</c> names a font asset. A Render step at
/// <see cref="StageOrder.MetricsOverlay"/>, inside the sprite batch scope, after the game's drawing.
/// </summary>
internal sealed class MetricsOverlaySystem(IServiceProvider services, IMetricsOverlaySource overlay, IOptionsMonitor<MetricsConfig> config)
{
	private static readonly Color Background = new(0f, 0f, 0f, 0.6f);

	private ISpriteBatch? _spriteBatch;
	private IFont? _font;
	private float _lineHeight;
	private bool _resolved;

	[Render(Order = StageOrder.MetricsOverlay)]
	public void Draw(GameTime dt)
	{
		var options = config.CurrentValue;
		if (!options.Overlay) return;
		if (!_resolved) Resolve(options);
		if (_spriteBatch is null || _font is null) return;

		var lines = overlay.Lines;
		// An estimate (0.6 em per character): measuring every line every frame would cost more than the overlay is worth.
		var chars = 0;
		for (var i = 0; i < lines.Count; i++) chars = Math.Max(chars, lines[i].Length);
		var width = chars * _font.FontSize * 0.6f;

		var origin = new Vector2(8, 8);
		_spriteBatch.DrawRect(Background, origin - new Vector2(4), new Vector2(width + 8, lines.Count * _lineHeight + 8), depth: 1f);
		for (var i = 0; i < lines.Count; i++)
		{
			_spriteBatch.DrawString(_font, lines[i], origin + new Vector2(0, i * _lineHeight), Color.White, depth: 1f);
		}
	}

	private void Resolve(MetricsConfig options)
	{
		_resolved = true;
		_spriteBatch = services.GetService<ISpriteBatch>();
		if (_spriteBatch is null || string.IsNullOrWhiteSpace(options.OverlayFont)) return;

		var assets = services.GetService<IAssetManager>();
		_font = assets?.Load<IFontSet>(options.OverlayFont).CreateStyle(options.OverlayFontSize);
		_lineHeight = _font is null ? 0 : (_font.LineHeight > 0 ? _font.LineHeight : options.OverlayFontSize * 1.25f);
	}
}
