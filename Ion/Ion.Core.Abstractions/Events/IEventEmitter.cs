
namespace Ion;

public interface IEventEmitter
{
	void Emit<T>() where T : unmanaged;
	void Emit<T>(T data) where T : unmanaged;
}