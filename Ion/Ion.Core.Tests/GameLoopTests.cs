using Ion.Core;

namespace Ion.Tests;

public class GameLoopTests
{
	private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

	private const double FixedStep60 = 1.0 / 60.0;

	[Fact, Trait(CATEGORY, UNIT)]
	public void FiftyMillisecondFrameAt60HzRunsThreeFixedStepsAndCarriesTheRemainder()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		// A 20 ms frame runs one fixed step and carries 3.33 ms.
		clock.Advance(20 * Ms);
		loop.Step();
		Assert.Equal(1, system.FixedUpdateCount);
		Assert.Equal(0.02 - FixedStep60, loop.Accumulator, 6);

		// A 50 ms frame on top of that carry runs three fixed steps and still carries 3.33 ms.
		clock.Advance(50 * Ms);
		loop.Step();
		Assert.Equal(4, system.FixedUpdateCount);
		Assert.Equal(0.0033, loop.Accumulator, 4);
		Assert.Equal(0.05f, loop.GameTime.Delta, 6);
		Assert.Equal((float)FixedStep60, loop.FixedGameTime.Delta, 6);
		Assert.Equal(4 * FixedStep60, loop.FixedGameTime.Elapsed.TotalSeconds, 5);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ExactMultipleOfTheFixedStepCarriesNothing()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		clock.Advance(50 * Ms);
		loop.Step();

