using Ion.Core;

namespace Ion.Tests;

/// <summary>
/// Things that happen once (events, loop stages) must reach FixedUpdate consumers exactly once whatever the ratio of
/// <see cref="GameConfig.MaxFPS"/> to <see cref="GameConfig.FixedUpdateRate"/>; at 120 fps and 60 Hz about every other
/// frame runs no fixed step.
/// </summary>
public class FixedStepConsumerTests
{
	public record struct PingEvent(int Value);

	private static LoopTestHost Host120FpsAt60Hz(ManualClock clock, params Type[] systems) =>
		new(clock, c => { c.MaxFPS = 120; c.FixedUpdateRate = 60; }, systems: systems);

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoopContextReportsEveryStageAndCountsFixedSteps()
	{
		var clock = new ManualClock();
		using var host = Host120FpsAt60Hz(clock, typeof(StageRecorder));
		var recorder = host.Get<StageRecorder>();
		var loop = host.BuildLoop();
		var context = host.Get<ILoopContext>();

		Assert.Same(context, loop.Context);
		Assert.Equal(GameLoopStage.None, context.Stage);

		loop.RunFrames(40);

		Assert.Equal(GameLoopStage.None, context.Stage);
		Assert.Equal(GameLoopStage.Init, recorder.Stages[0]);
		Assert.Equal(GameLoopStage.Destroy, recorder.Stages[^1]);
		Assert.All(recorder.FixedStages, s => Assert.Equal(GameLoopStage.FixedUpdate, s));
		Assert.Equal(recorder.FixedStages.Count, context.FixedStepCount);

		// Fixed steps are numbered 1, 2, 3, ... in the order they run.
		Assert.Equal(Enumerable.Range(1, recorder.FixedStages.Count).Select(i => (long)i), recorder.FixedStepIndices);

		// At 120 fps and 60 Hz some frames run no fixed step, and none runs more than one.
		Assert.Contains(0, recorder.FixedStepsPerFrame);
		Assert.Contains(1, recorder.FixedStepsPerFrame);
		Assert.All(recorder.FixedStepsPerFrame, n => Assert.InRange(n, 0, 1));
		Assert.InRange(context.FixedStepCount, 15, 20);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UntimedStepRunsOneFixedStepAndSetsTheStage()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(StageRecorder));
		var recorder = host.Get<StageRecorder>();
		var loop = host.BuildLoop();

		loop.Step(new GameTime { Delta = 1f / 60f });
		loop.Step(new GameTime { Delta = 1f / 60f });

