using Kyber.Extensions.Debug;

namespace Kyber;

internal class EventSystem
{
	private readonly EventEmitter _eventEmitter;
	private readonly ITraceTimer _trace;

	public EventSystem(IEventEmitter eventEmitter, ITraceTimer<EventSystem> trace)
	{
		_eventEmitter = (EventEmitter)eventEmitter;
		_trace = trace;
	}

	[Last]
	public void StepEvents(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		var timer = _trace.Start("Step");
		_eventEmitter.Step();
		timer.Stop();
	}
}
