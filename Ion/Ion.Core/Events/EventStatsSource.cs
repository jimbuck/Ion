namespace Ion;

/// <summary>
/// Writes <see cref="FrameStats.EventsEmitted"/>: the events emitted on the bus since the previous frame, across every channel.
/// </summary>
internal sealed class EventStatsSource(EventBus events) : IFrameStatsSource
{
	private long _previous;

	public void Collect(ref FrameStats stats)
	{
		var channels = events.Channels;
		long total = 0;
		for (var i = 0; i < channels.Count; i++) total += channels[i].EmittedCount;

		stats.EventsEmitted = total - _previous;
		_previous = total;
	}
}
