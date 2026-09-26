using System.Diagnostics.Metrics;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using Ion.Extensions.Graphics;

namespace Ion.Tests;

public record struct BallSpawned(int Id);

/// <summary>Draws three rectangles and a string every frame, counts balls and records a histogram.</summary>
public sealed class DrawingSystem(ISpriteBatch spriteBatch, IMetrics metrics, IEvents events)
{
	private readonly MetricsCounter _balls = metrics.Counter("balls");
	private readonly MetricsGauge _speed = metrics.Gauge("speed", "px/s");
	private readonly MetricsHistogram _bounces = metrics.Histogram("bounce_angle", "deg");
	private readonly SpanId _span = metrics.Span("DrawingSystem.Work");
	private EventReader<BallSpawned> _spawned = events.Reader<BallSpawned>();

	public int Spawned { get; private set; }

	[Update]
	public void Update(GameTime dt)
	{
		using var _ = metrics.Profiler.Scope(_span);
		_balls.Increment();
		_speed.Set(120.5);
		_bounces.Record(10);
		_bounces.Record(30);
		events.Emit(new BallSpawned(1));
		events.Emit(new BallSpawned(2));
	}

	[Last]
	public void Last(GameTime dt) => Spawned += _spawned.Read().Length;

	[Render]
	public void Render(GameTime dt)
	{
		for (var i = 0; i < 3; i++) spriteBatch.DrawRect(Color.Red, new Vector2(i * 10, 0), new Vector2(8, 8));
	}
}

