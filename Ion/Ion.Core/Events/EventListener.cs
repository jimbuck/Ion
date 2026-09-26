namespace Ion;

/// <summary>
/// Obsolete adapter: <see cref="IEventListener"/> over <see cref="IEvents"/>, with one <see cref="EventReader{T}"/> per
/// event type (an <see cref="EventReaderSet"/>). Kept for one release.
/// </summary>
[Obsolete(EventAdapterMessages.Listener)]
public class EventListener : IEventListener
{
	private readonly EventReaderSet _readers;

	/// <summary>Creates a listener that reads <paramref name="events"/>. The first read of a type starts at its oldest visible event.</summary>
	public EventListener(IEvents events)
	{
		ArgumentNullException.ThrowIfNull(events);
		_readers = new EventReaderSet(events);
	}

	/// <summary>Creates a listener that reads the bus behind <paramref name="emitter"/>.</summary>
	public EventListener(EventEmitter emitter) : this((emitter ?? throw new ArgumentNullException(nameof(emitter))).Events) { }

	/// <inheritdoc/>
	public bool On<T>() where T : unmanaged => _readers.TryRead<T>(out _);

	/// <inheritdoc/>
	public bool On<T>(out T data) where T : unmanaged => _readers.TryRead(out data);

	/// <inheritdoc/>
	public bool OnLatest<T>() where T : unmanaged => _readers.TryReadLatest<T>(out _);

	/// <inheritdoc/>
	public bool OnLatest<T>(out T data) where T : unmanaged => _readers.TryReadLatest(out data);

	/// <inheritdoc/>
	public void Emit<T>() where T : unmanaged => _readers.Events.Emit(default(T));

	/// <inheritdoc/>
	public void Emit<T>(T data) where T : unmanaged => _readers.Events.Emit(in data);

	/// <summary>Nothing to release: readers are not attached to the bus.</summary>
	public void Dispose() => GC.SuppressFinalize(this);
}
