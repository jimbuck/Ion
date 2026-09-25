using System.Runtime.CompilerServices;

namespace Ion.Benchmarks;

public record struct PingEvent(int Value);
public record struct PongEvent(float Value);
public record struct HitEvent(int A, int B);
public record struct ScoreEvent(long Score);

/// <summary>
/// Cost of the frame-event system: <see cref="EventEmitter.Emit{T}"/> boxes each struct event into the <see cref="IEvent"/> ring buffer,
/// and every <see cref="EventListener.On{T}"/> scans both frames' buffers with type checks plus a per-listener HashSet of seen ids.
/// A minimal typed-channel prototype is included as the comparison point for an unboxed design.
/// </summary>
[MemoryDiagnoser]
public class EventBenchmarks
{
	[Params(1, 8)]
	public int Listeners { get; set; }

	private const int EventsPerType = 25; // 4 types -> 100 events per frame

	private EventEmitter _emitter = null!;
	private EventListener[] _listeners = null!;
	private TypedChannels _typed = null!;

	[GlobalSetup]
	public void Setup()
	{
		_emitter = new EventEmitter();
		_listeners = new EventListener[Listeners];
		for (var i = 0; i < Listeners; i++) _listeners[i] = new EventListener(_emitter);
		_typed = new TypedChannels(Listeners);
	}

	[Benchmark(Baseline = true)]
	public void Ion_Emit100_Step()
	{
		Emit100(_emitter);
		_emitter.Step();
	}

	[Benchmark]
	public int Ion_Emit100_PollAllListeners_Step()
	{
		Emit100(_emitter);
		var handled = 0;
		foreach (var l in _listeners)
		{
			while (l.On<PingEvent>(out var e)) handled += e.Data.Value;
			while (l.On<PongEvent>(out var e)) handled += (int)e.Data.Value;
			while (l.On<HitEvent>(out var e)) handled += e.Data.A;
			while (l.On<ScoreEvent>(out var e)) handled += (int)e.Data.Score;
		}
		_emitter.Step();
		return handled;
	}

	[Benchmark]
	public int Ion_Emit100_OnLatestAllListeners_Step()
	{
		Emit100(_emitter);
		var handled = 0;
		foreach (var l in _listeners)
		{
			if (l.OnLatest<PingEvent>(out var e)) handled += e.Data.Value;
			if (l.OnLatest<ScoreEvent>(out var s)) handled += (int)s.Data.Score;
		}
		_emitter.Step();
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

	private static void Emit100(EventEmitter emitter)
	{
		for (var i = 0; i < EventsPerType; i++)
		{
			emitter.Emit(new PingEvent(i));
			emitter.Emit(new PongEvent(i));
			emitter.Emit(new HitEvent(i, i));
			emitter.Emit(new ScoreEvent(i));
		}
	}

	/// <summary>Unboxed, per-type double-buffered event channel with a read cursor per listener (design sketch, not engine code).</summary>
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
