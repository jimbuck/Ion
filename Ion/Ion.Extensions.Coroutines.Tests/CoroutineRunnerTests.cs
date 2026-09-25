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
		var emitter = _app.Services.GetRequiredService<IEventEmitter>();
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
	public void RepeatedManualUpdateInSameFrameStillSteps()
	{
		var log = new List<int>();
		_runner.Start(Counter(log, 1, 10));

		_runner.Update(_dt);
		_runner.Update(_dt);

		Assert.Equal([1, 1], log);
	}

	public record struct TestEvent(int Value);

	private sealed class CountingListenerFactory(IEventEmitter emitter) : IEventListenerFactory
	{
		private readonly List<CountingListener> _created = [];

		public int Created => _created.Count;
		public int Disposed => _created.Count(l => l.IsDisposed);

		public IEventListener CreateListener()
		{
			var listener = new CountingListener(new EventListener(emitter));
			_created.Add(listener);
			return listener;
		}
	}

	private sealed class CountingListener(IEventListener inner) : IEventListener
	{
		public bool IsDisposed { get; private set; }

		public bool On<T>() where T : unmanaged => inner.On<T>();
		public bool On<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEvent<T>? data) where T : unmanaged => inner.On(out data);
		public bool OnLatest<T>() where T : unmanaged => inner.OnLatest<T>();
		public bool OnLatest<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEvent<T>? data) where T : unmanaged => inner.OnLatest(out data);
		public void Emit<T>() where T : unmanaged => inner.Emit<T>();
		public void Emit<T>(T data) where T : unmanaged => inner.Emit(data);

		public void Dispose()
		{
			IsDisposed = true;
			inner.Dispose();
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EventListenerFactoryIsRegisteredAndCreatesDistinctListeners()
	{
		var factory = _app.Services.GetRequiredService<IEventListenerFactory>();

		using var a = factory.CreateListener();
		using var b = factory.CreateListener();

		Assert.NotSame(a, b);
		Assert.NotNull(_app.Services.GetRequiredService<IEventListener>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ListenersAreReleasedWhenCoroutinesFinishOrStop()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddCoroutines();
		builder.Services.AddSingleton<CountingListenerFactory>();
		builder.Services.AddSingleton<IEventListenerFactory>(sp => sp.GetRequiredService<CountingListenerFactory>());
		using var app = builder.Build();
		var factory = app.Services.GetRequiredService<CountingListenerFactory>();
		var runner = app.Services.GetRequiredService<ICoroutineRunner>();
		var dt = new GameTime { Delta = 0.1f };

		var finishes = Counter([], 1, 1);
		var stopped = Counter([], 2, 10);
		var remaining = Counter([], 3, 10);
		runner.Start(finishes);
		runner.Start(stopped);
		runner.Start(remaining);
		Assert.Equal(3, factory.Created);

		runner.Stop(stopped);
		Assert.Equal(1, factory.Disposed);

		dt.Frame = 1;
		runner.Update(dt);
		dt.Frame = 2;
		runner.Update(dt);
		Assert.False(runner.IsActive(finishes));
		Assert.Equal(2, factory.Disposed);

		((IDisposable)runner).Dispose();
		Assert.Equal(3, factory.Disposed);
		Assert.Equal(0, runner.Count);
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
}