		Assert.Equal(2, loop.Context.FixedStepCount);
		Assert.Equal([GameLoopStage.First, GameLoopStage.FixedUpdate, GameLoopStage.Update, GameLoopStage.Render, GameLoopStage.Last], recorder.Stages.Take(5));
		Assert.Equal(GameLoopStage.None, loop.Context.Stage);
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	[InlineData(8)]
	public void EventEmittedInUpdateOnAnyFrameIsSeenOnceByFixedAndOnceByUpdateListeners(int emitFrame)
	{
		var clock = new ManualClock();
		using var host = Host120FpsAt60Hz(clock, typeof(EventConsumers), typeof(EventProducer));
		var consumers = host.Get<EventConsumers>();
		var producer = host.Get<EventProducer>();
		producer.EmitInUpdateOnFrame = (uint)emitFrame;
		var loop = host.BuildLoop();

		for (var i = 0; i < 20; i++) loop.Step();

		Assert.Equal(1, consumers.SeenInFixedUpdate);
		Assert.Equal(1, consumers.SeenInUpdate);
		Assert.Equal(1, consumers.SeenByCoroutineStyleUpdatePoll);
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public void EventEmittedInFixedUpdateIsSeenOnceByAnEarlierFixedListener(int emitFixedStep)
	{
		var clock = new ManualClock();
		// The consumers run before the producer in each fixed step, so they can only see the event in a later step,
		// which may be two frames later.
		using var host = Host120FpsAt60Hz(clock, typeof(EventConsumers), typeof(EventProducer));
		var consumers = host.Get<EventConsumers>();
		var producer = host.Get<EventProducer>();
		producer.EmitInFixedStep = emitFixedStep;
		var loop = host.BuildLoop();

		for (var i = 0; i < 24; i++) loop.Step();

		Assert.Equal(1, consumers.SeenInFixedUpdate);
		Assert.Equal(1, consumers.SeenInUpdate);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void FixedStepBacklogIsBoundedWhenNoFixedStepRuns()
	{
		// The clock never moves, so no fixed step ever runs.
		var clock = new ManualClock { AdvanceOnSleep = false };
		using var host = new LoopTestHost(clock, systems: typeof(EventProducer));
		var producer = host.Get<EventProducer>();
		producer.EmitEveryUpdate = true;
		var emitter = host.Get<EventEmitter>();
		var loop = host.BuildLoop();

		for (var i = 0; i < EventEmitter.MaxBacklogFrames + 50; i++) loop.Step();

		Assert.Equal(0, loop.Context.FixedStepCount);
		Assert.InRange(emitter.FixedStepBacklog.Count, 1, EventEmitter.MaxBacklogFrames);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EmitterWithoutALoopKeepsTheTwoFrameWindow()
	{
		var emitter = new EventEmitter();
		using var listener = new EventListener(emitter);

		emitter.Emit(new PingEvent(1));
		emitter.Step();
		emitter.Step();

		Assert.Equal(0, emitter.FixedStepBacklog.Count);
		Assert.False(listener.On<PingEvent>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void HandledEventsAreNotKeptForFixedSteps()
	{
		var clock = new ManualClock { AdvanceOnSleep = false };
		using var host = new LoopTestHost(clock, systems: typeof(EventProducer));
		var producer = host.Get<EventProducer>();
		producer.EmitInUpdateOnFrame = 0;
		producer.MarkHandled = true;
		var emitter = host.Get<EventEmitter>();
		var loop = host.BuildLoop();

		for (var i = 0; i < 4; i++) loop.Step();

		Assert.Equal(0, emitter.FixedStepBacklog.Count);
	}

	public sealed class StageRecorder(ILoopContext context)
	{
		private int _stepsThisFrame;

		public List<GameLoopStage> Stages { get; } = [];
		public List<GameLoopStage> FixedStages { get; } = [];
		public List<long> FixedStepIndices { get; } = [];
		public List<int> FixedStepsPerFrame { get; } = [];

		[Init] public void Init(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); next(dt); }
		[First] public void First(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); _stepsThisFrame = 0; next(dt); }

		[FixedUpdate]
		public void FixedUpdate(GameTime dt, GameLoopDelegate next)
		{
			Stages.Add(context.Stage);
			FixedStages.Add(context.Stage);
			FixedStepIndices.Add(context.FixedStepCount);
			_stepsThisFrame++;
			next(dt);
		}

		[Update] public void Update(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); next(dt); }
		[Render] public void Render(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); next(dt); }
		[Last] public void Last(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); FixedStepsPerFrame.Add(_stepsThisFrame); next(dt); }
		[Destroy] public void Destroy(GameTime dt, GameLoopDelegate next) { Stages.Add(context.Stage); next(dt); }
	}

	public sealed class EventConsumers(IEventListenerFactory listeners)
	{
		private readonly IEventListener _fixed = listeners.CreateListener();
		private readonly IEventListener _update = listeners.CreateListener();
		private readonly IEventListener _poll = listeners.CreateListener();

		public int SeenInFixedUpdate { get; private set; }
		public int SeenInUpdate { get; private set; }
		public int SeenByCoroutineStyleUpdatePoll { get; private set; }

		[FixedUpdate]
		public void FixedUpdate(GameTime dt, GameLoopDelegate next)
		{
			while (_fixed.On<PingEvent>()) SeenInFixedUpdate++;
			next(dt);
		}

		[Update]
		public void Update(GameTime dt, GameLoopDelegate next)
		{
			// Before the producer in the pipeline, like a coroutine runner registered early.
			if (_poll.OnLatest<PingEvent>()) SeenByCoroutineStyleUpdatePoll++;
			next(dt);
		}

		[Last]
		public void Last(GameTime dt, GameLoopDelegate next)
		{
			while (_update.On<PingEvent>()) SeenInUpdate++;
			next(dt);
		}
	}

	public sealed class EventProducer(IEventEmitter emitter, IEventListenerFactory listeners)
	{
		private readonly IEventListener _marker = listeners.CreateListener();
		private int _fixedSteps;

		public uint? EmitInUpdateOnFrame { get; set; }
		public int? EmitInFixedStep { get; set; }
		public bool EmitEveryUpdate { get; set; }
		public bool MarkHandled { get; set; }

		[FixedUpdate]
		public void FixedUpdate(GameTime dt, GameLoopDelegate next)
		{
			_fixedSteps++;
			if (_fixedSteps == EmitInFixedStep) emitter.Emit(new PingEvent(_fixedSteps));
			next(dt);
		}

		[Update]
		public void Update(GameTime dt, GameLoopDelegate next)
		{
			if (EmitEveryUpdate || dt.Frame == EmitInUpdateOnFrame)
			{
				emitter.Emit(new PingEvent((int)dt.Frame));
				if (MarkHandled && _marker.On<PingEvent>(out var e)) e.Handled = true;
			}

			next(dt);
		}
	}
}
