namespace Ion;

/// <summary>
/// Creates <see cref="IEventListener"/> instances. Obsolete: an adapter over <see cref="IEvents"/>, kept for one release.
/// </summary>
[Obsolete(EventAdapters.FactoryMessage)]
public interface IEventListenerFactory
{
	/// <summary>Creates a listener that reads the application's <see cref="IEvents"/>.</summary>
	IEventListener CreateListener();
}
