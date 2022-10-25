namespace Kyber;

public interface IEventListener
{
    bool On<T>();
    bool On<T>([NotNullWhen(true)] out IEvent<T>? data);
    bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? data);
}

public class EventListener : IEventListener, IDisposable
{
    private readonly EventEmitter _eventSystem;

    private HashSet<ulong> _currFrameSeenEvents = new();
    private HashSet<ulong> _prevFrameKnownEvents = new();

    internal EventListener(EventEmitter eventSystem)
    {
        _eventSystem = eventSystem;
    }

    public bool On<T>()
    {
        return On<T>(out _);
    }

    public bool On<T>([NotNullWhen(true)]out IEvent<T>? @event)
    {
        foreach(var e in _eventSystem.GetEvents<T>())
        {
            if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

            @event = e;
            _currFrameSeenEvents.Add(e.Id);
            return true;
        }

        @event = default;
        return false;
    }

    public bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? @event)
    {
        @event = default;
        foreach (var e in _eventSystem.GetEvents<T>())
        {
            if (_prevFrameKnownEvents.Contains(e.Id) || _currFrameSeenEvents.Contains(e.Id)) continue;

            @event = e;
            _currFrameSeenEvents.Add(e.Id);
        }

        return @event != default;
    }

    internal void UpdateKnownEvents()
    {
        (_prevFrameKnownEvents, _currFrameSeenEvents) = (_currFrameSeenEvents, new HashSet<ulong>());
    }

    public void Dispose()
    {
        _eventSystem.DetachListener(this);
    }
}
