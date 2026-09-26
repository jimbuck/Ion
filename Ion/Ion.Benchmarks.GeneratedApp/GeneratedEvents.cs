using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ion.Benchmarks.GeneratedApp;

public record struct GeneratedPingEvent(int Value);
public record struct GeneratedPongEvent(float Value);
public record struct GeneratedHitEvent(int A, int B);
public record struct GeneratedScoreEvent(long Score);

/// <summary>
/// The <c>EventBenchmarks</c> frame (100 events of 4 types, every listener reads all 4) on the generated event bus: the
/// application's <c>CreateBuilder</c> call installs it, and the <c>Emit</c>/<c>Reader</c> calls below are routed to its
/// typed channel fields at compile time.
/// </summary>
public sealed class GeneratedEventsBenchmark : IDisposable
{
	private const int EventsPerType = 25;

	private readonly IonApplication _app;
	private readonly IEvents _events;
	private readonly EventBus _bus;
	private readonly Readers[] _readers;

	/// <summary>One listener: a reader per event type.</summary>
	private sealed class Readers(IEvents events)
	{
		public EventReader<GeneratedPingEvent> Ping = events.Reader<GeneratedPingEvent>();
		public EventReader<GeneratedPongEvent> Pong = events.Reader<GeneratedPongEvent>();
		public EventReader<GeneratedHitEvent> Hit = events.Reader<GeneratedHitEvent>();
		public EventReader<GeneratedScoreEvent> Score = events.Reader<GeneratedScoreEvent>();
	}

	public GeneratedEventsBenchmark(int listeners)
	{
		var builder = IonApplication.CreateBuilder([]);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		_app = builder.Build();
		_events = _app.Services.GetRequiredService<IEvents>();
		_bus = _app.Services.GetRequiredService<EventBus>();
		_readers = new Readers[listeners];
		for (var i = 0; i < listeners; i++) _readers[i] = new Readers(_events);
	}

	/// <summary>Whether the application runs on the generated bus (it must, or the benchmark measures the wrong thing).</summary>
	public bool IsGenerated => _bus.GetType().Name.EndsWith("GeneratedEventBus", StringComparison.Ordinal) && EventId<GeneratedPingEvent>.IsGenerated;

	/// <summary>Emits 100 events, lets every listener read them all, and ends the frame.</summary>
	public int Frame()
	{
		var events = _events;
		for (var i = 0; i < EventsPerType; i++)
		{
			events.Emit(new GeneratedPingEvent(i));
			events.Emit(new GeneratedPongEvent(i));
			events.Emit(new GeneratedHitEvent(i, i));
			events.Emit(new GeneratedScoreEvent(i));
		}

		var handled = 0;
		foreach (var r in _readers)
		{
			foreach (ref readonly var e in r.Ping.Read()) handled += e.Value;
			foreach (ref readonly var e in r.Pong.Read()) handled += (int)e.Value;
			foreach (ref readonly var e in r.Hit.Read()) handled += e.A;
			foreach (ref readonly var e in r.Score.Read()) handled += (int)e.Score;
		}

		_bus.Step();
		return handled;
	}

	public void Dispose() => _app.Dispose();
}
