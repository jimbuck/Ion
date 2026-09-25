namespace Ion;

/// <summary>
/// Default <see cref="IEventListenerFactory"/>: creates <see cref="EventListener"/> instances attached to the
/// application's <see cref="IEventEmitter"/>. The listeners are not tracked by the container.
/// </summary>
internal sealed class EventListenerFactory(IEventEmitter eventEmitter) : IEventListenerFactory
{
	public IEventListener CreateListener() => new EventListener(eventEmitter);
}
