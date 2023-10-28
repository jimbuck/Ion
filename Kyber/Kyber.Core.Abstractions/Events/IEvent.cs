namespace Kyber;

public interface IEvent
{
	ulong Id { get; }
	bool Handled { get; set; }
}

public interface IEvent<T> : IEvent
{
	T? Data { get; }
}