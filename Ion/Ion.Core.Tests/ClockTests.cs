using System.Diagnostics;

namespace Ion.Tests;

public class ClockTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void ManualClockOnlyMovesWhenAdvanced()
	{
		var clock = new ManualClock(TimeSpan.FromSeconds(1));

		Assert.Equal(TimeSpan.FromSeconds(1), clock.Elapsed);
		Assert.Equal(TimeSpan.FromSeconds(1), clock.NextFrame());
		Assert.Equal(1.0, ((IClock)clock).Seconds);

		clock.Advance(TimeSpan.FromMilliseconds(250));
		Assert.Equal(TimeSpan.FromMilliseconds(1250), clock.Elapsed);
		Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ManualClockRecordsSleepRequests()
	{
		var clock = new ManualClock();

		clock.Sleep(TimeSpan.Zero);
		clock.Sleep(TimeSpan.FromMilliseconds(-3));
		Assert.Equal(0, clock.SleepCount);

		clock.Sleep(TimeSpan.FromMilliseconds(5));
		clock.Sleep(TimeSpan.FromMilliseconds(2));

		Assert.Equal(2, clock.SleepCount);
		Assert.Equal(TimeSpan.FromMilliseconds(2), clock.LastSleep);
		Assert.Equal(TimeSpan.FromMilliseconds(7), clock.TotalSleep);
		Assert.Equal(TimeSpan.FromMilliseconds(7), clock.Elapsed);

		clock.AdvanceOnSleep = false;
		clock.Sleep(TimeSpan.FromMilliseconds(5));
		Assert.Equal(3, clock.SleepCount);
		Assert.Equal(TimeSpan.FromMilliseconds(7), clock.Elapsed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FixedStepClockAdvancesOneStepPerFrame()
	{
		var clock = new FixedStepClock(TimeSpan.FromMilliseconds(10));

		Assert.Equal(TimeSpan.FromMilliseconds(10), clock.Step);
		Assert.Equal(TimeSpan.Zero, clock.Elapsed);
		Assert.Equal(TimeSpan.FromMilliseconds(10), clock.NextFrame());
		Assert.Equal(TimeSpan.FromMilliseconds(10), clock.Elapsed);

		clock.Advance();
		clock.Sleep(TimeSpan.FromSeconds(10));
		Assert.Equal(TimeSpan.FromMilliseconds(20), clock.Elapsed);

		Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(TimeSpan.Zero));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopwatchClockSleepsAtLeastTheRequestedDuration()
	{
		var clock = new StopwatchClock();
		var before = clock.Elapsed;

		var sw = Stopwatch.StartNew();
		clock.Sleep(TimeSpan.FromMilliseconds(3));
		sw.Stop();

		Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(3), $"Slept only {sw.Elapsed.TotalMilliseconds} ms.");
		Assert.True(clock.NextFrame() > before);

		sw.Restart();
		clock.Sleep(TimeSpan.Zero);
		clock.Sleep(TimeSpan.FromMilliseconds(0.2)); // Shorter than the spin threshold: spins only.
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TimerResolutionScopeIsInactiveOffWindows()
	{
		using var scope = WindowsTimerResolution.Begin();

		Assert.Equal(OperatingSystem.IsWindows(), scope.IsActive);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BuilderRegistersTheStopwatchClockByDefault()
	{
		using var app = IonApplication.CreateBuilder().Build();

		Assert.IsType<StopwatchClock>(app.Services.GetRequiredService<IClock>());
	}
}
