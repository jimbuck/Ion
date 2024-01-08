using System.Runtime.CompilerServices;

namespace Ion;

public class EventListener : IEventListener
{
    private readonly EventEmitter _eventEmitter;


	private HashSet<ulong> _currFrameSeenEvents = new(8);
    private HashSet<ulong> _prevFrameKnownEvents = new(8);

    public EventListener(IEventEmitter eventEmitter)
    {
        _eventEmitter = (EventEmitter)eventEmitter;
		_eventEmitter.AttachListener(this);
    }

    public bool On<T>() where T : unmanaged
	{
		for (var i = 0; i < _eventEmitter.PreviousFrameEvents.Count; i++)
		{
			var e = _eventEmitter.PreviousFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
			return true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
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
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
			@event = (IEvent<T>)e;
			return true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
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
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
			found = true;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
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
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
			@event = (IEvent<T>)e;
		}

		for (var i = 0; i < _eventEmitter.CurrentFrameEvents.Count; i++)
		{
			var e = _eventEmitter.CurrentFrameEvents[i];
			if (e.Handled || e is not IEvent<T>) continue;
			if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			_currFrameSeenEvents.Add(e.Id);
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
