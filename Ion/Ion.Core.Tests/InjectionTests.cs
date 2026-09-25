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

#pragma warning disable CS0618 // The obsolete trace timer adapters.
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheObsoleteTraceTimersStillRecord()
	{
		var output = Path.Combine(Path.GetTempPath(), $"ion-trace-{Guid.NewGuid():N}.json");
		using var host = new LoopTestHost(new ManualClock(), services: s => s.AddDebugUtils(new ConfigurationBuilder().Build(), d => d.TraceOutput = output), use: app => app.UseDebugUtils());

		var traceManager = host.Get<ITraceManager>();
		var profiler = host.Get<FrameProfiler>();
		var loop = host.BuildLoop();
		loop.Initialize();

		traceManager.Start();
		Assert.True(profiler.IsActive);

		profiler.BeginFrame(99);
		var instance = host.Get<ITraceTimer<GameLoop>>().Start("Probe");
		instance.Then("Probe2");
		instance.Stop();
		traceManager.CreateTimer("Custom").Start("Work").Stop();
		profiler.EndFrame();

		var names = profiler.GetFrame(0).Spans.ToArray().Select(s => s.Id.Name).ToList();
		Assert.Equal(["GameLoop::Probe", "GameLoop::Probe2", "Custom::Work"], names);

		try
		{
			traceManager.OutputTrace();
			Assert.True(File.Exists(output));
		}
		finally
		{
			File.Delete(output);
		}

		traceManager.Stop();
		Assert.False(profiler.IsActive);
		traceManager.Clear();
		Assert.Equal(0, profiler.Count);

		// Not recording: a shared instance, nothing recorded.
		profiler.BeginFrame(100);
		host.Get<ITraceTimer<GameLoop>>().Start("Idle").Stop();
		profiler.EndFrame();
		Assert.Equal(0, profiler.GetFrame(0).Spans.Length);
		loop.Shutdown();
	}
#pragma warning restore CS0618

	private sealed class FakeEvents : IEvents
	{
		public int Emitted { get; private set; }

		public void Emit<T>(in T e) where T : unmanaged => Emitted++;

		public EventReader<T> Reader<T>() where T : unmanaged => default;
	}
}
