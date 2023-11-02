using System.Runtime.CompilerServices;

namespace Kyber;

public class EventEmitter : IEventEmitter
{
	private readonly ConcurrentQueue<IEvent> _currFrame = new();
	private IEvent[] _prevFrame = Array.Empty<IEvent>();
	private readonly List<EventListener> _listeners = new();
	private uint _nextId = 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Step()
    {
		_prevFrame = _currFrame.Where(e => !e.Handled).ToArray();
		_currFrame.Clear();

        foreach (var listener in _listeners) listener.UpdateKnownEvents();
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>()
	{
		_currFrame.Enqueue(new Event<T>(Interlocked.Increment(ref _nextId)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>(T data)
	{
		_currFrame.Enqueue(new Event<T>(Interlocked.Increment(ref _nextId), data));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AttachListener(EventListener listener)
	{
		_listeners.Add(listener);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void DetachListener(EventListener listener)
    {
        _listeners.Remove(listener);
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IEnumerable<IEvent<T>> GetEvents<T>()
	{
		foreach (var e in _getEvents<T>(_prevFrame)) yield return e;
		foreach (var e in _getEvents<T>(_currFrame)) yield return e;
	}

	private IEnumerable<IEvent<T>> _getEvents<T>(IEnumerable<IEvent> events)
	{
		foreach (var e in events)
		{
			if (e.Handled || e is not IEvent<T>) continue;
			yield return (IEvent<T>)e;
		}
	}
}
