using Ion.Extensions.Debug;

namespace Ion;

/// <summary>
/// Ends the event frame at the very end of every frame (Last, order <see cref="StageOrder.Events"/>), after every other
/// Last step has read and emitted its events, and logs event channels that grow past their initial capacity (by event id,
/// see <see cref="EventId{T}"/>).
/// </summary>
internal partial class EventSystem
{
	private readonly EventBus _events;
	private readonly ITraceTimer _trace;
	private readonly ILogger _logger;

	public EventSystem(EventBus events, ITraceTimer<EventSystem> trace, ILogger<EventSystem> logger)
	{
		_events = events;
		_trace = trace;
		_logger = logger;
		events.ChannelGrew += OnChannelGrew;
	}

	[Last(Order = StageOrder.Events)]
	public void StepEvents(GameTime dt)
	{
		var timer = _trace.Start("Step");
		_events.Step();
		timer.Stop();
	}

	private void OnChannelGrew(EventChannel channel) => LogChannelGrew(_logger, channel.Id, channel.EventType.Name, channel.Capacity);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Event channel {EventId} ({EventName}) grew to {Capacity} events; it does not allocate again unless a frame emits more.")]
	private static partial void LogChannelGrew(ILogger logger, int eventId, string eventName, int capacity);
}
