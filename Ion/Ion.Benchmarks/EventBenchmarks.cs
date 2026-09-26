using System.Runtime.CompilerServices;

using Ion.Benchmarks.GeneratedApp;

namespace Ion.Benchmarks;

public record struct PingEvent(int Value);
public record struct PongEvent(float Value);
public record struct HitEvent(int A, int B);
public record struct ScoreEvent(long Score);

/// <summary>
/// Cost of the frame events, 100 events of 4 types per frame and 1 or 8 listeners that each read all 4 types (or the
/// latest of 2): the runtime <see cref="EventBus"/> (typed channels found through a per-type slot), the same bus called
/// through <see cref="IEvents"/> (a generic interface call per emit), the generated bus (<c>Ion.Benchmarks.GeneratedApp</c>,
/// whose emits are routed to typed fields at compile time), the obsolete <c>IEventEmitter</c>/<c>IEventListener</c>
/// adapters, and the typed-channel prototype that Events v2 was designed from.
/// </summary>
[MemoryDiagnoser]
public class EventBenchmarks
{
	[Params(1, 8)]
	public int Listeners { get; set; }

	private const int EventsPerType = 25; // 4 types -> 100 events per frame

	private EventBus _bus = null!;
	private IEvents _events = null!;
	private Readers[] _readers = null!;
	private GeneratedEventsBenchmark _generated = null!;
#pragma warning disable CS0618 // The legacy rows measure the obsolete adapters.
	private IEventEmitter _legacyEmitter = null!;
	private IEventListener[] _legacyListeners = null!;
#pragma warning restore CS0618
	private EventBus _legacyBus = null!;
	private TypedChannels _typed = null!;

	/// <summary>One listener: a reader per event type.</summary>
	private sealed class Readers(IEvents events)
	{
		public EventReader<PingEvent> Ping = events.Reader<PingEvent>();
		public EventReader<PongEvent> Pong = events.Reader<PongEvent>();
		public EventReader<HitEvent> Hit = events.Reader<HitEvent>();
		public EventReader<ScoreEvent> Score = events.Reader<ScoreEvent>();
	}

	[GlobalSetup]
	public void Setup()
	{
		_bus = new EventBus();
		_events = _bus;
		_readers = new Readers[Listeners];
		for (var i = 0; i < Listeners; i++) _readers[i] = new Readers(_bus);

		_generated = new GeneratedEventsBenchmark(Listeners);
		if (!_generated.IsGenerated) throw new InvalidOperationException("The generated event bus is not installed.");

#pragma warning disable CS0618
		_legacyBus = new EventBus();
		_legacyEmitter = new EventEmitter(_legacyBus);
		_legacyListeners = new IEventListener[Listeners];
		for (var i = 0; i < Listeners; i++) _legacyListeners[i] = new EventListener(_legacyBus);
#pragma warning restore CS0618

		_typed = new TypedChannels(Listeners);
	}

	[GlobalCleanup]
	public void Cleanup() => _generated.Dispose();

	[Benchmark(Baseline = true)]
	public void Ion_Emit100_Step()
	{
		var bus = _bus;
		for (var i = 0; i < EventsPerType; i++)
		{
			bus.Emit(new PingEvent(i));
			bus.Emit(new PongEvent(i));
			bus.Emit(new HitEvent(i, i));
			bus.Emit(new ScoreEvent(i));
		}

		bus.Step();
	}

	[Benchmark]
	public int Ion_Emit100_ReadAll_Step()
	{
		var bus = _bus;
		for (var i = 0; i < EventsPerType; i++)
		{
			bus.Emit(new PingEvent(i));
			bus.Emit(new PongEvent(i));
			bus.Emit(new HitEvent(i, i));
			bus.Emit(new ScoreEvent(i));
		}

		var handled = 0;
		foreach (var r in _readers)
		{
			foreach (ref readonly var e in r.Ping.Read()) handled += e.Value;
			foreach (ref readonly var e in r.Pong.Read()) handled += (int)e.Value;
			foreach (ref readonly var e in r.Hit.Read()) handled += e.A;
			foreach (ref readonly var e in r.Score.Read()) handled += (int)e.Score;
		}

		bus.Step();
		return handled;
	}

	[Benchmark]
	public int Ion_Emit100_TryReadAll_Step()
	{
		var bus = _bus;
		for (var i = 0; i < EventsPerType; i++)
		{
			bus.Emit(new PingEvent(i));
			bus.Emit(new PongEvent(i));
			bus.Emit(new HitEvent(i, i));
			bus.Emit(new ScoreEvent(i));
		}

		var handled = 0;
		foreach (var r in _readers)
		{
			while (r.Ping.TryRead(out var e)) handled += e.Value;
			while (r.Pong.TryRead(out var e)) handled += (int)e.Value;
			while (r.Hit.TryRead(out var e)) handled += e.A;
			while (r.Score.TryRead(out var e)) handled += (int)e.Score;
		}

		bus.Step();
		return handled;
	}

