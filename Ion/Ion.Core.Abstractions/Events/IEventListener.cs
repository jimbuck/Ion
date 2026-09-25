namespace Ion;

/// <summary>
/// Reads frame events, one cursor per event type. Obsolete: an adapter over <see cref="IEvents"/> (an
/// <see cref="EventReaderSet"/>), kept for one release.
/// </summary>
[Obsolete(EventAdapters.ListenerMessage)]
public interface IEventListener : IEventEmitter, IDisposable
{
	/// <summary>Reads the oldest unread event of type <typeparamref name="T"/>, if any.</summary>
	[ReadsEvent]
	bool On<T>() where T : unmanaged;

	/// <summary>Reads the oldest unread event of type <typeparamref name="T"/>, if any.</summary>
	[ReadsEvent]
	bool On<T>(out T data) where T : unmanaged;

	/// <summary>Reads every unread event of type <typeparamref name="T"/> and returns whether there was one.</summary>
	[ReadsEvent]
	bool OnLatest<T>() where T : unmanaged;

	/// <summary>Reads every unread event of type <typeparamref name="T"/> and returns the newest.</summary>
	[ReadsEvent]
	bool OnLatest<T>(out T data) where T : unmanaged;
}
