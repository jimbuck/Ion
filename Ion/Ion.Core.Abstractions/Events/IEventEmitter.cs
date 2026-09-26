namespace Ion;

/// <summary>
/// Emits frame events. Obsolete: an adapter over <see cref="IEvents"/>, kept for one release.
/// </summary>
[Obsolete(EventAdapters.EmitterMessage)]
public interface IEventEmitter
{
	/// <summary>Emits the default value of <typeparamref name="T"/>.</summary>
	[EmitsEvent]
	void Emit<T>() where T : unmanaged;

	/// <summary>Emits <paramref name="data"/>.</summary>
	[EmitsEvent]
	void Emit<T>(T data) where T : unmanaged;
}

/// <summary>The messages of the obsolete event adapters.</summary>
internal static class EventAdapters
{
	public const string EmitterMessage = "IEventEmitter is an adapter over IEvents and will be removed in the next release. Inject IEvents and call Emit<T>(in T).";
	public const string ListenerMessage = "IEventListener is an adapter over IEvents and will be removed in the next release. Inject IEvents and keep an EventReader<T> from Reader<T>(), created once in the constructor, in a field that is not readonly.";
	public const string FactoryMessage = "IEventListenerFactory is an adapter over IEvents and will be removed in the next release. Create an EventReader<T> (or an EventReaderSet) from IEvents instead.";
}