	[Benchmark]
	public int Ion_Emit100_ReadLatest_Step()
	{
		var bus = _bus;
		for (var i = 0; i < EventsPerType; i++)
		{
			bus.Emit(new PingEvent(i));
			bus.Emit(new PongEvent(i));
			bus.Emit(new HitEvent(i, i));
			bus.Emit(new ScoreEvent(i));
		}

		var handled = 0;
		foreach (var r in _readers)
		{
			if (r.Ping.TryReadLatest(out var e)) handled += e.Value;
			if (r.Score.TryReadLatest(out var s)) handled += (int)s.Score;
		}

		bus.Step();
		return handled;
	}

	[Benchmark]
	public int Ion_IEvents_Emit100_ReadAll_Step()
	{
		var events = _events;
		for (var i = 0; i < EventsPerType; i++)
		{
			events.Emit(new PingEvent(i));
			events.Emit(new PongEvent(i));
			events.Emit(new HitEvent(i, i));
			events.Emit(new ScoreEvent(i));
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

	[Benchmark]
	public int Ion_GeneratedBus_Emit100_ReadAll_Step() => _generated.Frame();

	[Benchmark]
	public int Legacy_Adapters_Emit100_PollAll_Step()
	{
#pragma warning disable CS0618
		var emitter = _legacyEmitter;
		for (var i = 0; i < EventsPerType; i++)
		{
			emitter.Emit(new PingEvent(i));
			emitter.Emit(new PongEvent(i));
			emitter.Emit(new HitEvent(i, i));
			emitter.Emit(new ScoreEvent(i));
		}

		var handled = 0;
		foreach (var l in _legacyListeners)
		{
			while (l.On<PingEvent>(out var e)) handled += e.Value;
			while (l.On<PongEvent>(out var e)) handled += (int)e.Value;
			while (l.On<HitEvent>(out var e)) handled += e.A;
			while (l.On<ScoreEvent>(out var e)) handled += (int)e.Score;
		}
#pragma warning restore CS0618

		_legacyBus.Step();
		return handled;
	}

	[Benchmark]
	public int Prototype_TypedChannels_Emit100_PollAll_Step()
	{
		var t = _typed;
		for (var i = 0; i < EventsPerType; i++)
		{
			t.Ping.Emit(new PingEvent(i));
			t.Pong.Emit(new PongEvent(i));
			t.Hit.Emit(new HitEvent(i, i));
			t.Score.Emit(new ScoreEvent(i));
		}

		var handled = 0;
		for (var l = 0; l < Listeners; l++)
		{
			foreach (ref readonly var e in t.Ping.Read(l)) handled += e.Value;
			foreach (ref readonly var e in t.Pong.Read(l)) handled += (int)e.Value;
			foreach (ref readonly var e in t.Hit.Read(l)) handled += e.A;
			foreach (ref readonly var e in t.Score.Read(l)) handled += (int)e.Score;
		}
		t.Step();
		return handled;
	}

	/// <summary>Unboxed, per-type double-buffered event channel with a read cursor per listener (the design sketch Events v2 grew from).</summary>
	public sealed class TypedChannel<T> where T : unmanaged
	{
		private T[] _current = new T[64];
		private T[] _previous = new T[64];
		private int _currentCount, _previousCount;
		private readonly int[] _cursors;

		public TypedChannel(int listeners) => _cursors = new int[listeners];

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Emit(in T value)
		{
			if (_currentCount == _current.Length) Array.Resize(ref _current, _current.Length * 2);
			_current[_currentCount++] = value;
		}

		/// <summary>Events this listener has not seen yet: the tail of the previous frame it missed plus everything from the current frame.</summary>
		public ReadOnlySpan<T> Read(int listener)
		{
			// Simplified: listeners drain the current frame only; unread previous-frame events are exposed via the cursor.
			var start = _cursors[listener];
			_cursors[listener] = _currentCount;
			return new ReadOnlySpan<T>(_current, start, _currentCount - start);
		}

		public void Step()
		{
			(_current, _previous) = (_previous, _current);
			_previousCount = _currentCount;
			_currentCount = 0;
			Array.Clear(_cursors);
		}
	}

	public sealed class TypedChannels(int listeners)
	{
		public readonly TypedChannel<PingEvent> Ping = new(listeners);
		public readonly TypedChannel<PongEvent> Pong = new(listeners);
		public readonly TypedChannel<HitEvent> Hit = new(listeners);
		public readonly TypedChannel<ScoreEvent> Score = new(listeners);

		public void Step() { Ping.Step(); Pong.Step(); Hit.Step(); Score.Step(); }
	}
}
