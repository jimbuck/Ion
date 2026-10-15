namespace Ion;

/// <summary>
/// One <see cref="EventReader{T}"/> per event type, created on first use: for code that reads event types chosen at the
/// call site (a coroutine waiting for any event, a test collector) and must remember what it has read across calls.
/// Systems that know their event types should hold <see cref="EventReader{T}"/> fields instead.
/// </summary>
/// <remarks>Allocates once per event type (the first read of it); later reads do not allocate.</remarks>
public sealed class EventReaderSet(IEvents events)
{
	private readonly IEvents _events = events ?? throw new ArgumentNullException(nameof(events));
	private readonly Dictionary<Type, object> _readers = [];

	/// <summary>The bus this set reads.</summary>
	public IEvents Events => _events;

	/// <summary>The reader of <typeparamref name="T"/> (created on first use), by reference so reads advance it.</summary>
	[ReadsEvent]
	public ref EventReader<T> Reader<T>() where T : unmanaged
	{
		if (!_readers.TryGetValue(typeof(T), out var box))
		{
			box = new Box<T> { Reader = _events.Reader<T>() };
			_readers.Add(typeof(T), box);
		}

		return ref ((Box<T>)box).Reader;
	}

	/// <inheritdoc cref="EventReader{T}.TryRead"/>
	[ReadsEvent]
	public bool TryRead<T>(out T e) where T : unmanaged => Reader<T>().TryRead(out e);

	/// <inheritdoc cref="EventReader{T}.TryReadLatest"/>
	[ReadsEvent]
	public bool TryReadLatest<T>(out T e) where T : unmanaged => Reader<T>().TryReadLatest(out e);

	/// <inheritdoc cref="EventReader{T}.Any"/>
	[ReadsEvent]
	public bool Any<T>() where T : unmanaged => Reader<T>().Any();

	private sealed class Box<T> where T : unmanaged
	{
		public EventReader<T> Reader;
	}
}
