using System.Diagnostics.Metrics;

namespace Ion.Extensions.Metrics;

/// <summary>
/// The <c>Ion</c> <see cref="Meter"/>: frame-level aggregates for <c>dotnet-counters</c> and OpenTelemetry. Engine
/// instruments are named <c>ion.*</c>; game instruments keep the name they were registered with.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>ion.frame.duration</c> (histogram, ms): the wall-clock time of every frame.</item>
/// <item><c>ion.fps</c> (gauge): frames per second, averaged over the frames since the last collection.</item>
/// <item><c>ion.frames</c> (counter): frames completed.</item>
/// <item><c>ion.frame.fixed_steps</c>, <c>ion.frame.draw_calls</c>, <c>ion.frame.sprites</c>, <c>ion.frame.triangles</c>,
/// <c>ion.frame.entities</c>, <c>ion.frame.events_emitted</c>, <c>ion.frame.allocated_bytes</c>,
/// <c>ion.frame.gc_gen0</c>, <c>ion.frame.gc_gen1</c>, <c>ion.frame.gc_gen2</c> (gauges): the last frame's counters.</item>
/// </list>
/// </remarks>
public sealed class IonMeter : IDisposable
{
	/// <summary>The meter name (<c>dotnet-counters monitor --counters Ion</c>).</summary>
	public const string MeterName = "Ion";

	private readonly Histogram<double> _frameDuration;
	private FrameStats _last;
	private long _frames;
	private long _fpsFrames;
	private long _fpsStart = System.Diagnostics.Stopwatch.GetTimestamp();
	private double _fps;

	/// <summary>Creates the meter and the engine instruments.</summary>
	public IonMeter()
	{
		Meter = new Meter(MeterName, typeof(IonMeter).Assembly.GetName().Version?.ToString());

		_frameDuration = Meter.CreateHistogram<double>("ion.frame.duration", "ms", "Wall-clock time of every frame.");
		Meter.CreateObservableGauge("ion.fps", ReadFps, "{frame}/s", "Frames per second since the last collection.");
		Meter.CreateObservableCounter("ion.frames", () => Volatile.Read(ref _frames), "{frame}", "Frames completed.");
		Gauge("ion.frame.fixed_steps", "{step}", "FixedUpdate steps in the last frame.", static s => s.FixedSteps);
		Gauge("ion.frame.draw_calls", "{call}", "Draw calls in the last frame.", static s => s.DrawCalls);
		Gauge("ion.frame.sprites", "{sprite}", "Sprites drawn in the last frame.", static s => s.Sprites);
		Gauge("ion.frame.triangles", "{triangle}", "Triangles drawn in the last frame.", static s => s.Triangles);
		Gauge("ion.frame.entities", "{entity}", "Live entities at the end of the last frame.", static s => s.Entities);
		Gauge("ion.frame.events_emitted", "{event}", "Events emitted in the last frame.", static s => s.EventsEmitted);
		Gauge("ion.frame.allocated_bytes", "By", "Bytes allocated by the loop thread during the last frame.", static s => s.AllocatedBytes);
		Gauge("ion.frame.gc_gen0", "{collection}", "Generation 0 collections during the last frame.", static s => s.Gc0);
		Gauge("ion.frame.gc_gen1", "{collection}", "Generation 1 collections during the last frame.", static s => s.Gc1);
		Gauge("ion.frame.gc_gen2", "{collection}", "Generation 2 collections during the last frame.", static s => s.Gc2);
	}

	/// <summary>The meter.</summary>
	public Meter Meter { get; }

	/// <summary>Records a completed frame.</summary>
	public void OnFrame(in FrameStats stats)
	{
		_last = stats;
		Interlocked.Increment(ref _frames);
		Interlocked.Increment(ref _fpsFrames);
		_frameDuration.Record(stats.FrameMs);
	}

	/// <summary>Publishes a game instrument.</summary>
	public void Add(MetricsInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);

		switch (instrument)
		{
			case MetricsCounter counter:
				Meter.CreateObservableCounter(counter.Name, () => counter.Value, counter.Unit, counter.Description);
				break;
			case MetricsGauge gauge:
				Meter.CreateObservableGauge(gauge.Name, () => gauge.Value, gauge.Unit, gauge.Description);
				break;
			case MetricsHistogram histogram:
				var published = Meter.CreateHistogram<double>(histogram.Name, histogram.Unit, histogram.Description);
				histogram.Recorded = published.Record;
				break;
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Meter.Dispose();

	private void Gauge(string name, string unit, string description, Func<FrameStats, long> read) =>
		Meter.CreateObservableGauge(name, () => read(_last), unit, description);

	private double ReadFps()
	{
		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		var frames = Interlocked.Exchange(ref _fpsFrames, 0);
		var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Exchange(ref _fpsStart, now), now).TotalSeconds;
		if (seconds > 0 && frames > 0) _fps = frames / seconds;
		return _fps;
	}
}
