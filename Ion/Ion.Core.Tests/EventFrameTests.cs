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

		// The First-stage listener runs before the emit in frame 3, so it first sees the event in frame 4, only once.
		Assert.Equal([4u], system.FirstSeen);
		// A listener that looks at every frame's end sees it in the frame it was emitted, and never again.
		Assert.Equal([3u], system.LastSeen);
		// A listener that only looks in frame N+1 sees it; one that only looks in frame N+2 does not.
		Assert.True(system.SeenByNextFrameProbe);
		Assert.False(system.SeenByFrameAfterNextProbe);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OnLatestReturnsTheNewestUnseenEvent()
	{
		var emitter = new EventEmitter();
		using var listener = new EventListener(emitter);

		emitter.Emit(new PingEvent(1));
		emitter.Step();
		emitter.Emit(new PingEvent(2));
		emitter.Emit(new PingEvent(3));

		Assert.True(listener.OnLatest<PingEvent>(out var latest));
		Assert.Equal(3, latest.Data.Value);
		Assert.False(listener.OnLatest<PingEvent>(out _));

		emitter.Emit<PingEvent>();
		Assert.True(listener.OnLatest<PingEvent>());
		Assert.False(listener.OnLatest<PingEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OnReadsPreviousFrameEventsFirst()
	{
		var emitter = new EventEmitter();
		using var listener = new EventListener(emitter);

		emitter.Emit(new PingEvent(1));
		emitter.Step();
		emitter.Emit(new PingEvent(2));

		Assert.True(listener.On<PingEvent>(out var first));
		Assert.Equal(1, first.Data.Value);
		Assert.True(listener.On<PingEvent>());
		Assert.False(listener.On<PingEvent>());
		Assert.False(listener.On<ExitGameEvent>(out _));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ListenerEmitsThroughItsEmitterAndDetachesOnDispose()
	{
		var emitter = new EventEmitter();
		var listener = new EventListener(emitter);
		var other = new EventListener(emitter);

		listener.Emit<PingEvent>();
		listener.Emit(new PingEvent(7));

		var events = emitter.GetEvents<PingEvent>();
		var count = 0;
		while (events.MoveNext()) count++;
		Assert.Equal(2, count);

		listener.Dispose();
		other.Dispose();
		emitter.Step();
		Assert.Equal(2, emitter.PreviousFrameEvents.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EmitterGrowsPastItsInitialCapacity()
	{
		var emitter = new EventEmitter();
		using var listener = new EventListener(emitter);

		for (var i = 0; i < 200; i++) emitter.Emit(new PingEvent(i));

		Assert.Equal(200, emitter.CurrentFrameEvents.Count);
		Assert.Equal(199, ((IEvent<PingEvent>)emitter.CurrentFrameEvents[199]).Data.Value);
		Assert.Throws<ArgumentOutOfRangeException>(() => emitter.CurrentFrameEvents[200]);
		Assert.Throws<ArgumentException>(() => new RingBuffer<int>(0));
	}

	public sealed class PingSystem : IDisposable
	{
		private readonly IEventEmitter _emitter;
		private readonly IEventListener _first;
		private readonly IEventListener _last;
		private readonly IEventListener _nextFrameProbe;
		private readonly IEventListener _frameAfterNextProbe;

		public PingSystem(IEventEmitter emitter, IEventListenerFactory listeners)
		{
			_emitter = emitter;
			_first = listeners.CreateListener();
			_last = listeners.CreateListener();
			_nextFrameProbe = listeners.CreateListener();
			_frameAfterNextProbe = listeners.CreateListener();
		}

		public uint EmitOnFrame { get; set; }
		public List<uint> FirstSeen { get; } = [];
		public List<uint> LastSeen { get; } = [];
		public bool SeenByNextFrameProbe { get; private set; }
		public bool SeenByFrameAfterNextProbe { get; private set; }

		// Frames are counted from 1 here to read naturally; GameTime.Frame is zero-based.
		[First]
		public void First(GameTime dt, GameLoopDelegate next)
		{
			var frame = dt.Frame + 1;
			if (_first.On<PingEvent>()) FirstSeen.Add(frame);
			if (frame == EmitOnFrame + 1) SeenByNextFrameProbe = _nextFrameProbe.On<PingEvent>();
			if (frame == EmitOnFrame + 2) SeenByFrameAfterNextProbe = _frameAfterNextProbe.On<PingEvent>();
			next(dt);
		}

		[Update]
		public void Update(GameTime dt, GameLoopDelegate next)
		{
			if (dt.Frame + 1 == EmitOnFrame) _emitter.Emit(new PingEvent(1));
			next(dt);
		}

		[Render]
		public void Render(GameTime dt, GameLoopDelegate next)
		{
			if (_last.On<PingEvent>()) LastSeen.Add(dt.Frame + 1);
			next(dt);
		}

		public void Dispose()
		{
			_first.Dispose();
			_last.Dispose();
			_nextFrameProbe.Dispose();
			_frameAfterNextProbe.Dispose();
		}
	}
}
