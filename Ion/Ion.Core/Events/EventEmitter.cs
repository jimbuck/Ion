using System.Runtime.CompilerServices;

namespace Ion;

public class EventEmitter : IEventEmitter
{
	private RingBuffer<IEvent> _currFrame = new(64);
	private RingBuffer<IEvent> _prevFrame = new(64);

	private readonly List<EventListener> _listeners = new();
	private uint _nextId = 1;

	public RingBuffer<IEvent> CurrentFrameEvents => _currFrame;
	public RingBuffer<IEvent> PreviousFrameEvents => _prevFrame;


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Step()
    {
		(_currFrame, _prevFrame) = (_prevFrame, _currFrame);
		_currFrame.Clear();

        foreach (var listener in _listeners) listener.UpdateKnownEvents();
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>() where T : unmanaged
	{
		_currFrame.Add(new Event<T>(Interlocked.Increment(ref _nextId)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>(T data) where T : unmanaged
	{
		_currFrame.Add(new Event<T>(Interlocked.Increment(ref _nextId), data));
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
	public IEnumerator<IEvent<T>> GetEvents<T>() where T : unmanaged
	{
		for (var i = 0; i < _prevFrame.Count; i++)
		{
			if (_prevFrame[i].Handled || _prevFrame[i] is not IEvent<T>) continue;
			yield return (IEvent<T>)_prevFrame[i];
		}

		for (var i = 0; i < _currFrame.Count; i++)
		{
			if (_currFrame[i].Handled || _currFrame[i] is not IEvent<T>) continue;
			yield return (IEvent<T>)_currFrame[i];
		}
	}
}
