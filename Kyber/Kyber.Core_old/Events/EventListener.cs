namespace Kyber;

public interface IEventListener
{
    bool On<T>();
    bool On<T>([NotNullWhen(true)] out IEvent<T>? data);

	bool OnLatest<T>();
	bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? data);
}

internal class EventListener : IEventListener, IDisposable
{
    public readonly EventEmitter _eventEmitter;

    private HashSet<ulong> _currFrameSeenEvents = new();
    private HashSet<ulong> _prevFrameKnownEvents = new();

    public EventListener(IEventEmitter eventEmitter)
    {
        _eventEmitter = (EventEmitter)eventEmitter;
		_eventEmitter.AttachListener(this);
    }

    public bool On<T>()
    {
        return On<T>(out _);
    }

    public bool On<T>([NotNullWhen(true)]out IEvent<T>? @event)
    {
        foreach(var e in _eventEmitter.GetEvents<T>())
        {
            if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

			@event = e;
            _currFrameSeenEvents.Add(e.Id);
            return true;
        }

        @event = default;
        return false;
    }

	public bool OnLatest<T>()
	{
		return OnLatest<T>(out _);
	}

	public bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? @event)
    {
        @event = default;
        foreach (var e in _eventEmitter.GetEvents<T>())
        {
            if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

            @event = e;
            _currFrameSeenEvents.Add(e.Id);
        }

        return @event != default;
    }

    public void UpdateKnownEvents()
    {
		_prevFrameKnownEvents = _currFrameSeenEvents;
		_currFrameSeenEvents = new HashSet<ulong>();
    }

    public void Dispose()
    {
        _eventEmitter.DetachListener(this);
    }
}
