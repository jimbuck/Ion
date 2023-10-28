namespace Kyber;

internal record struct Event<T>(ulong Id, T? Data, bool Handled) : IEvent<T>
{
	public Event(ulong id) : this(id, default, false) { }
	public Event(ulong id, T data) : this(id, data, false) { }
}