namespace Kyber;

public interface IEvent
{
	int EventType { get; }
	uint Id { get; }
	bool Handled { get; set; }
}

public interface IEvent<T> : IEvent where T : unmanaged
{
	T Data { get; }
}