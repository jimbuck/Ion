using System.Diagnostics.CodeAnalysis;

namespace Kyber;

public interface IEventListener
{
	bool On<T>();
	bool On<T>([NotNullWhen(true)] out IEvent<T>? data);

	bool OnLatest<T>();
	bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? data);
}