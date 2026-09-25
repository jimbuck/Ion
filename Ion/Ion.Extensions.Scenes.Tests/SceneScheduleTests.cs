using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

public class SceneScheduleTests
{
	private static readonly GameTime Dt = new() { Frame = 0, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	private static IonApplication CreateApp(Action<IServiceCollection>? services = null)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<CallLog>();
		services?.Invoke(builder.Services);
		return builder.Build();
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SceneStepsFollowTheSameOrderingRulesAndRunAtTheSceneOrder()
	{
		using var game = CreateApp(s => s.AddScoped<SceneStepA>().AddScoped<SceneStepB>().AddScoped<SceneScope>());
		var log = game.Services.GetRequiredService<CallLog>();

		// Registered first, but order 0: runs after the scene (StageOrder.Scenes).
		game.Update(dt => log.Entries.Add("app"));
		game.Update(dt => log.Entries.Add("app-before-scene"), order: StageOrder.Scenes - 1);
		game.UseScene(1, scene => scene
			.UseSystem<SceneStepA>()
			.UseSystem<SceneStepB>()
			.UseSystem<SceneScope>()
			.Update((GameTime dt, CallLog l) => l.Entries.Add("scene-fn"), order: 20));
		game.UseEvents();

		var loop = game.Build();
		loop.Init(Dt);
		log.Entries.Clear();

		loop.Update(Dt);

		// In the scene: the scope opens first (order -10). A (order 10) must wait for B (order 30, [Before<SceneStepA>]), and
		// the function step (order 20) is ready before B, so it runs first: the lowest order among the steps that are free
		// to run goes next.
		Assert.Equal(["app-before-scene", "scope-begin", "scene-fn", "b", "a", "scope-end", "app"], log.Entries);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SceneSchedulesAreValidatedWhenTheApplicationIsBuilt()
	{
		using var game = CreateApp(s => s.AddScoped<BrokenSceneScope>());
		game.UseEvents();
		game.UseScene(7, scene => scene.UseSystem<BrokenSceneScope>());

		var ex = Assert.Throws<IonScheduleException>(() => game.Build());

		var diagnostic = Assert.Single(ex.Diagnostics);
		Assert.Equal(ScheduleDiagnosticCodes.UnpairedScope, diagnostic.Code);
		Assert.StartsWith("[Scene 7] ", diagnostic.Message);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ScopedSystemsAreAllowedInScenesButNotInTheRoot()
	{
		using var game = CreateApp(s => s.AddScoped<SceneStepA>());
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<SceneStepA>());

		var loop = game.Build();
		loop.Init(Dt);
		loop.Update(Dt);

		Assert.Equal(["a"], game.Services.GetRequiredService<CallLog>().Entries);

		game.UseSystem<SceneStepA>();
		var ex = Assert.Throws<IonScheduleException>(() => game.Build());
		Assert.Equal([ScheduleDiagnosticCodes.ScopedServiceInRoot], ex.Codes.ToArray());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void PrintScheduleIncludesEveryScene()
	{
		using var game = CreateApp(s => s.AddScoped<SceneStepA>().AddScoped<SceneStepB>());
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<SceneStepA>().UseSystem<SceneStepB>());
		game.UseScene(2, scene => { });

		var text = game.PrintSchedule();

		Assert.Contains("      -500  SceneSystem.Update\n", text);
		Assert.Contains("Schedule Scene 1 (run by SceneSystem)\n", text);
		Assert.Contains("  Update\n        30  SceneStepB.Step [before SceneStepA]\n        10  SceneStepA.Step\n", text);
		Assert.Contains("Schedule Scene 2 (run by SceneSystem)\n", text);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RegisteringOnASceneAfterItLoadedThrows()
	{
		ISceneBuilder? captured = null;
		using var game = CreateApp();
		game.UseEvents();
		game.UseScene(1, scene => captured = scene);

		var loop = game.Build();
		loop.Init(Dt);

		var ex = Assert.Throws<IonScheduleException>(() => captured!.Update(dt => { }));
		Assert.Equal([ScheduleDiagnosticCodes.UnreachableStep], ex.Codes.ToArray());
	}
}

public sealed class SceneStepA(CallLog log)
{
	[Update(Order = 10)] public void Step(GameTime dt) => log.Entries.Add("a");
}

public sealed class SceneStepB(CallLog log)
{
	[Update(Order = 30), Before<SceneStepA>] public void Step(GameTime dt) => log.Entries.Add("b");
}

public sealed class SceneScope(CallLog log)
{
	[Begin(Stage.Update, Order = -10)] public void Begin(GameTime dt) => log.Entries.Add("scope-begin");
	[End(Stage.Update, Order = -10)] public void End(GameTime dt) => log.Entries.Add("scope-end");
}

public sealed class BrokenSceneScope
{
	[End(Stage.Render)] public void Close(GameTime dt) { }
}
