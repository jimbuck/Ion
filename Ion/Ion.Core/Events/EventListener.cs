using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ion;

public class EventListener : IEventListener
{
    private readonly EventEmitter _eventEmitter;

	// Ids of visible events this listener has returned; pruned by UpdateKnownEvents once they are no longer visible.
	private readonly HashSet<ulong> _seenEvents = new(16);
	private readonly Predicate<ulong> _isStale;
	private ulong _pruneBelow;

	/// <summary>
	/// Creates a listener attached to <paramref name="eventEmitter"/>. Dispose it to detach it.
	/// </summary>
	/// <param name="eventEmitter">The engine's event emitter, whose frame buffers the listener reads.</param>
	public EventListener(EventEmitter eventEmitter)
	{
		ArgumentNullException.ThrowIfNull(eventEmitter);
		_eventEmitter = eventEmitter;
		_isStale = id => id < _pruneBelow;
		_eventEmitter.AttachListener(this);
	}

	/// <summary>
	/// Compatibility overload for callers that only hold an <see cref="IEventEmitter"/>. The listener reads the engine's
	/// frame buffers, so <paramref name="eventEmitter"/> must be an <see cref="EventEmitter"/>; prefer the
	/// <see cref="EventListener(EventEmitter)"/> overload or <see cref="IEventListenerFactory"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="eventEmitter"/> is not an <see cref="EventEmitter"/>.</exception>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public EventListener(IEventEmitter eventEmitter)
		: this(eventEmitter as EventEmitter ?? throw new ArgumentException($"{nameof(EventListener)} needs the engine's {nameof(EventEmitter)}; resolve {nameof(IEventListener)} or {nameof(IEventListenerFactory)} from the container instead.", nameof(eventEmitter)))
	{
	}

	/// <summary>
	/// Returns true (once per event) when an unhandled event of type <typeparamref name="T"/> that this listener has not
	/// seen is visible: one emitted this frame or the previous one, oldest first. From FixedUpdate, older events that no
	/// fixed step has had a chance to see are visible too (see <see cref="EventEmitter"/>).
	/// </summary>
	public bool On<T>() where T : unmanaged => On<T>(out _);

	/// <inheritdoc cref="On{T}()"/>
	public bool On<T>([NotNullWhen(true)] out IEvent<T>? @event) where T : unmanaged
	{
		if (_eventEmitter.InFixedStep && _takeFirst(_eventEmitter.FixedStepBacklog, out @event)) return true;
		if (_takeFirst(_eventEmitter.PreviousFrameEvents, out @event)) return true;
		return _takeFirst(_eventEmitter.CurrentFrameEvents, out @event);
	}

	/// <summary>
	/// Marks every visible unseen event of type <typeparamref name="T"/> as seen and returns true if there was one.
	/// </summary>
	public bool OnLatest<T>() where T : unmanaged => OnLatest<T>(out _);

	/// <summary>
	/// Marks every visible unseen event of type <typeparamref name="T"/> as seen and returns the newest.
	/// </summary>
	public bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? @event) where T : unmanaged
	{
		@event = default;
		if (_eventEmitter.InFixedStep) _takeAll(_eventEmitter.FixedStepBacklog, ref @event);
		_takeAll(_eventEmitter.PreviousFrameEvents, ref @event);
		_takeAll(_eventEmitter.CurrentFrameEvents, ref @event);
		return @event is not null;
	}

	/// <summary>
	/// Forgets the ids of events that are no longer visible. Called by <see cref="EventEmitter.Step"/>.
	/// </summary>
	public void UpdateKnownEvents()
	{
		if (_seenEvents.Count == 0) return;

		_pruneBelow = _eventEmitter.OldestLiveEventId;
		_seenEvents.RemoveWhere(_isStale);
	}

	private bool _takeFirst<T>(RingBuffer<IEvent> events, [NotNullWhen(true)] out IEvent<T>? @event) where T : unmanaged
	{
		for (var i = 0; i < events.Count; i++)
		{
			var e = events[i];
			if (e.Handled || e is not IEvent<T> typed) continue;
			if (!_seenEvents.Add(e.EventId)) continue;

			@event = typed;
			return true;
		}

		@event = default;
		return false;
	}

	private void _takeAll<T>(RingBuffer<IEvent> events, ref IEvent<T>? latest) where T : unmanaged
	{
		for (var i = 0; i < events.Count; i++)
		{
			var e = events[i];
			if (e.Handled || e is not IEvent<T> typed) continue;
			if (!_seenEvents.Add(e.EventId)) continue;

			latest = typed;
		}
	}

    public void Dispose()
    {
        _eventEmitter.DetachListener(this);
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>() where T : unmanaged
	{
		_eventEmitter.Emit<T>();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>(T data) where T : unmanaged
	{
		_eventEmitter.Emit(data);
	}
}
