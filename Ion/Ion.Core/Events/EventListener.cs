using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ion;

public class EventListener : IEventListener
{
    private readonly EventEmitter _eventEmitter;

	private HashSet<ulong> _currFrameSeenEvents = new(8);
    private HashSet<ulong> _prevFrameKnownEvents = new(8);

	/// <summary>
	/// Creates a listener attached to <paramref name="eventEmitter"/>. Dispose it to detach it.
	/// </summary>
	/// <param name="eventEmitter">The engine's event emitter, whose frame buffers the listener reads.</param>
	public EventListener(EventEmitter eventEmitter)
	{
		ArgumentNullException.ThrowIfNull(eventEmitter);
		_eventEmitter = eventEmitter;
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

    public bool On<T>() where T : unmanaged
	{
		for (var i = 0; i < _eventEmitter.PreviousFrameEvents.Count; i++)
		{
			var e = _eventEmitter.PreviousFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			return true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			return true;
		}

		return false;
	}

    public bool On<T>([NotNullWhen(true)]out IEvent<T>? @event) where T : unmanaged
	{
		for (var i = 0; i < _eventEmitter.PreviousFrameEvents.Count; i++)
		{
			var e = _eventEmitter.PreviousFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			@event = (IEvent<T>)e;
			return true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			@event = (IEvent<T>)e;
			return true;
		}

		@event = default;
		return false;
	}

	public bool OnLatest<T>() where T : unmanaged
	{
		var found = false;
		for (var i = 0; i < _eventEmitter.PreviousFrameEvents.Count; i++)
		{
			var e = _eventEmitter.PreviousFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			found = true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			found = true;
		}

		return found;
	}

	public bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? @event) where T : unmanaged
	{
		@event = default;
		for (var i = 0; i < _eventEmitter.PreviousFrameEvents.Count; i++)
		{
			var e = _eventEmitter.PreviousFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			@event = (IEvent<T>)e;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.EventId) || _currFrameSeenEvents.Contains(e.EventId)) continue;

			_currFrameSeenEvents.Add(e.EventId);
			@event = (IEvent<T>)e;
		}

		return @event != default;
    }

    public void UpdateKnownEvents()
    {
		(_currFrameSeenEvents, _prevFrameKnownEvents) = (_prevFrameKnownEvents, _currFrameSeenEvents);
		_currFrameSeenEvents.Clear();
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
