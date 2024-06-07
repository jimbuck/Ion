using Ion.Extensions.Debug;

namespace Ion;

internal class EventSystem(IEventEmitter eventEmitter, ITraceTimer<EventSystem> trace)
{
	private readonly EventEmitter _eventEmitter = (EventEmitter)eventEmitter;

	[Last]
	public void StepEvents(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		var timer = trace.Start("Step");
		_eventEmitter.Step();
		timer.Stop();
	}
}
