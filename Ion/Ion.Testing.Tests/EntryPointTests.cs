using Microsoft.Extensions.Logging;

using Ion.Core;
using Ion.Testing;
using Ion.Testing.TestProgram;

namespace Ion.Tests;

/// <summary>
/// The test host runs a game's own <c>Program.cs</c> (Ion.Testing.TestProgram, top-level statements compiled with the
/// schedule generator) and takes over the application at its <c>Run()</c> call.
/// </summary>
public class EntryPointTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RunsTheProgramHeadlessOnTheDeterministicClockWithItsGeneratedSchedule()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>();
		var ticks = host.Collect<Tick>();

		Assert.Equal(3, host.Step(3));

		Assert.True(typeof(Program).IsPublic, "The generator declares the top-level Program public.");
		Assert.True(host.Loop.Schedule!.IsGenerated, "The schedule the generator compiled for Program.cs is not in use.");
		Assert.Equal(3, host.Get<Counter>().Count);
		Assert.True(host.Application.Configuration.IsHeadless());
		Assert.IsType<FixedStepClock>(host.Get<IClock>());
		Assert.Equal([1, 2, 3], ticks.Select(t => t.Count));
		Assert.Equal([0u, 1u, 2u], ticks.Frames);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheProgramReadsTheHostsConfigurationAndArgumentsWhileItRegisters()
	{
		using (var host = new IonTestHost().UseEntryPoint<Program>("--Test:Speed=5"))
		{
			host.Step(2);
			Assert.Equal(10, host.Get<Counter>().Count);
		}

		using (var host = new IonTestHost().WithConfiguration("Test:Speed", "7").UseEntryPoint<Program>())
		{
			host.Step(1);
			Assert.Equal(7, host.Get<Counter>().Count);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheHostsServicesAndSystemsAreAddedAfterThePrograms()
	{
		using var host = new IonTestHost()
			.UseEntryPoint<Program>()
			.Configure(services => services.AddSingleton(new CounterSettings(3)))
			.WithSystem<Observer>();
		host.Step(2);

		// The host's settings replace the program's, and its system runs with the program's.
		Assert.Equal([3, 6], host.Get<Observer>().Seen);
		Assert.False(host.Loop.Schedule!.IsGenerated, "A system the program did not add runs on the runtime schedule.");
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RunEntryPointReturnsTheOutcome()
	{
		using var run = IonTestHost.RunEntryPoint<Program>(4, host => host.WithArgs("--Test:Speed=2"));

		Assert.Equal(4, run.Frames);
		Assert.Equal(8, run.Get<Counter>().Count);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void DisposingTheHostLetsTheProgramFinish()
	{
		Exits.Returned = false;
		var host = new IonTestHost().UseEntryPoint<Program>();
		host.Step(1);
		Assert.False(Exits.Returned);

		host.Dispose();
		Assert.True(Exits.Returned, "Run() returned and the program ran to its end.");
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AProgramThatThrowsBeforeRunFailsTheTest()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>("--throw-before-run");

		var exception = Assert.Throws<InvalidOperationException>(() => host.Step(1));
		Assert.Contains("threw before it called Run()", exception.Message, StringComparison.Ordinal);
		Assert.Equal("The program failed before Run().", exception.InnerException!.Message);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AProgramThatReturnsWithoutRunFailsTheTest()
	{
		using var host = new IonTestHost().UseEntryPoint<Program>("--return-before-run");

		var exception = Assert.Throws<InvalidOperationException>(() => host.Step(1));
		Assert.Contains("returned without calling Run()", exception.Message, StringComparison.Ordinal);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AProgramThatThrowsAfterRunFailsWhenTheHostIsDisposed()
	{
		var host = new IonTestHost().UseEntryPoint<Program>("--throw-after-run");
		host.Step(1);

		var exception = Assert.Throws<InvalidOperationException>(host.Dispose);
		Assert.Contains("threw after Run() returned", exception.Message, StringComparison.Ordinal);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnAssemblyWithoutAnEntryPointIsRejected()
	{
		var exception = Assert.Throws<ArgumentException>(() => new IonTestHost().UseEntryPoint<IonTestHost>());
		Assert.Contains("has no entry point", exception.Message, StringComparison.Ordinal);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public async Task HostsRunningInParallelEachGetTheirOwnApplication()
	{
		// The hook reaches only the program thread its host started, so parallel hosts do not see each other's settings.
		var counts = await Task.WhenAll(Enumerable.Range(1, 6).Select(speed => Task.Run(() =>
		{
			using var host = new IonTestHost().WithConfiguration("Test:Speed", speed.ToString(System.Globalization.CultureInfo.InvariantCulture)).UseEntryPoint<Program>();
			host.Step(2);
			return host.Get<Counter>().Count;
		})));

		Assert.Equal([2, 4, 6, 8, 10, 12], counts);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnApplicationCreatedOutsideTheEntryPointIsNotTakenOver()
	{
		// No hook installed here: CreateBuilder and Run behave as usual (RunFrames runs the loop).
		var builder = IonApplication.CreateBuilder(["--Ion:Headless=true"]);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.AddIon().AddSystem<Counter>().Services.AddSingleton(new CounterSettings(1));
		using var app = builder.Build();
		app.UseIon().UseSystem<Counter>();
		app.RunFrames(2);

		Assert.Equal(2, app.Services.GetRequiredService<Counter>().Count);
	}

	/// <summary>Records the count it sees after the counter ran.</summary>
	public sealed class Observer(Counter counter)
	{
		public List<int> Seen { get; } = [];

		[Update(Order = 1)]
		public void Observe(GameTime dt) => Seen.Add(counter.Count);
	}
}
