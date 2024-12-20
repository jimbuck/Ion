﻿namespace Ion;

internal record struct Event<T>(uint EventId, int EventType, T Data, bool Handled) : IEvent<T> where T : unmanaged
{
	public Event(uint id) : this(id, typeof(T).GetHashCode(), default, false) { }
	public Event(uint id, T data) : this(id, typeof(T).GetHashCode(), data, false) { }
}