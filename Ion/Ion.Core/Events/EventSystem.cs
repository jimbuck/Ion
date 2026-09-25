using Ion.Extensions.Debug;

namespace Ion;

internal class EventSystem(EventEmitter eventEmitter, ITraceTimer<EventSystem> trace)
{
	[Last]
	public void StepEvents(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		var timer = trace.Start("Step");
		eventEmitter.Step();
		timer.Stop();
	}
}
