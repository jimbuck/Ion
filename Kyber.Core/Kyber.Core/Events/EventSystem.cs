namespace Kyber;

internal class EventSystem : IPreUpdateSystem
{
	private readonly EventEmitter _eventEmitter;

	public bool IsEnabled { get; set; } = true;

	public EventSystem(IEventEmitter eventEmitter)
	{
		_eventEmitter = (EventEmitter)eventEmitter;
	}

	public void PreUpdate(float dt)
	{
		_eventEmitter.Step();
	}
}