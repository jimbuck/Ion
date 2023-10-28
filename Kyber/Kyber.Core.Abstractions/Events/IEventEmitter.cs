
namespace Kyber;

public interface IEventEmitter
{
	void Emit<T>();
	void Emit<T>(T data);
}
