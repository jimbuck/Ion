using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// The storage of one event type on an <see cref="EventBus"/>. See <see cref="EventChannel{T}"/>.
/// </summary>
public abstract class EventChannel
{
	private protected EventChannel(EventBus bus, int id, Type eventType)
	{
		Bus = bus;
		Id = id;
		EventType = eventType;
	}

	/// <summary>The bus that owns this channel.</summary>
	public EventBus Bus { get; }

	/// <summary>The event type's id (<see cref="EventId{T}"/>), used by traces and logs.</summary>
	public int Id { get; }

	/// <summary>The event type.</summary>
	public Type EventType { get; }

	/// <summary>The number of events the channel can hold before it grows. Channels never shrink.</summary>
	public abstract int Capacity { get; }

	/// <summary>The number of events emitted in the current frame.</summary>
	public abstract int CurrentFrameCount { get; }

	/// <summary>The number of events emitted in the previous frame (still visible to every reader).</summary>
	public abstract int PreviousFrameCount { get; }

	/// <summary>The number of older events kept for readers in FixedUpdate steps (see <see cref="EventBus.MaxBacklogFrames"/>).</summary>
	public abstract int BacklogCount { get; }

	/// <summary>The total number of events emitted on this channel.</summary>
	public abstract long EmittedCount { get; }

	internal abstract void Step(bool keepBacklog);

	internal abstract void MarkFixedStep();

	/// <inheritdoc/>
	public override string ToString() => $"{EventType.Name} (id {Id}): {CurrentFrameCount} this frame, {PreviousFrameCount} last frame, {BacklogCount} backlog, capacity {Capacity}";
}

/// <summary>
/// The events of type <typeparamref name="T"/>, unboxed. One array holds the fixed-step backlog, the previous frame and
/// the current frame, in that order; every event has a sequence number, and readers (<see cref="EventReader{T}"/>) keep
/// the sequence number of the next event they have not read.
/// </summary>
/// <remarks>
/// Emitting appends to the array; it grows (doubling) when full and never shrinks. At the end of a frame
/// (<see cref="EventBus.Step"/>) the events that no reader can see any more are dropped by moving the window; the
/// retained events are moved to the front of the array when that copies no more than it drops (or the dropped prefix is
/// half the array), so the current frame is written at the start of the same array every frame and a large fixed-step
/// backlog is not copied every frame. Not thread-safe: emit and read from the game loop's thread.
/// </remarks>
public sealed class EventChannel<T> : EventChannel where T : unmanaged
{

	private T[] _items;
	private int _end;

	// Sequence number of _items[0].
	private long _base;
	// Oldest retained event (the fixed-step backlog starts here), first event of the previous frame, of the current frame.
	private long _backlogStart;
	private long _previousStart;
	private long _currentStart;
	// Events at or after this sequence number were emitted after the most recent fixed step started.
	private long _fixedMark;

	// The first sequence number of each frame since the most recent fixed step (a ring), to bound the backlog in frames.
	private long[] _frameStarts = new long[4];
	private int _frameHead;
	private int _frameCount;

	internal EventChannel(EventBus bus, int id, int capacity) : base(bus, id, typeof(T))
	{
		_items = new T[Math.Max(1, capacity)];
	}

	/// <inheritdoc/>
	public override int Capacity => _items.Length;

	/// <inheritdoc/>
	public override int CurrentFrameCount => (int)(EndSequence - _currentStart);

	/// <inheritdoc/>
	public override int PreviousFrameCount => (int)(_currentStart - _previousStart);

	/// <inheritdoc/>
	public override int BacklogCount => (int)(_previousStart - _backlogStart);

	/// <inheritdoc/>
	public override long EmittedCount => EndSequence;

	/// <summary>The sequence number the next emitted event gets.</summary>
	internal long EndSequence
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _base + _end;
	}

	/// <summary>The oldest event a reader can see now: the backlog during a FixedUpdate step, else the previous frame.</summary>
	internal long ReadStart
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Bus.InFixedStep ? _backlogStart : _previousStart;
	}

	/// <summary>Appends <paramref name="e"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit(in T e)
	{
		if (_end == _items.Length) Grow();
		_items[_end++] = e;
	}

	/// <summary>Creates a reader that starts at the oldest visible event.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public EventReader<T> Reader() => new(this, 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow()
	{
		// The old array stays valid for spans handed out earlier in the frame.
		var grown = new T[_items.Length * 2];
		Array.Copy(_items, grown, _end);
		_items = grown;
		Bus.OnChannelGrew(this);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ref readonly T At(long sequence) => ref _items[(int)(sequence - _base)];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ReadOnlySpan<T> Slice(long start, long end) => new(_items, (int)(start - _base), (int)(end - start));

	internal override void MarkFixedStep()
	{
		_fixedMark = EndSequence;
		_frameCount = 0;
	}

	internal override void Step(bool keepBacklog)
	{
		var end = EndSequence;
		if (_backlogStart == end)
		{
			// Nothing visible or kept (the common case for most event types): start over at the front of the array. No
			// frame starts need recording, since a later backlog only holds events emitted after this frame.
			_backlogStart = _previousStart = _currentStart = _base = end;
			_end = 0;
			_frameCount = 0;
			return;
		}

		_previousStart = _currentStart;
		_currentStart = end;

		long keepFrom;
		if (keepBacklog)
		{
			// The frame that just ended started at _previousStart. Keep at most MaxBacklogFrames frames of backlog.
			PushFrameStart(_previousStart);
			keepFrom = _fixedMark;
			if (_frameCount == EventBus.MaxBacklogFrames) keepFrom = Math.Max(keepFrom, _frameStarts[_frameHead]);
		}
		else
		{
			_frameCount = 0;
			keepFrom = _previousStart;
		}

		_backlogStart = Math.Clamp(keepFrom, _backlogStart, _previousStart);

		var dead = (int)(_backlogStart - _base);
		if (_backlogStart == end)
		{
			// Nothing retained: start over at the front of the array.
			_base = end;
			_end = 0;
		}
		else if (dead > 0 && (_end - dead <= dead || dead >= _items.Length / 2))
		{
			Array.Copy(_items, dead, _items, 0, _end - dead);
			_base += dead;
			_end -= dead;
		}
	}

	private void PushFrameStart(long start)
	{
		if (_frameCount == _frameStarts.Length && _frameCount < EventBus.MaxBacklogFrames)
		{
			// Grow the ring (rare: only while frames pass without a fixed step), keeping its order.
			var grown = new long[Math.Min(_frameStarts.Length * 2, EventBus.MaxBacklogFrames)];
			for (var i = 0; i < _frameCount; i++) grown[i] = _frameStarts[(_frameHead + i) % _frameStarts.Length];
			_frameStarts = grown;
			_frameHead = 0;
		}

		if (_frameCount == _frameStarts.Length)
		{
			// Full at MaxBacklogFrames: overwrite the oldest.
			_frameStarts[_frameHead] = start;
			_frameHead = (_frameHead + 1) % _frameStarts.Length;
			return;
		}

		_frameStarts[(_frameHead + _frameCount) % _frameStarts.Length] = start;
		_frameCount++;
	}
}
