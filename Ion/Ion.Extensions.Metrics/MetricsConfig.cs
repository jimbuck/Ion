namespace Ion.Extensions.Metrics;

/// <summary>
/// Metrics options, bound from <c>Ion:Metrics</c>.
/// </summary>
public class MetricsConfig
{
	/// <summary>The number of completed frames the profiler keeps (the ring depth). Default 300 (5 s at 60 fps).</summary>
	public int HistoryFrames { get; set; } = FrameProfiler.DefaultHistoryFrames;

	/// <summary>The number of spans each frame can hold. Default 512.</summary>
	public int SpansPerFrame { get; set; } = FrameProfiler.DefaultSpansPerFrame;

	/// <summary>
	/// Whether span recording starts on (the runtime toggle, <see cref="IMetrics.IsProfiling"/>). Default false. When on at
	/// shutdown, the kept frames are written to <see cref="TraceOutput"/>.
	/// </summary>
	public bool Profiling { get; set; }

	/// <summary>Where Chrome traces are written (captures, and at shutdown when <see cref="Profiling"/>). Empty disables writing.</summary>
	public string? TraceOutput { get; set; } = "trace.json";

	/// <summary>A JSON Lines file that receives one line per frame (<see cref="FrameStats"/> and every game counter). Empty disables it.</summary>
	public string? FrameLog { get; set; }

	/// <summary>How often, in frames, the frame log is flushed to disk. Default 60; 1 flushes every line.</summary>
	public int FrameLogFlushFrames { get; set; } = 60;

	/// <summary>The key that captures a trace of the next <see cref="CaptureFrames"/> frames. Default F9; <see cref="Key.Unknown"/> disables it.</summary>
	public Key CaptureKey { get; set; } = Key.F9;

	/// <summary>The number of frames a capture records. Default 120.</summary>
	public int CaptureFrames { get; set; } = 120;

	/// <summary>Whether to publish the <c>Ion</c> <see cref="System.Diagnostics.Metrics.Meter"/> (for <c>dotnet-counters</c>). Default true.</summary>
	public bool Meter { get; set; } = true;

	/// <summary>Whether to draw the metrics overlay (needs <see cref="OverlayFont"/>). Default false.</summary>
	public bool Overlay { get; set; }

	/// <summary>The font asset the overlay is drawn with (for example <c>Fonts/Mono.ttf</c>).</summary>
	public string? OverlayFont { get; set; }

	/// <summary>The overlay's font size. Default 16.</summary>
	public float OverlayFontSize { get; set; } = 16;

	/// <summary>How often the overlay text is refreshed, in seconds. Default 0.25.</summary>
	public double OverlayRefreshSeconds { get; set; } = 0.25;
}
