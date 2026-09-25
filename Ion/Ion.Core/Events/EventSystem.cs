using Ion.Extensions.Debug;

namespace Ion;

/// <summary>
/// Steps the event frame buffers at the very end of every frame (Last, order <see cref="StageOrder.Events"/>), after
/// every other Last step has read and emitted its events.
/// </summary>
internal class EventSystem(EventEmitter eventEmitter, ITraceTimer<EventSystem> trace)
{
	[Last(Order = StageOrder.Events)]
	public void StepEvents(GameTime dt)
	{
		var timer = trace.Start("Step");
		eventEmitter.Step();
		timer.Stop();
	}
}
