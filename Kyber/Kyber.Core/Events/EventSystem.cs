namespace Kyber.Builder;

internal class EventSystem
{
	private readonly EventEmitter _eventEmitter;

	public EventSystem(IEventEmitter eventEmitter)
	{
		_eventEmitter = (EventEmitter)eventEmitter;
	}

	[Last]
	public void StepEvents(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		_eventEmitter.Step();
	}
}
