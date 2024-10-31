namespace Ion;

public interface IEvent
{
	int EventType { get; }
	uint EventId { get; }
	bool Handled { get; set; }
}

public interface IEvent<T> : IEvent where T : unmanaged
{
	T Data { get; }
}

public interface IEvent2 { }