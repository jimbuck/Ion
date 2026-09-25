using Ion.Core;
using Ion.Extensions.Debug;

using Microsoft.Extensions.Configuration;

namespace Ion.Tests;

public class InjectionTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void EventEmitterInterfaceForwardsToTheEngineEmitterByDefault()
	{
		using var app = IonApplication.CreateBuilder().Build();

		Assert.Same(app.Services.GetRequiredService<EventEmitter>(), app.Services.GetRequiredService<IEventEmitter>());
		Assert.IsType<EventListener>(app.Services.GetRequiredService<IEventListener>());
		Assert.IsType<EventListener>(app.Services.GetRequiredService<IEventListenerFactory>().CreateListener());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AFakeEventEmitterCanBeInjected()
	{
		var fake = new FakeEventEmitter();
		using var host = new LoopTestHost(new ManualClock(), services: s => s.AddSingleton<IEventEmitter>(fake), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		host.Get<ExitAfterFramesSystem>().Frames = 2;

		// The engine's listeners and EventSystem keep working against the real emitter, so building and running succeed.
		host.BuildLoop().RunFrames(4);

		// Systems that depend on IEventEmitter got the fake: the exit request went to it, so the loop ran all 4 frames.
		Assert.Same(fake, host.Get<IEventEmitter>());
		Assert.Equal(1, fake.Emitted);
		Assert.Equal(4, host.Get<TestSystem>().UpdateCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ListenerRejectsAnEmitterThatIsNotTheEngines()
	{
		var ex = Assert.Throws<ArgumentException>(() => new EventListener((IEventEmitter)new FakeEventEmitter()));
		Assert.Equal("eventEmitter", ex.ParamName);

		// The compatibility overload still accepts the engine's emitter.
		using var listener = new EventListener((IEventEmitter)new EventEmitter());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AFakeTraceManagerCanBeInjected()
	{
		var fake = new FakeTraceManager();
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => c.MaxFPS = 100, services: s =>
		{
			s.AddDebugUtils(new ConfigurationBuilder().Build());
			s.AddSingleton<ITraceManager>(fake);
		}, use: app => app.UseDebugUtils(), systems: typeof(TestSystem));

		host.BuildLoop().RunFrames(3);

		// GameLoop's ITraceTimer<GameLoop> came from the fake, and recorded the idle time of every paced frame.
		Assert.Contains("GameLoop", fake.Prefixes);
		Assert.Equal(3, fake.Started.Count(n => n == "GameLoop::Idle"));
		Assert.Equal(3, host.Get<TestSystem>().UpdateCount);
#if DEBUG
		// In Debug builds the TraceTimerSystem wraps every stage with timers from the same fake.
		Assert.Contains("GameLoop::Update", fake.Started);
#endif
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheRealTraceManagerIsUsedByDefault()
	{
		using var host = new LoopTestHost(new ManualClock(), c => c.MaxFPS = 100, services: s => s.AddDebugUtils(new ConfigurationBuilder().Build(), d => d.TraceOutput = ""), use: app => app.UseDebugUtils());

		var traceManager = host.Get<ITraceManager>();
		traceManager.Start();
		host.BuildLoop().RunFrames(2);
		traceManager.Stop();
		traceManager.OutputTrace();
		traceManager.Clear();

		var timer = host.Get<ITraceTimer<GameLoop>>();
		var instance = timer.Start("Probe");
		instance.Then("Probe2");
		instance.Stop();
	}

	private sealed class FakeEventEmitter : IEventEmitter
	{
		public int Emitted { get; private set; }

		public void Emit<T>() where T : unmanaged => Emitted++;

		public void Emit<T>(T data) where T : unmanaged => Emitted++;
	}

	private sealed class FakeTraceManager : ITraceManager
	{
		public List<string> Prefixes { get; } = [];
		public List<string> Started { get; } = [];

		public bool IsEnabled { get; set; } = true;

		public void Start() => IsEnabled = true;
		public void Stop() => IsEnabled = false;
		public void Clear() => Started.Clear();
		public void OutputTrace() { }

		public ITraceTimer CreateTimer(string prefix)
		{
			Prefixes.Add(prefix);
			return new FakeTimer(this, prefix);
		}

		private sealed class FakeTimer(FakeTraceManager manager, string prefix) : ITraceTimer
		{
			public ITraceTimerInstance Start(string name)
			{
				manager.Started.Add(prefix + "::" + name);
				return new NullTimerInstance();
			}
		}
	}
}
