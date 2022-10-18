namespace Kyber.Events;

public interface IEvent
{
    ulong Id { get; }
    EventCategory Category { get; }
    bool Handled { get; set; }
}

public interface IEvent<T> : IEvent
{
    T Data { get; }
}

internal struct Event<T>: IEvent<T> {
    public ulong Id { get; init;  }
    public EventCategory Category { get; init; }
    public T Data { get; init; }
    public bool Handled { get; set; }
}
