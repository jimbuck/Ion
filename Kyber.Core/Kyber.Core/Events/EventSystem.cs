namespace Kyber;

public interface IEventEmitter
{
    void Emit<T>();
    void Emit<T>(T data);
}

internal class EventSystem : IPreUpdateSystem, IEventEmitter
{
	private readonly ConcurrentQueue<IEvent> _currFrame = new();
	private IEvent[] _prevFrame = Array.Empty<IEvent>();
	private readonly List<EventListener> _listeners = new();
	private ulong _nextId = 0;

	public bool IsEnabled { get; set; } = true;

    public void PreUpdate(float dt)
    {
		_prevFrame = _currFrame.Where(e => !e.Handled).ToArray();
		_currFrame.Clear();

        foreach (var listener in _listeners) listener.UpdateKnownEvents();
    }

	public void Emit<T>()
	{
		_currFrame.Enqueue(new Event<T>(Interlocked.Increment(ref _nextId)));
	}

	public void Emit<T>(T data)
	{
		_currFrame.Enqueue(new Event<T>(Interlocked.Increment(ref _nextId), data));
	}

	internal IEventListener CreateListener()
	{
		var listener = new EventListener(this);
		_listeners.Add(listener);
		return listener;
	}

	internal void DetachListener(EventListener listener)
    {
        _listeners.Remove(listener);
    }

	internal IEnumerable<IEvent<T>> GetEvents<T>()
	{
		var type = typeof(T);
		foreach (var e in _getEvents<T>(_prevFrame, type)) yield return e;
		foreach (var e in _getEvents<T>(_currFrame, type)) yield return e;
	}

	private IEnumerable<IEvent<T>> _getEvents<T>(IEnumerable<IEvent> events, Type type)
	{
		foreach (var e in events)
		{
			if (e.Handled || e is not IEvent<T>) continue;
			yield return (IEvent<T>)e;
		}
	}
}