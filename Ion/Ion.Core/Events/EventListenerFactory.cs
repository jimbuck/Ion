namespace Ion;

/// <summary>
/// Obsolete adapter: creates <see cref="EventListener"/> instances over the application's <see cref="IEvents"/>.
/// </summary>
#pragma warning disable CS0618 // The adapters are obsolete together.
internal sealed class EventListenerFactory(IEvents events) : IEventListenerFactory
{
	public IEventListener CreateListener() => new EventListener(events);
}
#pragma warning restore CS0618
