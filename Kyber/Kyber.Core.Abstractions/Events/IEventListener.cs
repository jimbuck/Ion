using System.Diagnostics.CodeAnalysis;

namespace Kyber;

public interface IEventListener : IEventEmitter, IDisposable
{
	bool On<T>() where T : unmanaged;
	bool On<T>([NotNullWhen(true)] out IEvent<T>? data) where T : unmanaged;

	bool OnLatest<T>() where T : unmanaged;
	bool OnLatest<T>([NotNullWhen(true)] out IEvent<T>? data) where T : unmanaged;
}