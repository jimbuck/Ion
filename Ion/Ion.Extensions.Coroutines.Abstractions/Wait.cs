namespace Ion;

public static class Wait
{
	public static IWait For(float delay) => new WaitFor(delay);

	public static IWait For<TEvent>() where TEvent : unmanaged => new WaitForEvent<TEvent>();

	public static IWait Until(Func<bool> predicate) => new WaitUntil(predicate);
	public static IWait While(Func<bool> predicate) => new WaitUntil(() => !predicate());
}

public record struct WaitFor(float Delay) : IWait
{
	public readonly bool IsReady => Delay <= 0f;
	public void Update(GameTime dt, IEventListener eventListener) => Delay -= dt;
}

public record struct WaitUntil(Func<bool> Predicate) : IWait
{
	public readonly bool IsReady => Predicate();
	public void Update(GameTime dt, IEventListener eventListener) { }
}

public record struct WaitForEvent<TEvent>() : IWait where TEvent : unmanaged
{
	private bool _isReady = false;
	public readonly bool IsReady => _isReady;
	public void Update(GameTime dt, IEventListener eventListener) { 
		if (eventListener.On<TEvent>()) _isReady = true;
	}
}