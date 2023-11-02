namespace Kyber;

public interface IEvent
{
	uint Id { get; }
	bool Handled { get; set; }
}

public interface IEvent<T> : IEvent
{
	T? Data { get; }
}