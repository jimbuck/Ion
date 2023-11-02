namespace Kyber;

internal record struct Event<T>(uint Id, T? Data, bool Handled) : IEvent<T>
{
	public Event(uint id) : this(id, default, false) { }
	public Event(uint id, T data) : this(id, data, false) { }
}