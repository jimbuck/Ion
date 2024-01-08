namespace Ion;

public interface IWait
{
	bool IsReady { get; }
	void Update(GameTime dt, IEventListener eventListener);
}
