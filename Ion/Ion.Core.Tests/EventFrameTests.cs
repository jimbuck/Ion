namespace Ion.Tests;

public class EventFrameTests
{
	public record struct PingEvent(int Value);

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void EventsEmittedInFrameNAreVisibleInFrameNPlusOneButNotNPlusTwo()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(PingSystem));
		var system = host.Get<PingSystem>();
		system.EmitOnFrame = 3;
		var loop = host.BuildLoop();

		loop.RunFrames(8);

		// The First-stage reader runs before the emit in frame 3, so it first sees the event in frame 4, only once.
		Assert.Equal([4u], system.FirstSeen);
		// A reader that looks at every frame's end sees it in the frame it was emitted, and never again.
		Assert.Equal([3u], system.LastSeen);
		// A reader that only looks in frame N+1 sees it; one that only looks in frame N+2 does not.
		Assert.True(system.SeenByNextFrameProbe);
		Assert.False(system.SeenByFrameAfterNextProbe);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheLoopStopsOnExitGameEvent()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		host.Get<ExitAfterFramesSystem>().Frames = 3;

		host.BuildLoop().RunFrames(10);

		Assert.Equal(3, host.Get<TestSystem>().UpdateCount);
	}

	public sealed class PingSystem(IEvents events)
	{
		private EventReader<PingEvent> _first = events.Reader<PingEvent>();
		private EventReader<PingEvent> _last = events.Reader<PingEvent>();
		private EventReader<PingEvent> _nextFrameProbe = events.Reader<PingEvent>();
		private EventReader<PingEvent> _frameAfterNextProbe = events.Reader<PingEvent>();

		public uint EmitOnFrame { get; set; }
		public List<uint> FirstSeen { get; } = [];
		public List<uint> LastSeen { get; } = [];
		public bool SeenByNextFrameProbe { get; private set; }
		public bool SeenByFrameAfterNextProbe { get; private set; }

		// Frames are counted from 1 here to read naturally; GameTime.Frame is zero-based.
		[First]
		public void First(GameTime dt)
		{
			var frame = dt.Frame + 1;
			if (_first.TryRead(out _)) FirstSeen.Add(frame);
			if (frame == EmitOnFrame + 1) SeenByNextFrameProbe = _nextFrameProbe.Any();
			if (frame == EmitOnFrame + 2) SeenByFrameAfterNextProbe = _frameAfterNextProbe.Any();
		}

		[Update]
		public void Update(GameTime dt)
		{
			if (dt.Frame + 1 == EmitOnFrame) events.Emit(new PingEvent(1));
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (_last.TryRead(out _)) LastSeen.Add(dt.Frame + 1);
		}
	}
}
