using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Metrics;

/// <summary>
/// Writes <see cref="FrameStats.DrawCalls"/>, <see cref="FrameStats.Sprites"/> and <see cref="FrameStats.Triangles"/> from
/// the registered <see cref="ISpriteBatch"/> when it implements <see cref="ISpriteBatchStatistics"/> (resolved on first use).
/// </summary>
internal sealed class SpriteBatchStatsSource(IServiceProvider services) : IFrameStatsSource
{
	private ISpriteBatchStatistics? _statistics;
	private bool _resolved;

	public void Collect(ref FrameStats stats)
	{
		if (!_resolved)
		{
			_resolved = true;
			_statistics = services.GetService<ISpriteBatch>() as ISpriteBatchStatistics;
		}

		if (_statistics is null) return;

		var last = _statistics.LastFrameStatistics;
		stats.DrawCalls = last.DrawCalls;
		stats.Sprites = last.Sprites;
		stats.Triangles = last.Triangles;
	}
}

/// <summary>
/// The metrics steps: the trace capture key (First, after input) and the trace written at shutdown (Destroy, last).
/// </summary>
internal sealed class MetricsSystem(IServiceProvider services, MetricsService metrics, IOptionsMonitor<MetricsConfig> config)
{
	private IInputState? _input;
	private bool _resolved;

	[First(Order = StageOrder.Metrics)]
	public void CaptureKey(GameTime dt)
	{
		var key = config.CurrentValue.CaptureKey;
		if (key == Key.Unknown) return;

		if (!_resolved)
		{
			_resolved = true;
			_input = services.GetService<IInputState>();
		}

		if (_input is not null && _input.Pressed(key) && !metrics.IsCapturing) metrics.Capture(config.CurrentValue.CaptureFrames);
	}

	[Destroy(Order = StageOrder.EngineTeardownLast)]
	public void Shutdown(GameTime dt) => metrics.Shutdown();
}
