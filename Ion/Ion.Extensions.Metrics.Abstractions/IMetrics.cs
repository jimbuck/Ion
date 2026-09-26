namespace Ion.Extensions.Metrics;

/// <summary>
/// The metrics of a running game: the frame profiler (spans), the last frame's <see cref="FrameStats"/>, game counters,
/// and trace capture. Registered by <c>AddMetrics</c> (and so by <c>AddIon</c>).
/// </summary>
/// <remarks>
/// Hot paths should keep the handles: a <see cref="SpanId"/> registered once and <see cref="FrameProfiler.Scope"/> on
/// <see cref="Profiler"/> (a sealed class, so the disabled check inlines), and the <see cref="MetricsCounter"/>,
/// <see cref="MetricsGauge"/> or <see cref="MetricsHistogram"/> returned when registering a counter. Registering looks up
/// a name; updating a handle does not.
/// </remarks>
public interface IMetrics
{
	/// <summary>The frame profiler: the ring of the last <c>HistoryFrames</c> frames.</summary>
	FrameProfiler Profiler { get; }

	/// <summary>The runtime toggle for span recording (see <see cref="FrameProfiler.IsActive"/>).</summary>
	bool IsProfiling { get; set; }

	/// <summary>The stats of the last completed frame.</summary>
	FrameStats LastFrame { get; }

	/// <summary>Registers (or finds) a span name; see <see cref="MetricsIds.Register"/>.</summary>
	SpanId Span(string name);

	/// <summary>A span for a <c>using</c> block; see <see cref="FrameProfiler.Scope"/>.</summary>
	MetricsScope Scope(SpanId span);

	/// <summary>Registers (or finds) a counter: a running total, reported every frame.</summary>
	MetricsCounter Counter(string name, string? unit = null, string? description = null);

	/// <summary>Registers (or finds) a gauge: a value set by the game, reported every frame.</summary>
	MetricsGauge Gauge(string name, string? unit = null, string? description = null);

	/// <summary>Registers (or finds) a histogram: values recorded during a frame, reported per frame as count, sum, min and max.</summary>
	MetricsHistogram Histogram(string name, string? unit = null, string? description = null);

	/// <summary>Every registered counter, gauge and histogram, in registration order.</summary>
	IReadOnlyList<MetricsInstrument> Instruments { get; }

	/// <summary>
	/// Records the next <paramref name="frames"/> frames (at most <c>HistoryFrames</c>) with profiling on, then writes them
	/// as a Chrome trace to <paramref name="path"/> (<c>Ion:Metrics:TraceOutput</c> when null) and restores the toggle.
	/// </summary>
	void Capture(int frames, string? path = null);

	/// <summary>Whether a <see cref="Capture"/> is in progress.</summary>
	bool IsCapturing { get; }

	/// <summary>The path of the last trace written (by a capture, <see cref="WriteTrace"/> or at shutdown), if any.</summary>
	string? LastTracePath { get; }

	/// <summary>
	/// Writes the last <paramref name="frames"/> kept frames as a Chrome trace to <paramref name="path"/>
	/// (<c>Ion:Metrics:TraceOutput</c> when null) now. Returns the path written, or null when there is no path.
	/// </summary>
	string? WriteTrace(string? path = null, int frames = int.MaxValue);
}

/// <summary>
/// Text lines describing the current metrics (frame time, fps, draw calls, counters), refreshed a few times per second,
/// for an on-screen overlay. The metrics module draws them with the sprite batch when <c>Ion:Metrics:Overlay</c> is on and
/// an overlay font is configured; anything else with a sprite batch can draw them too.
/// </summary>
public interface IMetricsOverlaySource
{
	/// <summary>The lines to draw, top to bottom. The list is reused; copy it to keep it.</summary>
	IReadOnlyList<string> Lines { get; }

	/// <summary>Increments every time <see cref="Lines"/> changes.</summary>
	int Version { get; }
}
