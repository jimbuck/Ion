using Ion.Core;

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
	public void TheLoopRecordsAStageSpanAndIdleTimeIntoAnInjectedProfiler()
	{
		var profiler = new FrameProfiler(historyFrames: 8, spansPerFrame: 64) { IsActive = true };
		using var host = new LoopTestHost(new ManualClock(), c => c.MaxFPS = 100, services: s => s.AddSingleton(profiler), systems: typeof(TestSystem));

		host.BuildLoop().RunFrames(3);

		// Init, three frames and Destroy.
		Assert.Equal(5, profiler.Count);
		var frame = profiler.GetFrame(1);
		Assert.Equal(FrameKind.Frame, frame.Kind);
		var names = frame.Spans.ToArray().Select(s => s.Id.Name).ToList();
		Assert.Contains("First", names);
		Assert.Contains("Update", names);
		Assert.Contains("Idle", names);
		Assert.Contains("EventSystem.StepEvents", names);
		Assert.Contains("EventSystem.Step", names);
		Assert.Equal(1, frame.Stats.FixedSteps);
		Assert.Equal(3, host.Get<TestSystem>().UpdateCount);
	}

	private sealed class FakeEvents : IEvents
	{
		public int Emitted { get; private set; }

		public void Emit<T>(in T e) where T : unmanaged => Emitted++;

		public EventReader<T> Reader<T>() where T : unmanaged => default;
	}
}