		Assert.Equal(3, system.FixedUpdateCount);
		Assert.Equal(0, loop.Accumulator, 9);
		Assert.Equal(0, loop.GameTime.Alpha, 6);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AlphaIsTheCarriedTimeOverTheFixedStep()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, systems: typeof(TestSystem));
		var loop = host.BuildLoop();

		clock.Advance(20 * Ms);
		loop.Step();

		// 3.33 ms carried of a 16.67 ms step.
		Assert.Equal(0.2f, loop.GameTime.Alpha, 4);
		Assert.Equal(1f, loop.FixedGameTime.Alpha);

		// Half a step: no fixed update, alpha grows.
		clock.Advance(TimeSpan.FromSeconds(FixedStep60 / 2));
		loop.Step();
		Assert.Equal(0.7f, loop.GameTime.Alpha, 3);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FixedStepFollowsFixedUpdateRateNotMaxFps()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => { c.FixedUpdateRate = 50; c.MaxFPS = 30; }, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		Assert.Equal(0.02f, loop.FixedGameTime.Delta, 6);

		clock.Advance(60 * Ms);
		loop.Step();

		Assert.Equal(0.02f, loop.FixedGameTime.Delta, 6);
		Assert.Equal(3, system.FixedUpdateCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NonPositiveConfigurationFallsBackToDefaults()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => { c.FixedUpdateRate = 0; c.MaxFrameTime = TimeSpan.Zero; }, systems: typeof(TestSystem));
		var loop = host.BuildLoop();

		clock.Advance(TimeSpan.FromSeconds(5));
		loop.Step();

		Assert.Equal((float)FixedStep60, loop.FixedGameTime.Delta, 6);
		Assert.Equal(0.1f, loop.GameTime.Delta, 6);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LongFramesAreClampedToTheDefaultMaxFrameTime()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		clock.Advance(TimeSpan.FromSeconds(2));
		loop.Step();

		Assert.Equal(0.1f, loop.GameTime.Delta, 6);
		Assert.Equal(6, system.FixedUpdateCount);
		Assert.Equal(TimeSpan.FromSeconds(2), loop.GameTime.Elapsed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MaxFrameTimeIsConfigurable()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => c.MaxFrameTime = TimeSpan.FromMilliseconds(250), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		clock.Advance(TimeSpan.FromSeconds(2));
		loop.Step();

		Assert.Equal(0.25f, loop.GameTime.Delta, 6);
		Assert.Equal(15, system.FixedUpdateCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MaxFpsAsksTheClockToSleepForTheRestOfTheFrame()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => c.MaxFPS = 100, use: app => app.UseRender(next => dt =>
		{
			clock.Advance(4 * Ms); // The frame's work takes 4 ms.
			next(dt);
		}));
		var loop = host.BuildLoop();

		clock.Advance(10 * Ms);
		loop.Step();

		Assert.Equal(1, clock.SleepCount);
		Assert.Equal(6 * Ms, clock.LastSleep);

		// The sleep brought the frame to exactly 10 ms, which is what the next frame measures.
		loop.Step();
		Assert.Equal(0.01f, loop.GameTime.Delta, 6);
		Assert.Equal(2, clock.SleepCount);
		Assert.Equal(12 * Ms, clock.TotalSleep);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OverBudgetFramesDoNotSleep()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => c.MaxFPS = 100, use: app => app.UseUpdate(next => dt =>
		{
			clock.Advance(15 * Ms);
			next(dt);
		}));
		var loop = host.BuildLoop();

		loop.Step();

		Assert.Equal(0, clock.SleepCount);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(0, false)]
	[InlineData(-5, false)]
	[InlineData(100, true)]
	public void UncappedOrVSyncFramesDoNotSleep(int maxFps, bool vsync)
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => { c.MaxFPS = maxFps; c.VSync = vsync; });
		var loop = host.BuildLoop();

		loop.RunFrames(5);

		Assert.Equal(0, clock.SleepCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunFramesRunsInitOnceTheFramesThenDestroyOnce()
	{
		var clock = new ManualClock();
		using var host = new LoopTestHost(clock, c => c.MaxFPS = 60, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		loop.RunFrames(10);

		Assert.Equal(1, system.InitializeCount);
		Assert.Equal(10, system.FirstCount);
		Assert.Equal(10, system.UpdateCount);
		Assert.Equal(10, system.RenderCount);
		Assert.Equal(10, system.LastCount);
		Assert.Equal(1, system.DestroyCount);
		Assert.Equal(10u, loop.GameTime.Frame);
		Assert.False(loop.IsRunning);

		// Paced at 60 FPS with a manual clock: every frame slept a full frame, and the fixed steps kept up.
		Assert.Equal(10, clock.SleepCount);
		Assert.InRange(system.FixedUpdateCount, 8, 10);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunFramesZeroOnlyRunsInitAndDestroy()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();

		host.BuildLoop().RunFrames(0);

		Assert.Equal(1, system.InitializeCount);
		Assert.Equal(0, system.UpdateCount);
		Assert.Equal(1, system.DestroyCount);
		Assert.Throws<ArgumentOutOfRangeException>(() => host.BuildLoop().RunFrames(-1));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunExitsOnExitGameEvent()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		var system = host.Get<TestSystem>();
		host.Get<ExitAfterFramesSystem>().Frames = 5;
		var loop = host.BuildLoop();

		loop.Run();

		Assert.Equal(1, system.InitializeCount);
		Assert.Equal(5, system.UpdateCount);
		Assert.Equal(1, system.DestroyCount);
		Assert.False(loop.IsRunning);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunFramesStopsEarlyOnExitGameEvent()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		var system = host.Get<TestSystem>();
		host.Get<ExitAfterFramesSystem>().Frames = 3;

		host.BuildLoop().RunFrames(10);

		Assert.Equal(3, system.UpdateCount);
		Assert.Equal(1, system.DestroyCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunStopsWhenTheTokenIsCancelled()
	{
		using var cts = new CancellationTokenSource();
		var frames = 0;
		using var host = new LoopTestHost(new FixedStepClock(TimeSpan.FromMilliseconds(16)), use: app => app.UseUpdate(next => dt =>
		{
			if (++frames == 4) cts.Cancel();
			next(dt);
		}), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();

		host.BuildLoop().Run(cts.Token);

		Assert.Equal(4, frames);
		Assert.Equal(1, system.DestroyCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopEndsTheLoopAfterTheCurrentFrame()
	{
		GameLoop? loop = null;
		using var host = new LoopTestHost(new ManualClock(), use: app => app.UseUpdate(next => dt =>
		{
			if (dt.Frame == 2) loop!.Stop();
			next(dt);
		}), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		loop = host.BuildLoop();

		loop.Run();

		Assert.Equal(3, system.RenderCount);
		Assert.Equal(1, system.DestroyCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RunCannotBeReentered()
	{
		GameLoop? loop = null;
		Exception? reentry = null;
		using var host = new LoopTestHost(new ManualClock(), use: app => app.UseInit(next => dt =>
		{
			reentry = Record.Exception(() => loop!.RunFrames(1));
			next(dt);
		}));
		loop = host.BuildLoop();

		loop.RunFrames(1);

		Assert.IsType<InvalidOperationException>(reentry);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FixedStepClockGivesEveryFrameTheSameDelta()
	{
		var clock = new FixedStepClock(TimeSpan.FromMilliseconds(20));
		using var host = new LoopTestHost(clock, c => c.FixedUpdateRate = 50, systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();

		loop.RunFrames(25);

		Assert.Equal(25, system.FixedUpdateCount);
		Assert.Equal(0.02f, loop.GameTime.Delta, 6);
		Assert.Equal(TimeSpan.FromMilliseconds(500), loop.GameTime.Elapsed);
		Assert.Equal(TimeSpan.FromMilliseconds(500), loop.FixedGameTime.Elapsed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RebuildRebuildsThePipelinesAtTheEndOfTheFrame()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();
		var update = loop.Update;

		loop.Rebuild = true;
		loop.Step();

		Assert.False(loop.Rebuild);
		Assert.NotSame(update, loop.Update);
		loop.Step();
		Assert.Equal(2, system.UpdateCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UntimedStepSkipsRenderOnceStopped()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();
		var loop = host.BuildLoop();
		var dt = new GameTime { Delta = 0.01f, Alpha = 1 };

		loop.Step(dt);
		loop.Stop();
		loop.Step(dt);

		Assert.Equal(2, system.UpdateCount);
		Assert.Equal(2, system.FixedUpdateCount);
		Assert.Equal(1, system.RenderCount);
		Assert.Equal(1, system.LastCount);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ApplicationRunFramesAndRunUseTheRegisteredClock()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();

		host.App.RunFrames(3);

		Assert.Equal(1, system.InitializeCount);
		Assert.Equal(3, system.UpdateCount);
		Assert.Equal(1, system.DestroyCount);

		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		host.App.Run(cancelled.Token);

		Assert.Equal(2, system.InitializeCount);
		Assert.Equal(3, system.UpdateCount);
		Assert.Equal(2, system.DestroyCount);
		Assert.Throws<ArgumentOutOfRangeException>(() => host.App.RunFrames(-1));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ApplicationRunExitsOnExitGameEvent()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: [typeof(TestSystem), typeof(ExitAfterFramesSystem)]);
		host.Get<ExitAfterFramesSystem>().Frames = 2;

		host.App.Run();

		Assert.Equal(2, host.Get<TestSystem>().UpdateCount);
	}
}

/// <summary>Emits <see cref="ExitGameEvent"/> during the Update stage of frame number <see cref="Frames"/> (1-based).</summary>
public sealed class ExitAfterFramesSystem(IEventEmitter events)
{
	private int _frames;

	public int Frames { get; set; } = 1;

	[Update]
	public void Update()
	{
		if (++_frames == Frames) events.Emit<ExitGameEvent>();
	}
}
