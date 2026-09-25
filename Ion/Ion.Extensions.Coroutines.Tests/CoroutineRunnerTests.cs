using System.Collections;

using Ion.Extensions.Coroutines;

namespace Ion.Tests;

public class CoroutineRunnerTests : IDisposable
{
	private readonly IonApplication _app;
	private readonly ICoroutineRunner _runner;
	private readonly GameTime _dt = new() { Frame = 0, Delta = 0.1f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	public CoroutineRunnerTests()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddCoroutines();
		_app = builder.Build();
		_app.UseEvents();
		_runner = _app.Services.GetRequiredService<ICoroutineRunner>();
	}

	public void Dispose() => _app.Dispose();

	private void NextFrame()
	{
		_runner.Update(_dt);
		_dt.Frame++;
	}

	private static IEnumerator Counter(List<int> log, int id, int steps)
	{
		for (var i = 0; i < steps; i++)
		{
			log.Add(id);
			yield return null;
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunnerIsSingletonInTheCoroutinesNamespace()
	{
		Assert.Same(_runner, _app.Services.GetRequiredService<ICoroutineRunner>());
		Assert.IsType<CoroutineRunner>(_runner);
		Assert.Equal("Ion.Extensions.Coroutines", typeof(CoroutineRunner).Namespace);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StartRunsOnUpdateAndCompletes()
	{
		var log = new List<int>();
		var routine = Counter(log, 1, 2);

		_runner.Start(routine);
		Assert.Empty(log);
		Assert.True(_runner.IsActive(routine));
		Assert.Equal(1, _runner.Count);

		NextFrame();
		NextFrame();
		Assert.Equal([1, 1], log);
		Assert.True(_runner.IsActive(routine));

		NextFrame();
		Assert.False(_runner.IsActive(routine));
		Assert.Equal(0, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopUnknownRoutineIsNoOp()
	{
		var ex = Record.Exception(() => _runner.Stop(Counter([], 1, 1)));
		Assert.Null(ex);

		var routine = Counter([], 1, 1);
		_runner.Start(routine);
		_runner.Stop(routine);
		ex = Record.Exception(() => _runner.Stop(routine));
		Assert.Null(ex);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopRemovesOnlyThatRoutine()
	{
		var log = new List<int>();
		var a = Counter(log, 1, 10);
		var b = Counter(log, 2, 10);
		_runner.Start(a);
		_runner.Start(b);

		NextFrame();
		_runner.Stop(a);
		NextFrame();

		Assert.Equal([1, 2, 2], log);
		Assert.False(_runner.IsActive(a));
		Assert.True(_runner.IsActive(b));
		Assert.Equal(1, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopAllClearsEverything()
	{
		var log = new List<int>();
		_runner.Start(Counter(log, 1, 10));
		_runner.Start(Counter(log, 2, 10));

		_runner.StopAll();
		NextFrame();

		Assert.Equal(0, _runner.Count);
		Assert.Empty(log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RoutineStoppingItselfDoesNotSkipOthers()
	{
		var log = new List<int>();
		IEnumerator self = null!;
		IEnumerator SelfStopping()
		{
			log.Add(0);
			_runner.Stop(self);
			yield return null;
			log.Add(99);
		}
		self = SelfStopping();

		_runner.Start(self);
		_runner.Start(Counter(log, 2, 5));

		NextFrame();
		NextFrame();

		Assert.Equal([0, 2, 2], log);
		Assert.Equal(1, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WaitForDelaysBySeconds()
	{
		var log = new List<string>();
		IEnumerator Routine()
		{
			log.Add("start");
			yield return Wait.For(0.25f);
			log.Add("after");
		}

		_runner.Start(Routine());

		NextFrame(); // start, wait 0.25
		NextFrame(); // 0.15
		NextFrame(); // 0.05
		Assert.Equal(["start"], log);

		NextFrame(); // -0.05 => ready
		Assert.Equal(["start", "after"], log);
		Assert.Equal(0, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FloatYieldIsSeconds()
	{
		var log = new List<string>();
		IEnumerator Routine()
		{
			yield return 0.15f;
			log.Add("after");
		}

		_runner.Start(Routine());
		NextFrame();
		NextFrame();
		Assert.Empty(log);
		NextFrame();
		Assert.Equal(["after"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WaitUntilAndWhile()
	{
		var flag = false;
		var log = new List<string>();
		IEnumerator Routine()
		{
			yield return Wait.Until(() => flag);
			log.Add("until");
			yield return Wait.While(() => flag);
			log.Add("while");
		}

		_runner.Start(Routine());
		NextFrame();
		NextFrame();
		Assert.Empty(log);

		flag = true;
		NextFrame();
		Assert.Equal(["until"], log);
		NextFrame();
		Assert.Equal(["until"], log);

		flag = false;
		NextFrame();
		Assert.Equal(["until", "while"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WaitForEventResumesAfterEmit()
	{
		var emitter = _app.Services.GetRequiredService<IEvents>();
		var log = new List<string>();
		IEnumerator Routine()
		{
			yield return Wait.For<TestEvent>();
			log.Add("event");
		}

		_runner.Start(Routine());
		NextFrame();
		NextFrame();
		Assert.Empty(log);

		emitter.Emit(new TestEvent(1));
		NextFrame();
		Assert.Equal(["event"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NestedRoutinesRunToCompletionFirst()
	{
		var log = new List<string>();
		IEnumerator Inner()
		{
			log.Add("inner1");
			yield return null;
			log.Add("inner2");
		}
		IEnumerator Outer()
		{
			log.Add("outer1");
			yield return Inner();
			log.Add("outer2");
		}

		_runner.Start(Outer());
		for (var i = 0; i < 5; i++) NextFrame();

		Assert.Equal(["outer1", "inner1", "inner2", "outer2"], log);
		Assert.Equal(0, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TypedRoutinesSupportEveryWaitKind()
	{
		var flag = false;
		var log = new List<string>();
		var emitter = _app.Services.GetRequiredService<IEvents>();
		IEnumerator<Wait> Inner()
		{
			log.Add("inner");
			yield return Wait.None;
			log.Add("inner-done");
		}
		IEnumerator<Wait> Routine()
		{
			yield return 0.15f;
			log.Add("seconds");
			yield return Wait.Until(() => flag);
			log.Add("until");
			yield return Wait.For<TestEvent>();
			log.Add("event");
			yield return Wait.For(Inner());
			log.Add("nested");
		}

		_runner.Start(Routine());
		NextFrame(); // starts, waits 0.15
		NextFrame(); // 0.05
		Assert.Empty(log);
		NextFrame(); // ready, waits until flag
		Assert.Equal(["seconds"], log);
		NextFrame();
		flag = true;
		NextFrame(); // until, waits for the event
		Assert.Equal(["seconds", "until"], log);
		NextFrame();
		emitter.Emit(new TestEvent(1));
		NextFrame(); // event, yields the nested routine
		Assert.Equal(["seconds", "until", "event"], log);
		NextFrame(); // inner
		NextFrame(); // inner-done, nested
		Assert.Equal(["seconds", "until", "event", "inner", "inner-done", "nested"], log);
		Assert.Equal(0, _runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullAfterAPredicateWaitResumesNextFrame()
	{
		var flag = true;
		var log = new List<int>();
		IEnumerator Routine()
		{
			yield return Wait.Until(() => flag);
			log.Add(1);
			flag = false;
			yield return null;
			log.Add(2);
		}

		_runner.Start(Routine());
		NextFrame();
		NextFrame();
		NextFrame();
		Assert.Equal([1, 2], log);
	}

	private sealed class CountdownWait(int frames) : IWait
	{
		private int _frames = frames;
		public bool IsReady => _frames <= 0;
		public void Update(GameTime dt, EventReaderSet events) => _frames--;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CustomWaitIsUpdatedEveryFrame()
	{
		var log = new List<string>();
		IEnumerator<Wait> Typed()
		{
			yield return Wait.For(new CountdownWait(2));
			log.Add("typed");
		}
		IEnumerator Untyped()
		{
			yield return new CountdownWait(2);
			log.Add("untyped");
		}

		_runner.Start(Typed());
		_runner.Start(Untyped());
		NextFrame();
		NextFrame();
		Assert.Empty(log);
		NextFrame();
		Assert.Equal(["typed", "untyped"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WaitCarriesItsKindAndPayload()
	{
		Func<bool> predicate = () => true;
		Assert.Equal(WaitKind.None, Wait.None.Kind);
		Assert.Equal(WaitKind.Seconds, Wait.For(2f).Kind);
		Assert.Equal(2f, Wait.For(TimeSpan.FromSeconds(2)).Seconds);
		Assert.Equal(1.5f, ((Wait)1.5f).Seconds);
		Assert.Same(predicate, Wait.Until(predicate).Predicate);
		Assert.Equal(WaitKind.While, Wait.While(predicate).Kind);
		Assert.Equal(typeof(TestEvent), Wait.For<TestEvent>().EventType);
		Assert.Equal(WaitKind.Routine, Wait.FromYield(Counter([], 1, 1)).Kind);
		Assert.Equal(WaitKind.Seconds, Wait.FromYield(0.5f).Kind);
		Assert.Equal(WaitKind.None, Wait.FromYield("unknown").Kind);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SteppingTypedRoutinesAllocatesNothing()
	{
		static IEnumerator<Wait> Forever()
		{
			while (true)
			{
				yield return Wait.For(0.001f);
				yield return Wait.None;
				yield return Wait.For<TestEvent>();
			}
		}

		for (var i = 0; i < 100; i++) _runner.Start(Forever());

		// Warm up (JIT, list growth), then measure.
		for (var i = 0; i < 10; i++) NextFrame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) NextFrame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RepeatedManualUpdateInSameFrameStillSteps()
	{
		var log = new List<int>();
		_runner.Start(Counter(log, 1, 10));

		_runner.Update(_dt);
		_runner.Update(_dt);

		Assert.Equal([1, 1], log);
	}

	public record struct TestEvent(int Value);

	[Fact, Trait(CATEGORY, UNIT)]
	public void EachCoroutineSeesAnEventOnceAndALaterWaitDoesNotSeeItAgain()
	{
		var events = _app.Services.GetRequiredService<IEvents>();
		var log = new List<string>();
		IEnumerator Routine(string name)
		{
			yield return Wait.For<TestEvent>();
			log.Add(name + " 1");
			yield return Wait.For<TestEvent>();
			log.Add(name + " 2");
		}

		_runner.Start(Routine("a"));
		_runner.Start(Routine("b"));
		NextFrame();

		events.Emit(new TestEvent(1));
		NextFrame();
		Assert.Equal(["a 1", "b 1"], log);

		// The event is still visible this frame, but each coroutine has already read it.
		NextFrame();
		NextFrame();
		Assert.Equal(["a 1", "b 1"], log);

		events.Emit(new TestEvent(2));
		NextFrame();
		Assert.Equal(["a 1", "b 1", "a 2", "b 2"], log);
		Assert.Equal(0, _runner.Count);
	}
}

public class CoroutineSystemTests
{
	private static IEnumerator Counter(List<int> log)
	{
		while (true)
		{
			log.Add(1);
			yield return null;
		}
	}

	private static IonApplication Build(Action<IIonApplication>? afterUse = null)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddCoroutines();
		var app = builder.Build();
		app.UseEvents();
		app.UseCoroutines();
		afterUse?.Invoke(app);
		return app;
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SystemStepsCoroutinesEveryFrame()
	{
		using var app = Build();
		var runner = app.Services.GetRequiredService<ICoroutineRunner>();
		var log = new List<int>();
		runner.Start(Counter(log));

		var loop = app.Build();
		var dt = new GameTime { Delta = 0.01f };
		loop.Init(dt);
		for (uint frame = 0; frame < 3; frame++)
		{
			dt.Frame = frame;
			loop.Step(dt);
		}

		Assert.Equal(3, log.Count);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ManualUpdateAfterSystemInSameFrameIsIgnored()
	{
		ICoroutineRunner runner = null!;
		using var app = Build(a => a.UseUpdate(next => dt =>
		{
			runner.Update(dt);
			next(dt);
		}));
		runner = app.Services.GetRequiredService<ICoroutineRunner>();
		var log = new List<int>();
		runner.Start(Counter(log));

		var loop = app.Build();
		var dt = new GameTime { Delta = 0.01f };
		for (uint frame = 0; frame < 3; frame++)
		{
			dt.Frame = frame;
			loop.Step(dt);
		}

		Assert.Equal(3, log.Count);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ManualUpdateBeforeSystemInSameFrameIsNotDoubled()
	{
		ICoroutineRunner runner = null!;
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddCoroutines();
		using var app = builder.Build();
		app.UseEvents();
		app.UseFirst(next => dt =>
		{
			runner.Update(dt);
			next(dt);
		});
		app.UseCoroutines();
		runner = app.Services.GetRequiredService<ICoroutineRunner>();
		var log = new List<int>();
		runner.Start(Counter(log));

		var loop = app.Build();
		var dt = new GameTime { Delta = 0.01f };
		for (uint frame = 0; frame < 3; frame++)
		{
			dt.Frame = frame;
			loop.Step(dt);
		}

		Assert.Equal(3, log.Count);
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void WaitForEventSeesAFixedUpdateEventOnceAt120FpsAnd60Hz(int emitOnFixedStep)
	{
		var clock = new ManualClock();
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddSingleton<IClock>(clock);
		builder.Services.Configure<GameConfig>(c => { c.MaxFPS = 120; c.FixedUpdateRate = 60; });
		builder.Services.AddCoroutines();
		using var app = builder.Build();
		app.UseEvents();
		app.UseCoroutines();

		var fixedSteps = 0;
		var emitter = app.Services.GetRequiredService<IEvents>();
		app.UseFixedUpdate(next => dt =>
		{
			if (++fixedSteps == emitOnFixedStep) emitter.Emit(new CoroutineRunnerTests.TestEvent(fixedSteps));
			next(dt);
		});

		var runner = app.Services.GetRequiredService<ICoroutineRunner>();
		var resumed = 0;
		var frames = 0;
		IEnumerator Routine()
		{
			while (true)
			{
				yield return Wait.For<CoroutineRunnerTests.TestEvent>();
				resumed++;
			}
		}
		IEnumerator EveryFrame()
		{
			while (true)
			{
				frames++;
				yield return null;
			}
		}
		runner.Start(Routine());
		runner.Start(EveryFrame());

		app.RunFrames(20);

		// Coroutines step once per frame in Update, whether or not the frame ran a fixed step, and see the event once.
		Assert.Equal(1, resumed);
		Assert.Equal(20, frames);
		Assert.InRange(fixedSteps, emitOnFixedStep, 10);
	}
}
