using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Core;
using Ion.Extensions.Scenes;

using Xunit;
using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Breakout.ECS.Tests;

/// <summary>
/// End-to-end checks of the Ion schedule generator on the Breakout ECS sample (this project and the sample are compiled
/// with it): the generated schedule is the one that runs, it prints exactly like the reflection-bound runtime, and stack
/// traces through it show only user frames.
/// </summary>
public class GeneratedScheduleTests(ITestOutputHelper output)
{
	private static readonly string[] HeadlessArgs = ["--Ion:Headless=true"];

	private static IonApplication CreateGame()
	{
		var app = CreateBuilder().Build();
		BreakoutGame.Use(app);
		return app;
	}

	private static IonApplicationBuilder CreateBuilder()
	{
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		return BreakoutGame.Configure(builder);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheSampleRunsTheGeneratedSchedule()
	{
		// The same setup as Program.cs. Build() is intercepted here: the generator saw every registration made on app before
		// it (BreakoutGame.Use, and through the ScheduleRegistrations summaries, UseIon and the engine helpers it calls).
		using var app = CreateBuilder().Build();
		BreakoutGame.Use(app);
		var loop = app.Build();

		Assert.NotNull(loop.Schedule);
		Assert.True(loop.Schedule!.IsGenerated);

		loop.Initialize();
		for (var i = 0; i < 120; i++) loop.Step();
		loop.Shutdown();

		Assert.True(app.Services.GetRequiredService<ScoreSystem>() is not null);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void EveryRegistrationOfTheSampleIsPreBound()
	{
		using var app = CreateGame();

		Assert.All(app.Schedule.Entries, entry =>
		{
			Assert.NotNull(entry.Site);
			if (entry is SystemEntry system) Assert.NotNull(system.Generated);
		});
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void PrintScheduleIsIdenticalToTheReflectionBoundRuntime()
	{
		using var app = CreateGame();

		var generated = app.PrintSchedule();
		var reflection = ReflectionModel(app.Schedule).Plan(app.Services);

		output.WriteLine(generated);
		Assert.Equal(reflection.Print(), generated);
		Assert.Equal(reflection.Diagnostics, app.Schedule.Plan(app.Services).Diagnostics);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ARichScheduleMatchesTheReflectionBoundRuntime()
	{
		// Scopes, constraints, static and injected steps, inheritance, legacy middleware (in both forms and as a delegate),
		// function steps and a scene, all described by the generator, planned and printed like reflection does.
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.Services.AddScenes();
		builder.Services.AddSingleton<Probe>().AddSingleton<FrameScope>().AddSingleton<DerivedSystem>().AddSingleton<LegacySystem>().AddSingleton<InjectedSystem>();

		using var app = builder.Build();
		app.UseEvents()
			.UseSystem<DerivedSystem>()
			.UseSystem<FrameScope>()
			.UseSystem<LegacySystem>()
			.UseSystem<InjectedSystem>()
			.UseUpdate(next => dt => next(dt));
		app.Update((GameTime dt, Probe probe) => probe.Calls.Add("function"), order: 5);
#pragma warning disable ION012 // InjectedSystem is ordered after DerivedSystem, which is not in the scene (the runtime warns too).
		app.UseScene(1, scene => scene.UseSystem<InjectedSystem>().Render((GameTime dt, Probe probe) => probe.Calls.Add("scene render")));
#pragma warning restore ION012

		var reflection = ReflectionModel(app.Schedule);
		foreach (var nested in app.Schedule.Nested) reflection.AddNested(nested.Name, nested.Owner, nested.Plan);

		var generated = app.PrintSchedule();
		output.WriteLine(generated);
		Assert.Equal(reflection.Plan(app.Services).Print(), generated);

		var loop = app.Build();
		Assert.True(loop.Schedule!.IsGenerated);

		loop.Initialize();
		app.Services.GetRequiredService<IEventEmitter>().EmitChangeScene(1);
		loop.Step(new GameTime());
		loop.Step(new GameTime());
		loop.Shutdown();

		var probe = app.Services.GetRequiredService<Probe>();
		output.WriteLine(string.Join(", ", probe.Calls));
		Assert.Contains("scene render", probe.Calls);
		Assert.Contains("function", probe.Calls);

		// One frame: Update runs the legacy system around the rest of the stage, then Render opens the frame scope.
		var frame = probe.Calls.Skip(probe.Calls.IndexOf("legacy before")).Take(10).ToList();
		Assert.Equal(["legacy before", "derived update", "injected update", "function", "legacy after"], frame.Take(5));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AScopeEndRunsWhenAStepThrowsInTheGeneratedSchedule()
	{
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.Services.AddSingleton<Probe>().AddSingleton<FrameScope>().AddSingleton<ThrowingSystem>();

		using var app = builder.Build();
		app.UseSystem<FrameScope>().UseSystem<ThrowingSystem>();
		var loop = app.Build();
		Assert.True(loop.Schedule!.IsGenerated);

		Assert.Throws<InvalidOperationException>(() => loop.Step(new GameTime()));
		Assert.Equal(["frame begin", "frame end"], app.Services.GetRequiredService<Probe>().Calls);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void StackTracesShowOnlyUserFrames()
	{
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.Services.AddScenes();
		builder.Services.AddSingleton<Probe>().AddSingleton<FrameScope>().AddSingleton<ThrowingSystem>();

		using var app = builder.Build();
		app.UseEvents().UseSystem<FrameScope>().UseSystem<ThrowingSystem>();

		InvalidOperationException? exception = null;
		try
		{
			app.RunFrames(1);
		}
		catch (InvalidOperationException ex)
		{
			exception = ex;
		}

		Assert.NotNull(exception);
		var trace = exception.StackTrace!;
		output.WriteLine(exception.ToString());

		Assert.Contains(nameof(ThrowingSystem) + "." + nameof(ThrowingSystem.Render), trace, StringComparison.Ordinal);
		Assert.Contains(nameof(StackTracesShowOnlyUserFrames), trace, StringComparison.Ordinal);
		AssertOnlyUserFrames(trace);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void StackTracesThroughASceneShowOnlyUserFrames()
	{
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.Services.AddScenes();
		builder.Services.AddSingleton<Probe>().AddSingleton<FrameScope>().AddScoped<ThrowingSystem>();

		using var app = builder.Build();
		app.UseEvents().UseSystem<FrameScope>();
		app.UseScene(1, scene => scene.UseSystem<ThrowingSystem>());

		InvalidOperationException? exception = null;
		try
		{
			app.RunFrames(1);
		}
		catch (InvalidOperationException ex)
		{
			exception = ex;
		}

		Assert.NotNull(exception);
		var trace = exception.StackTrace!;
		output.WriteLine(exception.ToString());

		Assert.Contains(nameof(ThrowingSystem) + "." + nameof(ThrowingSystem.Render), trace, StringComparison.Ordinal);
		Assert.Contains(nameof(StackTracesThroughASceneShowOnlyUserFrames), trace, StringComparison.Ordinal);
		AssertOnlyUserFrames(trace);
	}

	private static void AssertOnlyUserFrames(string trace)
	{
		var frames = trace.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var frame in frames)
		{
			Assert.DoesNotContain("Ion.Generated", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("Ion.Schedule", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("StageRunner", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("SystemMiddlewareBinder", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("StepAdapters", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("Ion.Core.GameLoop", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("SceneSystem", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("lambda", frame, StringComparison.Ordinal);
			Assert.DoesNotContain("<", frame.Split(" in ")[0], StringComparison.Ordinal);
		}
	}

	/// <summary>The same registrations bound by reflection: what the runtime does without the generator.</summary>
	private static ScheduleModel ReflectionModel(ScheduleModel model)
	{
		var reflection = new ScheduleModel(model.Name, model.IsRoot);
		foreach (var entry in model.Entries)
		{
			switch (entry)
			{
				case SystemEntry system:
					reflection.AddSystem(system.ServiceType, system.ImplementationType);
					break;
				case FunctionEntry function:
					reflection.AddFunction(function.Stage, function.Function, function.ServiceTypes, function.Bind, function.Order);
					break;
				case MiddlewareEntry middleware:
					reflection.AddMiddleware(middleware.Stage, middleware.Middleware, middleware.Order);
					break;
			}
		}

		return reflection;
	}
}

public sealed class Probe
{
	public List<string> Calls { get; } = [];
}

public sealed class FrameScope(Probe probe)
{
	[Begin(Stage.Render, Order = StageOrder.Graphics)]
	public void BeginFrame(GameTime dt) => probe.Calls.Add("frame begin");

	[End(Stage.Render)]
	public void EndFrame() => probe.Calls.Add("frame end");
}

public abstract class BaseSystem(Probe probe)
{
	protected Probe Probe { get; } = probe;

	[Init]
	public void BaseInit(GameTime dt) => Probe.Calls.Add("base init");

	[Update]
	public virtual void OnUpdate(GameTime dt) => Probe.Calls.Add("base update");
}

[After<FrameScope>]
public sealed class DerivedSystem(Probe probe) : BaseSystem(probe)
{
	public override void OnUpdate(GameTime dt) => Probe.Calls.Add("derived update");

	[Render]
	public static void StaticRender(GameTime dt)
	{
	}
}

#pragma warning disable ION010 // The legacy middleware forms are exercised on purpose.
public sealed class LegacySystem(Probe probe)
{
	[Update(Order = -10)]
	public void Wrap(GameTime dt, GameLoopDelegate next)
	{
		probe.Calls.Add("legacy before");
		next(dt);
		probe.Calls.Add("legacy after");
	}

	[Last]
	public GameLoopDelegate Factory(GameLoopDelegate next) => dt => next(dt);
}
#pragma warning restore ION010

public sealed class InjectedSystem(Probe probe)
{
	[Update, After<DerivedSystem>]
	public void Update(GameTime dt, IEventListener events, ILoopContext context) => probe.Calls.Add("injected update");

	[First]
	public void NoArguments() => probe.Calls.Add("injected first");
}

public sealed class ThrowingSystem
{
	[Render]
	public void Render(GameTime dt) => throw new InvalidOperationException("Thrown by a user step.");
}
