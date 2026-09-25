using Ion.Core;
using Ion.Extensions.Debug;

using Microsoft.Extensions.Configuration;

namespace Ion.Tests;

public class InjectionTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void EventsForwardToTheEngineBusByDefault()
	{
		using var app = IonApplication.CreateBuilder().Build();

		Assert.Same(app.Services.GetRequiredService<EventBus>(), app.Services.GetRequiredService<IEvents>());
		Assert.Same(app.Services.GetRequiredService<ILoopContext>(), app.Services.GetRequiredService<EventBus>().Loop);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UseEventBusInstallsACustomBus()
	{
		var builder = IonApplication.CreateBuilder();
		EventBus? created = null;
		builder.UseEventBus(loop => created = new EventBus(loop));
		using var app = builder.Build();

		var events = app.Services.GetRequiredService<IEvents>();
		Assert.Same(created, events);
		Assert.NotNull(created!.Loop);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AFakeEventBusCanBeInjected()
	{
		var fake = new FakeEvents();
		using var host = new LoopTestHost(new ManualClock(), services: s => s.AddSingleton<IEvents>(fake), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		host.Get<ExitAfterFramesSystem>().Frames = 2;

		// The loop and EventSystem keep working against the engine's bus, so building and running succeed.
		host.BuildLoop().RunFrames(4);

		// Systems that depend on IEvents got the fake: the exit request went to it, so the loop ran all 4 frames.
		Assert.Same(fake, host.Get<IEvents>());
		Assert.Equal(1, fake.Emitted);
		Assert.Equal(4, host.Get<TestSystem>().UpdateCount);
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

	private sealed class FakeEvents : IEvents
	{
		public int Emitted { get; private set; }

		public void Emit<T>(in T e) where T : unmanaged => Emitted++;

		public EventReader<T> Reader<T>() where T : unmanaged => default;
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
