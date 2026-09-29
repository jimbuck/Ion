using System.Runtime.InteropServices;

namespace Ion;

/// <summary>
/// The engine counters of one frame, written once per frame by the game loop (and the <see cref="IFrameStatsSource"/>s)
/// when metrics are installed. Read the last completed frame from <see cref="FrameProfiler.LastFrame"/> (or
/// <c>IMetrics.LastFrame</c>); the frame log writes one of these per line.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public struct FrameStats
{
	/// <summary>The frame number (<see cref="GameTime.Frame"/>).</summary>
	public uint Frame;

	/// <summary>The frame's time step from the loop's clock, in milliseconds (<see cref="GameTime.Delta"/>).</summary>
	public double DeltaMs;

	/// <summary>The wall-clock time the frame took, in milliseconds, including the time spent pacing (<see cref="IdleMs"/>).</summary>
	public double FrameMs;

	/// <summary>The wall-clock time the loop slept to honour <c>MaxFPS</c>, in milliseconds.</summary>
	public double IdleMs;

	/// <summary>The number of FixedUpdate steps the frame ran.</summary>
	public int FixedSteps;

	/// <summary>Draw calls submitted by the sprite batch (for the null backend: every draw call made, it does not batch).</summary>
	public int DrawCalls;

	/// <summary>Sprites (quads, including rectangles, lines and glyphs) drawn by the sprite batch.</summary>
	public int Sprites;

	/// <summary>Triangles drawn by the sprite batch.</summary>
	public int Triangles;

	/// <summary>Live entities (written by the ECS module through an <see cref="IFrameStatsSource"/>; 0 without one).</summary>
	public int Entities;

	/// <summary>Events emitted during the frame, across every channel.</summary>
	public long EventsEmitted;

	/// <summary>Generation 0 collections during the frame.</summary>
	public int Gc0;

	/// <summary>Generation 1 collections during the frame.</summary>
	public int Gc1;

	/// <summary>Generation 2 collections during the frame.</summary>
	public int Gc2;

	/// <summary>Bytes allocated by the loop thread during the frame (other threads, such as the audio thread, are not counted).</summary>
	public long AllocatedBytes;

	/// <summary>Spans recorded for the frame (0 when profiling is off).</summary>
	public int Spans;

	/// <summary>Spans that did not fit in the frame's buffer (raise <c>Ion:Metrics:SpansPerFrame</c> when not 0).</summary>
	public int DroppedSpans;

	/// <summary>The frame's rate implied by <see cref="FrameMs"/> (0 when unknown).</summary>
	public readonly double Fps => FrameMs > 0 ? 1000.0 / FrameMs : 0;

	/// <summary>The work time of the frame: <see cref="FrameMs"/> without <see cref="IdleMs"/>.</summary>
	public readonly double WorkMs => FrameMs - IdleMs;

	/// <inheritdoc/>
	public override readonly string ToString() =>
		$"frame {Frame}: {FrameMs:0.###} ms ({IdleMs:0.###} idle), {FixedSteps} fixed, {DrawCalls} draws, {Sprites} sprites, {Triangles} tris, {Entities} entities, {EventsEmitted} events, gc {Gc0}/{Gc1}/{Gc2}, {AllocatedBytes} B";
}

/// <summary>
/// Adds engine counters to a frame's <see cref="FrameStats"/> (draw calls from the sprite batch, entities from the ECS,
/// events from the bus). Register implementations in the service collection; the game loop calls every one once per frame,
/// after the Last stage, when metrics are installed. <see cref="Collect"/> must not allocate.
/// </summary>
public interface IFrameStatsSource
{
	/// <summary>Writes this source's counters into <paramref name="stats"/>.</summary>
	void Collect(ref FrameStats stats);
}

/// <summary>
/// Receives every completed frame (its stats and, when profiling, its spans) from a <see cref="FrameProfiler"/>. Called
/// on the loop thread at the end of the frame; the profile is only valid during the call (the ring reuses it).
/// </summary>
public interface IFrameListener
{
	/// <summary>Called once per completed frame.</summary>
	void OnFrame(FrameProfile frame);
}

/// <summary>
/// Receives spans as they begin and end (for live profilers such as Tracy). Attached with
/// <see cref="FrameProfiler.SpanSink"/>; called on the thread that records the span.
/// </summary>
public interface ISpanSink
{
	/// <summary>A span starts.</summary>
	void Begin(SpanId span);

	/// <summary>The span started last on this thread ends.</summary>
	void End(SpanId span);

	/// <summary>A frame ends.</summary>
	void FrameMark();
}

/// <summary>
/// What the runtime (reflection-bound) schedule needs to time every step: implemented by <see cref="FrameProfiler"/>.
/// </summary>
public interface IStepProfiler
{
	/// <summary>Whether this profiler can ever record (false for a profiler without a span buffer).</summary>
	bool CanRecord { get; }

	/// <summary>Whether spans are being recorded now (the runtime toggle).</summary>
	bool IsActive { get; }

	/// <summary>Starts a span: returns its start timestamp, or 0 when not recording.</summary>
	long Begin(SpanId span);

	/// <summary>Ends a span started with <see cref="Begin"/> (nothing when <paramref name="start"/> is 0).</summary>
	void End(SpanId span, long start);
}
