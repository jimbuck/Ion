namespace Ion;

/// <summary>
/// Creates <see cref="IEventListener"/> instances whose lifetime the caller owns.
/// </summary>
/// <remarks>
/// Resolving <see cref="IEventListener"/> from the container gives a transient listener that the container keeps
/// alive (and attached to the emitter) until the container itself is disposed. Code that creates listeners on demand,
/// such as one per coroutine, should use this factory instead and dispose each listener when it is done with it.
/// </remarks>
public interface IEventListenerFactory
{
	/// <summary>
	/// Creates a listener attached to the application's <see cref="IEventEmitter"/>. Dispose it to detach it.
	/// </summary>
	IEventListener CreateListener();
}