public class MetricsModuleTests
{
	private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), $"ion-metrics-{Guid.NewGuid():N}{extension}");

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheTestHostExposesTheLastFrameStats()
	{
		using var host = new IonTestHost().WithSystem<DrawingSystem>();

		Assert.Equal(default, host.LastFrame);
		host.Step(3);

		var frame = host.LastFrame;
		Assert.Equal(2u, frame.Frame);
		Assert.Equal(3, frame.DrawCalls);
		Assert.Equal(3, frame.Sprites);
		Assert.Equal(6, frame.Triangles);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(2, frame.EventsEmitted);
		Assert.Equal(0, frame.Entities);
		Assert.Equal(1000.0 / 60, frame.DeltaMs, 1);
		Assert.True(frame.FrameMs > 0);
		Assert.Equal(0, frame.Spans); // Profiling is off by default.
		Assert.Same(host.Get<IMetrics>(), host.Metrics);
		Assert.Equal(frame, host.Metrics.LastFrame);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ProfilingRecordsTheStagesStepsAndScopes()
	{
		using var host = new IonTestHost().WithSystem<DrawingSystem>().WithConfiguration("Ion:Metrics:Profiling", "true");
		host.Step(2);

		Assert.True(host.Metrics.IsProfiling);
		var names = host.Metrics.Profiler.GetFrame(0).Spans.ToArray().Select(s => s.Id.Name).ToHashSet();
		Assert.Contains("Update", names);
		Assert.Contains("Render", names);
		Assert.Contains("DrawingSystem.Update", names);
		Assert.Contains("DrawingSystem.Work", names);
		Assert.Contains("NullSpriteBatchSystem.Begin", names); // The sprite batch scope.
		Assert.Contains("EventSystem.Step", names);
		Assert.True(host.LastFrame.Spans > 5);

		host.Metrics.IsProfiling = false;
		host.Step();
		Assert.Equal(0, host.LastFrame.Spans);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void CountersAreHandlesRegisteredOnce()
	{
		using var host = new IonTestHost().WithSystem<DrawingSystem>();
		host.Step(4);

		var metrics = host.Metrics;
		var balls = metrics.Counter("balls");
		Assert.Same(balls, metrics.Counter("balls"));
		Assert.Equal(4, balls.Value);
		balls.Add(6);
		Assert.Equal(10, balls.Value);

		Assert.Equal(120.5, metrics.Gauge("speed").Value);
		Assert.Equal("px/s", metrics.Gauge("speed").Unit);

		// Histograms are summarized per frame: reset when the frame ends.
		var bounces = metrics.Histogram("bounce_angle");
		Assert.Equal(0, bounces.Count);
		Assert.Equal(8, bounces.TotalCount);

		Assert.Equal(["balls", "speed", "bounce_angle"], metrics.Instruments.Select(i => i.Name));
		Assert.Throws<InvalidOperationException>(() => metrics.Gauge("balls"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheFrameLogHasOneLinePerFrame()
	{
		var path = TempFile(".jsonl");
		try
		{
			using (var host = new IonTestHost().WithSystem<DrawingSystem>().WithConfiguration("Ion:Metrics:FrameLog", path))
			{
				host.Step(10);
			}

			var lines = File.ReadAllLines(path);
			Assert.Equal(10, lines.Length);

			for (var i = 0; i < lines.Length; i++)
			{
				using var line = JsonDocument.Parse(lines[i]);
				var root = line.RootElement;
				Assert.Equal(i, root.GetProperty("frame").GetInt32());
				Assert.Equal(3, root.GetProperty("draw_calls").GetInt32());
				Assert.Equal(3, root.GetProperty("sprites").GetInt32());
				Assert.Equal(6, root.GetProperty("triangles").GetInt32());
				Assert.Equal(1, root.GetProperty("fixed_steps").GetInt32());
				Assert.Equal(2, root.GetProperty("events_emitted").GetInt32());
				Assert.True(root.GetProperty("frame_ms").GetDouble() >= 0);
				foreach (var name in new[] { "delta_ms", "idle_ms", "work_ms", "fps", "entities", "gc_gen0", "gc_gen1", "gc_gen2", "allocated_bytes", "spans", "dropped_spans" })
				{
					Assert.True(root.TryGetProperty(name, out _), name);
				}

				Assert.Equal(i + 1, root.GetProperty("counters").GetProperty("balls").GetInt64());
				Assert.Equal(120.5, root.GetProperty("gauges").GetProperty("speed").GetDouble());
				var bounces = root.GetProperty("histograms").GetProperty("bounce_angle");
				Assert.Equal(2, bounces.GetProperty("count").GetInt32());
				Assert.Equal(40, bounces.GetProperty("sum").GetDouble());
				Assert.Equal(10, bounces.GetProperty("min").GetDouble());
				Assert.Equal(30, bounces.GetProperty("max").GetDouble());
			}
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WritingAFrameLogLineDoesNotAllocateAfterWarmUp()
	{
		using var stream = new MemoryStream(1 << 22);
		using var log = new FrameLogWriter(stream, flushFrames: 1);
		MetricsInstrument[] instruments = [new MetricsCounter("a"), new MetricsGauge("b"), new MetricsHistogram("c")];
		var stats = new FrameStats { Frame = 1, FrameMs = 1.25, DrawCalls = 3 };

		for (var i = 0; i < 100; i++) log.Write(stats, instruments);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1000; i++) log.Write(stats, instruments);
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
		Assert.Equal(1100, log.Lines);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheChromeTraceLoadsWithOneEventPerSpanAndFrame()
	{
		var path = TempFile(".json");
		try
		{
			using var host = new IonTestHost().WithSystem<DrawingSystem>().WithConfiguration("Ion:Metrics:Profiling", "true");
			host.Step(5);

			var frames = new List<FrameProfile>();
			host.Metrics.Profiler.CopyFrames(frames, 3);
			var spans = frames.Sum(f => f.Spans.Length);
			var threads = frames.Select(f => f.ThreadId).Concat(frames.SelectMany(f => f.Spans.ToArray().Select(s => s.ThreadId))).Distinct().Count();

			var written = MetricsExporter.WriteChromeTrace(path, frames);

			using var trace = JsonDocument.Parse(File.ReadAllBytes(path));
			var events = trace.RootElement.GetProperty("traceEvents").EnumerateArray().ToList();
			Assert.Equal(written, events.Count);
			// Metadata (process and threads), then a complete and a counter event per frame, and one per span.
			Assert.Equal(1 + threads + 2 * frames.Count + spans, events.Count);
			Assert.Equal(spans, events.Count(e => e.TryGetProperty("cat", out var c) && c.GetString() == "span"));
			Assert.Equal(3, events.Count(e => e.TryGetProperty("cat", out var c) && c.GetString() == "frame"));
			Assert.Equal(3, events.Count(e => e.GetProperty("ph").GetString() == "C"));

			foreach (var e in events.Where(e => e.GetProperty("ph").GetString() == "X"))
			{
				Assert.False(string.IsNullOrEmpty(e.GetProperty("name").GetString()));
				Assert.True(e.GetProperty("ts").GetDouble() >= 0);
				Assert.True(e.GetProperty("dur").GetDouble() >= 0);
				Assert.Equal(1, e.GetProperty("pid").GetInt32());
				e.GetProperty("tid").GetInt32();
			}

			var frameEvent = events.First(e => e.TryGetProperty("cat", out var c) && c.GetString() == "frame");
			Assert.Equal("Frame 2", frameEvent.GetProperty("name").GetString());
			Assert.Equal(3, frameEvent.GetProperty("args").GetProperty("draw_calls").GetInt32());
			Assert.Contains(events, e => e.GetProperty("name").GetString() == "DrawingSystem.Update");
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void CaptureRecordsTheNextFramesThenRestoresTheToggle()
	{
		var path = TempFile(".json");
		try
		{
			using var host = new IonTestHost().WithSystem<DrawingSystem>();
			host.Step(2);
			Assert.False(host.Metrics.IsProfiling);

			host.Metrics.Capture(3, path);
			Assert.True(host.Metrics.IsCapturing);
			Assert.True(host.Metrics.IsProfiling);

			host.Step(2);
			Assert.False(File.Exists(path));
			host.Step();

			Assert.False(host.Metrics.IsCapturing);
			Assert.False(host.Metrics.IsProfiling);
			Assert.Equal(Path.GetFullPath(path), host.Metrics.LastTracePath);

			using var trace = JsonDocument.Parse(File.ReadAllBytes(path));
			var frameNames = trace.RootElement.GetProperty("traceEvents").EnumerateArray()
				.Where(e => e.TryGetProperty("cat", out var c) && c.GetString() == "frame")
				.Select(e => e.GetProperty("name").GetString())
				.ToList();
			Assert.Equal(["Frame 2", "Frame 3", "Frame 4"], frameNames);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheCaptureKeyCapturesATrace()
	{
		var path = TempFile(".json");
		try
		{
			using var host = new IonTestHost()
				.WithConfiguration("Ion:Metrics:TraceOutput", path)
				.WithConfiguration("Ion:Metrics:CaptureFrames", "2");
			host.Step();

			host.Input.Press(Key.F9);
			host.Step();
			Assert.True(host.Metrics.IsCapturing);
			host.Input.Release(Key.F9);
			host.Step(2);

			Assert.True(File.Exists(path));
			Assert.False(host.Metrics.IsCapturing);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ProfilingOnAtShutdownWritesTheTrace()
	{
		var path = TempFile(".json");
		try
		{
			using (var host = new IonTestHost()
				.WithConfiguration("Ion:Metrics:Profiling", "true")
				.WithConfiguration("Ion:Metrics:TraceOutput", path)
				.WithConfiguration("Ion:Metrics:HistoryFrames", "4"))
			{
				host.Step(10);
			}

			using var trace = JsonDocument.Parse(File.ReadAllBytes(path));
			var frames = trace.RootElement.GetProperty("traceEvents").EnumerateArray()
				.Count(e => e.TryGetProperty("cat", out var c) && c.GetString() == "frame");
			Assert.Equal(4, frames); // Bounded by the history.
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheIonMeterPublishesTheFrameAndGameInstruments()
	{
		using var host = new IonTestHost().WithSystem<DrawingSystem>();
		host.Start();
		var meter = host.Get<MetricsService>().Meter!.Meter;

		var instruments = new List<string>();
		var durations = new List<double>();
		var observed = new Dictionary<string, double>();
		using var listener = new MeterListener
		{
			InstrumentPublished = (instrument, l) =>
			{
				if (!ReferenceEquals(instrument.Meter, meter)) return;
				instruments.Add(instrument.Name);
				l.EnableMeasurementEvents(instrument);
			},
		};
		listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
		{
			if (instrument.Name == "ion.frame.duration") durations.Add(value);
			else observed[instrument.Name] = value;
		});
		listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => observed[instrument.Name] = value);
		listener.Start();

		host.Step(3);
		listener.RecordObservableInstruments();

		Assert.Equal(IonMeter.MeterName, meter.Name);
		foreach (var name in new[] { "ion.frame.duration", "ion.fps", "ion.frames", "ion.frame.fixed_steps", "ion.frame.draw_calls", "ion.frame.sprites", "ion.frame.triangles", "ion.frame.entities", "ion.frame.events_emitted", "ion.frame.allocated_bytes", "ion.frame.gc_gen0", "ion.frame.gc_gen1", "ion.frame.gc_gen2", "balls", "speed", "bounce_angle" })
		{
			Assert.Contains(name, instruments);
		}

		Assert.Equal(3, durations.Count);
		Assert.Equal(3, observed["ion.frame.draw_calls"]);
		Assert.Equal(3, observed["ion.frames"]);
		Assert.Equal(3, observed["balls"]);
		Assert.Equal(120.5, observed["speed"]);
		Assert.Equal(30, observed["bounce_angle"]);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheOverlaySourceDescribesTheLastFrames()
	{
		using var host = new IonTestHost().WithSystem<DrawingSystem>().WithConfiguration("Ion:Metrics:OverlayRefreshSeconds", "0");
		host.Step(2);

		var overlay = host.Get<IMetricsOverlaySource>();
		var lines = overlay.Lines;
		Assert.Equal(1, overlay.Version);
		Assert.Contains(lines, l => l.Contains("fps", StringComparison.Ordinal));
		Assert.Contains("draws 3  sprites 3  tris 6", lines);
		Assert.Contains("balls 2", lines);
		Assert.Same(lines, overlay.Lines);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheOverlayIsDrawnWithTheSpriteBatch()
	{
		using var host = new IonTestHost()
			.WithConfiguration("Ion:Metrics:Overlay", "true")
			.WithConfiguration("Ion:Metrics:OverlayFont", "Bungee-Regular.ttf")
			.WithConfiguration("Ion:Metrics:OverlayRefreshSeconds", "0");
		host.Step(2);

		var strings = host.SpriteBatch.LastFrame.Commands.Where(c => c.Kind == SpriteBatchCommandKind.String).ToList();
		Assert.Equal(4, strings.Count);
		Assert.Contains("fps", strings[0].Text);
		Assert.Equal(1, host.SpriteBatch.LastFrame.Rects);
	}

#pragma warning disable CS0618 // The 0.2 registration forwarders.
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AddDebugUtilsForwardsToMetrics()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ion:Debug:TraceEnabled"] = "true", ["Ion:Debug:TraceOutput"] = "legacy.json" });
		Ion.Extensions.Debug.BuilderExtensions.AddDebugUtils(builder.Services, builder.Configuration);
		using var app = builder.Build();
		Ion.Extensions.Debug.BuilderExtensions.UseDebugUtils(app);

		var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetricsConfig>>().Value;
		Assert.True(options.Profiling);
		Assert.Equal("legacy.json", options.TraceOutput);
		Assert.True(app.Services.GetRequiredService<IMetrics>().IsProfiling);
		Assert.IsType<TraceManagerAdapter>(app.Services.GetRequiredService<Ion.Extensions.Debug.ITraceManager>());
	}
#pragma warning restore CS0618
}
