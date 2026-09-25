using Ion.Core;

using Microsoft.Extensions.Logging;

namespace Ion.Tests;

public class ScheduleTests
{
	private static readonly GameTime Dt = new() { Frame = 0, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	private static LoopTestHost Host(Action<IServiceCollection>? services = null, Action<IIonApplication>? use = null, params Type[] systems) =>
		new(new ManualClock(), services: s => { s.AddSingleton<CallLog>(); services?.Invoke(s); }, use: use, systems: systems);

	private static List<string> RunUpdate(LoopTestHost host)
	{
		var loop = host.BuildLoop();
		var log = host.Get<CallLog>();
		log.Entries.Clear();
		loop.Update(Dt);
		return log.Entries;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StepsRunByOrderThenRegistrationThenDeclaration()
	{
		using var host = Host(systems: [typeof(OrderA), typeof(OrderB)]);

		Assert.Equal(["b-5", "a0-first", "a0-second", "b0", "a5"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BeforeAndAfterConstraintsTakePrecedenceOverOrder()
	{
		using var host = Host(systems: [typeof(ConstrainedC), typeof(ConstrainedD), typeof(ConstrainedE)]);

		// C (order -100) must follow D (order 100); E (order 0) must precede C.
		Assert.Equal(["e", "d", "c"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ClassLevelConstraintsApplyToEveryStepOfTheSystem()
	{
		using var host = Host(systems: [typeof(ClassConstrained), typeof(OrderB)]);

		// ClassConstrained is [After<OrderB>], so both of its steps follow both of OrderB's.
		Assert.Equal(["b-5", "b0", "class-1", "class-2"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ACycleIsReportedWithTheSystemsInvolved()
	{
		using var host = Host(systems: [typeof(CycleA), typeof(CycleB)]);

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		var diagnostic = Assert.Single(ex.Diagnostics);
		Assert.Equal(ScheduleDiagnosticCodes.OrderingCycle, diagnostic.Code);
		Assert.Contains("CycleA.Step", diagnostic.Message);
		Assert.Contains("CycleB.Step", diagnostic.Message);
		Assert.Contains("Update", diagnostic.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScopesNestAroundLaterStepsAndEndInReverseOrder()
	{
		using var host = Host(systems: [typeof(ScopedStep), typeof(InnerScope), typeof(OuterScope)]);

		Assert.Equal(["before", "outer-begin", "inner-begin", "step", "inner-end", "outer-end"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AScopeEndRunsWhenAStepThrows()
	{
		using var host = Host(systems: [typeof(ScopedStep), typeof(InnerScope), typeof(OuterScope)]);
		var loop = host.BuildLoop();
		var log = host.Get<CallLog>();
		host.Get<ScopedStep>().Throw = true;
		log.Entries.Clear();

		var ex = Assert.Throws<InvalidOperationException>(() => loop.Update(Dt));

		Assert.Equal("step failed", ex.Message);
		Assert.Equal(["before", "outer-begin", "inner-begin", "inner-end", "outer-end"], log.Entries);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NamedScopesPairWithinOneSystem()
	{
		using var host = Host(systems: [typeof(NamedScopes), typeof(ScopedStep)]);

		Assert.Equal(["before", "a-begin", "b-begin", "step", "b-end", "a-end"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScopeErrorsAreReported()
	{
		using (var host = Host(systems: typeof(BeginWithoutEnd)))
		{
			var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());
			Assert.Equal([ScheduleDiagnosticCodes.UnpairedScope], ex.Codes.ToArray());
			Assert.Contains("Open", ex.Message);
		}

		using (var host = Host(systems: typeof(TwoUnnamedScopes)))
		{
			var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());
			Assert.Equal([ScheduleDiagnosticCodes.AmbiguousScope], ex.Codes.ToArray());
		}

		using (var host = Host(systems: typeof(MismatchedScopeOrder)))
		{
			var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());
			Assert.Equal([ScheduleDiagnosticCodes.AmbiguousScope], ex.Codes.ToArray());
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void InvalidStepsAreReportedTogether()
	{
		using var host = Host(systems: [typeof(AsyncSteps), typeof(HiddenStep), typeof(CustomStageSystem)]);

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		Assert.Equal(
			[ScheduleDiagnosticCodes.AsyncStep, ScheduleDiagnosticCodes.AsyncStep, ScheduleDiagnosticCodes.UnreachableStep, ScheduleDiagnosticCodes.UnknownStage],
			ex.Codes.ToArray());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScopedServicesAreRejectedInTheRootSchedule()
	{
		using var host = new LoopTestHost(new ManualClock(), services: s =>
		{
			s.AddScoped<CallLog>();
			s.AddScoped<ScopedStep>();
		}, use: app => app.UseSystem<ScopedStep>().UseSystem<InjectedSteps>());

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		// The scoped system; then InjectedSteps, which is not registered at all, and the scoped CallLog each of its three
		// steps injects.
		Assert.Equal(
			[ScheduleDiagnosticCodes.ScopedServiceInRoot, ScheduleDiagnosticCodes.UnregisteredSystem, ScheduleDiagnosticCodes.ScopedServiceInRoot, ScheduleDiagnosticCodes.ScopedServiceInRoot, ScheduleDiagnosticCodes.ScopedServiceInRoot],
			ex.Codes.ToArray());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UnregisteredParametersAreReported()
	{
		using var host = new LoopTestHost(new ManualClock(), services: s => s.AddSingleton<InjectedSteps>(), use: app => app.UseSystem<InjectedSteps>());

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		Assert.All(ex.Diagnostics, d => Assert.Equal(ScheduleDiagnosticCodes.UnresolvableParameter, d.Code));
		Assert.Contains("CallLog", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StepMethodsCanInjectServicesAndBeStatic()
	{
		using var host = Host(systems: typeof(InjectedSteps));
		var loop = host.BuildLoop();
		var log = host.Get<CallLog>();

		loop.Init(Dt);
		loop.Update(Dt);

		Assert.Equal(["init-injected", "static", "update-injected:0.01"], log.Entries);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FunctionStepsRunInOrderWithInjectedServices()
	{
		var calls = new List<string>();
		using var host = Host(systems: typeof(OrderB), use: app => app
			.Update(dt => calls.Add("plain"), order: 100)
			.Update((GameTime dt, CallLog log) => log.Entries.Add("with-log"))
			.Update((GameTime dt, CallLog log, IEvents events) => log.Entries.Add("two-services"), order: -10, name: "Named")
			// A method group with services needs its service types spelled out (C# does not infer them from a method group).
			.Update<CallLog>(StaticSteps.Tick));

		var entries = RunUpdate(host);

		// Order 0 ties go by registration: the function steps were registered before OrderB.
		Assert.Equal(["two-services", "b-5", "with-log", "static-tick", "b0"], entries);
		Assert.Equal(["plain"], calls);

		var print = host.App.PrintSchedule();
		Assert.Contains("Named (function)", print);
		Assert.Contains("StaticSteps.Tick (function)", print);
		Assert.Contains("ScheduleTests.lambda() (function)", print);
		Assert.Contains("ScheduleTests.lambda(CallLog) (function)", print);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LegacyMiddlewareStillWorksAndIsReported()
	{
		var logger = new CapturingLoggerProvider();
		using var host = Host(services: s => s.AddLogging(l => l.AddProvider(logger)), use: app =>
		{
			app.UseUpdate(next => dt =>
			{
				app.Services.GetRequiredService<CallLog>().Entries.Add("delegate-before");
				next(dt);
				app.Services.GetRequiredService<CallLog>().Entries.Add("delegate-after");
			});
		}, systems: [typeof(LegacyWrapper), typeof(OrderB)]);

		var entries = RunUpdate(host);

		// The legacy method (order 0, registered after the delegate) is nested in the delegate; both wrap OrderB's order 0 step.
		Assert.Equal(["b-5", "delegate-before", "legacy-before", "b0", "legacy-after", "delegate-after"], entries);
		Assert.Equal(2, logger.Messages.Count(m => m.Contains(ScheduleDiagnosticCodes.LegacyMiddleware)));
		Assert.Contains(logger.Messages, m => m.Contains("LegacyWrapper.Update") && m.Contains("[Update] public void Update(GameTime dt)"));

		var print = host.App.PrintSchedule();
		Assert.Contains("LegacyWrapper.Update (middleware) {", print);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WarningsAreLoggedOnceAndKeptOnThePlan()
	{
		var logger = new CapturingLoggerProvider();
		using var host = Host(services: s => s.AddLogging(l => l.AddProvider(logger)), systems: [typeof(NoSteps), typeof(ConstrainedC)]);

		var loop = host.BuildLoop();
		loop.Build();

		var plan = loop.Schedule!.Plan;
		Assert.Equal([ScheduleDiagnosticCodes.SystemWithoutSteps, ScheduleDiagnosticCodes.UnmatchedConstraint], plan.Diagnostics.Select(d => d.Code).ToArray());
		Assert.Equal(2, logger.Messages.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RegistrationOrderDoesNotMatterAcrossOrderBands()
	{
		// The user system is registered before the "engine" systems, yet runs between their setup and teardown steps.
		using var host = Host(use: app => app.UseSystem<ScopedStep>().UseSystem<EngineLikeSetup>().UseSystem<EngineLikeTeardown>(),
			services: s => s.AddSingleton<ScopedStep>().AddSingleton<EngineLikeSetup>().AddSingleton<EngineLikeTeardown>());

		Assert.Equal(["engine-scope-begin", "engine-setup", "before", "step", "engine-teardown", "engine-scope-end"], RunUpdate(host));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LeafStepsAreBoundAsDirectDelegatesWithoutClosures()
	{
		using var host = Host(systems: typeof(ConstrainedD));
		var loop = host.BuildLoop();

		// A stage with a single step runs that step's delegate itself: the method bound to the system instance.
		Assert.Same(host.Get<ConstrainedD>(), loop.Update.Target);
		Assert.Equal(nameof(ConstrainedD.Step), loop.Update.Method.Name);

		// Function steps given as Action<GameTime> are rebound to the same method and target (no extra hop).
		var calls = 0;
		Action<GameTime> action = dt => calls++;
		var direct = ScheduleModel.AsGameLoopDelegate(action);
		Assert.Same(action.Method, direct.Method);
		Assert.Same(action.Target, direct.Target);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StageMatchesGameLoopStage()
	{
		foreach (var stage in Enum.GetValues<Stage>())
		{
			Assert.Equal(stage.ToString(), ((GameLoopStage)stage).ToString());
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RebuildingTheLoopRebindsTheSchedule()
	{
		using var host = Host(systems: typeof(OrderB));
		var loop = host.BuildLoop();
		var first = loop.Schedule;

		loop.Build();

		Assert.NotSame(first, loop.Schedule);
		Assert.Same(loop.Schedule!.Update, loop.Update);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PrintScheduleShowsStagesOrdersScopesAndConstraints()
	{
		using var host = Host(systems: [typeof(ScopedStep), typeof(InnerScope), typeof(OuterScope), typeof(ConstrainedC), typeof(ConstrainedD), typeof(ConstrainedE)], use: app => app.Last(dt => { }, order: 10, name: "LastFunction"));

		var expected = """
			Schedule root
			  Init
			    (empty)
			  First
			    (empty)
			  FixedUpdate
			    (empty)
			  Update
			      -100  ScopedStep.Before
			       -50  OuterScope.Begin {
			       -10    InnerScope.Begin {
			         0      ScopedStep.Step
			         0      ConstrainedE.Step [before ConstrainedC]
			       100      ConstrainedD.Step
			      -100      ConstrainedC.Step [after ConstrainedD]
			       -10    } InnerScope.End
			       -50  } OuterScope.End
			  Render
			    (empty)
			  Last
			        10  LastFunction (function)
			      1000  EventSystem.StepEvents
			  Destroy
			    (empty)

			""".Replace("\r\n", "\n");

		Assert.Equal(expected, host.App.PrintSchedule());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ThePlanDescribesEveryItemForTheGenerator()
	{
		using var host = Host(systems: [typeof(OuterScope), typeof(ScopedStep)]);

		var plan = host.App.Schedule.Plan(host.Services);
		var update = plan[Stage.Update].Steps;

		Assert.Equal([StepKind.Step, StepKind.Scope, StepKind.Step], update.Select(s => s.Kind).ToArray());
		var scope = update[1];
		Assert.Equal(typeof(OuterScope), scope.System!.ImplementationType);
		Assert.Equal(nameof(OuterScope.Begin), scope.Method!.Name);
		Assert.Equal(nameof(OuterScope.End), scope.EndMethod!.Name);
		Assert.Equal(-50, scope.Order);
		Assert.Equal([0, 0, 1], update.Select(s => s.Depth).ToArray());
		Assert.True(plan.IsRoot);
	}
}

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public class ConsoleCollection;

[Collection(nameof(ConsoleCollection))]
public class PrintScheduleOptionTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void PrintScheduleOptionPrintsAtBuildAndContinues()
	{
		var builder = IonApplication.CreateBuilder(["--Ion:PrintSchedule=true"]);
		builder.Services.AddLogging(l => l.ClearProviders());
		builder.Services.AddSingleton<CallLog>().AddSingleton<OrderB>();
		using var app = builder.Build();
		app.UseEvents().UseSystem<OrderB>();

		var original = Console.Out;
		var output = new StringWriter();
		Console.SetOut(output);
		try
		{
			app.RunFrames(1);
		}
		finally
		{
			Console.SetOut(original);
		}

		var text = output.ToString();
		Assert.StartsWith("Schedule root\n", text);
		Assert.Contains("  Update\n        -5  OrderB.Early\n         0  OrderB.Default\n", text);
		Assert.Equal(["b-5", "b0"], app.Services.GetRequiredService<CallLog>().Entries);
	}
}

public class CallLog
{
	public List<string> Entries { get; } = [];
}

public sealed class OrderA(CallLog log)
{
	[Update(Order = 5)] public void Late(GameTime dt) => log.Entries.Add("a5");
	[Update] public void First(GameTime dt) => log.Entries.Add("a0-first");
	[Update] public void Second(GameTime dt) => log.Entries.Add("a0-second");
}

public sealed class OrderB(CallLog log)
{
	[Update] public void Default(GameTime dt) => log.Entries.Add("b0");
	[Update(Order = -5)] public void Early(GameTime dt) => log.Entries.Add("b-5");
}

public sealed class ConstrainedC(CallLog log)
{
	[Update(Order = -100), After<ConstrainedD>] public void Step(GameTime dt) => log.Entries.Add("c");
}

public sealed class ConstrainedD(CallLog log)
{
	[Update(Order = 100)] public void Step(GameTime dt) => log.Entries.Add("d");
}

public sealed class ConstrainedE(CallLog log)
{
	[Update, Before<ConstrainedC>] public void Step(GameTime dt) => log.Entries.Add("e");
}

[After<OrderB>]
public sealed class ClassConstrained(CallLog log)
{
	[Update(Order = -50)] public void One(GameTime dt) => log.Entries.Add("class-1");
	[Update(Order = -40)] public void Two(GameTime dt) => log.Entries.Add("class-2");
}

public sealed class CycleA
{
	[Update, After<CycleB>] public void Step(GameTime dt) { }
}

public sealed class CycleB
{
	[Update, After<CycleA>] public void Step(GameTime dt) { }
}

public sealed class ScopedStep(CallLog log)
{
	public bool Throw { get; set; }

	[Update(Order = -100)] public void Before(GameTime dt) => log.Entries.Add("before");

	[Update]
	public void Step(GameTime dt)
	{
		if (Throw) throw new InvalidOperationException("step failed");
		log.Entries.Add("step");
	}
}

public sealed class OuterScope(CallLog log)
{
	[Begin(Stage.Update, Order = -50)] public void Begin(GameTime dt) => log.Entries.Add("outer-begin");
	[End(Stage.Update, Order = -50)] public void End(GameTime dt) => log.Entries.Add("outer-end");
}

public sealed class InnerScope(CallLog log)
{
	[Begin(Stage.Update, Order = -10)] public void Begin(GameTime dt) => log.Entries.Add("inner-begin");
	[End(Stage.Update)] public void End() => log.Entries.Add("inner-end");
}

public sealed class NamedScopes(CallLog log)
{
	[Begin(Stage.Update, Order = -20, ScopeName = "b")] public void BeginB(GameTime dt) => log.Entries.Add("b-begin");
	[End(Stage.Update, ScopeName = "b")] public void EndB(GameTime dt) => log.Entries.Add("b-end");
	[Begin(Stage.Update, Order = -30, ScopeName = "a")] public void BeginA(GameTime dt) => log.Entries.Add("a-begin");
	[End(Stage.Update, ScopeName = "a")] public void EndA(GameTime dt) => log.Entries.Add("a-end");
}

public sealed class BeginWithoutEnd
{
	[Begin(Stage.Render)] public void Open(GameTime dt) { }
}

public sealed class TwoUnnamedScopes
{
	[Begin(Stage.Render)] public void OpenA(GameTime dt) { }
	[Begin(Stage.Render)] public void OpenB(GameTime dt) { }
	[End(Stage.Render)] public void Close(GameTime dt) { }
}

public sealed class MismatchedScopeOrder
{
	[Begin(Stage.Render, Order = -10)] public void Open(GameTime dt) { }
	[End(Stage.Render, Order = 10)] public void Close(GameTime dt) { }
}

public sealed class AsyncSteps
{
	[Init] public Task LoadAsync() => Task.CompletedTask;
	[Update] public async void Tick(GameTime dt) => await Task.Yield();
}

public sealed class HiddenStep
{
	[Update] internal void Hidden(GameTime dt) { }
	[Render] public void Visible(GameTime dt) { }
}

public sealed class PostUpdateAttribute : StageAttribute
{
	public override Stage Stage => (Stage)42;
}

public sealed class CustomStageSystem
{
	[PostUpdate] public void Step(GameTime dt) { }
}

public sealed class InjectedSteps
{
	[Init] public void Load(CallLog log) => log.Entries.Add("init-injected");
	[Update] public void Tick(GameTime dt, CallLog log) => log.Entries.Add("update-injected:" + dt.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture));
	[Update(Order = -1)] public static void StaticTick(CallLog log) => log.Entries.Add("static");
}

public static class StaticSteps
{
	public static void Tick(GameTime dt, CallLog log) => log.Entries.Add("static-tick");
}

public sealed class LegacyWrapper(CallLog log)
{
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		log.Entries.Add("legacy-before");
		next(dt);
		log.Entries.Add("legacy-after");
	}
}

public sealed class NoSteps
{
	public void NotAStep(GameTime dt) { }
}

public sealed class EngineLikeSetup(CallLog log)
{
	[Begin(Stage.Update, Order = StageOrder.EngineSetupFirst)] public void Begin(GameTime dt) => log.Entries.Add("engine-scope-begin");
	[End(Stage.Update)] public void End(GameTime dt) => log.Entries.Add("engine-scope-end");
	[Update(Order = StageOrder.EngineSetupLast)] public void Setup(GameTime dt) => log.Entries.Add("engine-setup");
}

public sealed class EngineLikeTeardown(CallLog log)
{
	[Update(Order = StageOrder.EngineTeardownFirst)] public void Teardown(GameTime dt) => log.Entries.Add("engine-teardown");
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
	public List<string> Messages { get; } = [];

	public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

	public void Dispose() { }

	private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (category != "Ion.Schedule") return;
			lock (provider.Messages) provider.Messages.Add(formatter(state, exception));
		}
	}
}
