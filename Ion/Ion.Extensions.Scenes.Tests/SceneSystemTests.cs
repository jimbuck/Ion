using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

public class SceneSystemTests
{
	private static GameTime NewGameTime() => new() { Frame = 0, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TwoApplicationsInOneProcessBothSwitchScenes()
	{
		var dt = NewGameTime();
		using var appA = TestUtils.SetupWithScenes(2, out var servicesA, out var gameA);
		using var appB = TestUtils.SetupWithScenes(2, out var servicesB, out var gameB);

		var loopA = gameA.Build();
		var loopB = gameB.Build();
		loopA.Init(dt);
		loopB.Init(dt);

		Assert.Equal(1, servicesA.GetRequiredService<ICurrentScene>().SceneId);
		Assert.Equal(1, servicesB.GetRequiredService<ICurrentScene>().SceneId);

		servicesA.GetRequiredService<IEvents>().EmitChangeScene(2);
		servicesB.GetRequiredService<IEvents>().EmitChangeScene(2);
		loopA.Step(dt);
		loopB.Step(dt);

		Assert.Equal(2, servicesA.GetRequiredService<ICurrentScene>().SceneId);
		Assert.Equal(2, servicesB.GetRequiredService<ICurrentScene>().SceneId);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SystemRegisteredAfterUseSceneRuns()
	{
		var dt = NewGameTime();
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<StageCounterSystem>();

		using var game = builder.Build();
		game.UseEvents();
		game.UseScene(1, _ => { });
		game.UseSystem<StageCounterSystem>();

		var counter = game.Services.GetRequiredService<StageCounterSystem>();
		var loop = game.Build();

		loop.Init(dt);
		loop.Step(dt);
		loop.Destroy(dt);

		Assert.Equal(1, counter.Init);
		Assert.Equal(1, counter.First);
		Assert.Equal(1, counter.FixedUpdate);
		Assert.Equal(1, counter.Update);
		Assert.Equal(1, counter.Render);
		Assert.Equal(1, counter.Last);
		Assert.Equal(1, counter.Destroy);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SceneSystemsRunBeforeSystemsRegisteredAfterUseScene()
	{
		var dt = NewGameTime();
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<CallLog>();
		builder.Services.AddScoped<SceneLoggingSystem>();
		builder.Services.AddSingleton<AppLoggingSystem>();

		using var game = builder.Build();
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<SceneLoggingSystem>());
		game.UseSystem<AppLoggingSystem>();

		var log = game.Services.GetRequiredService<CallLog>();
		var loop = game.Build();
		loop.Init(dt);
		log.Entries.Clear();

		loop.Step(dt);

		Assert.Equal(["scene", "app"], log.Entries);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void UnknownSceneIdKeepsCurrentScene()
	{
		var dt = NewGameTime();
		using var _ = TestUtils.SetupWithScenes(2, out var services, out var game);

		var eventEmitter = services.GetRequiredService<IEvents>();
		var currentScene = services.GetRequiredService<ICurrentScene>();
		var loop = game.Build();

		loop.Init(dt);
		Assert.Equal(1, currentScene.SceneId);

		eventEmitter.EmitChangeScene(99);
		var ex = Record.Exception(() => { loop.Step(dt); loop.Step(dt); });

		Assert.Null(ex);
		Assert.Equal(1, currentScene.SceneId);

		eventEmitter.EmitChangeScene(2);
		loop.Step(dt);
		Assert.Equal(2, currentScene.SceneId);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void DisposeRunsActiveSceneDestroyOnce()
	{
		var dt = NewGameTime();
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<CallLog>();
		builder.Services.AddScoped<SceneLoggingSystem>();

		var game = builder.Build();
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<SceneLoggingSystem>());

		var log = game.Services.GetRequiredService<CallLog>();
		var loop = game.Build();
		loop.Init(dt);
		loop.Step(dt);

		game.Dispose();

		Assert.Equal(1, log.Entries.Count(e => e == "scene-destroy"));
		Assert.Equal(1, log.Entries.Count(e => e == "scene-disposed"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void DisposeAfterDestroyStageDoesNotDestroyTwice()
	{
		var dt = NewGameTime();
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<CallLog>();
		builder.Services.AddScoped<SceneLoggingSystem>();

		var game = builder.Build();
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<SceneLoggingSystem>());

		var log = game.Services.GetRequiredService<CallLog>();
		var loop = game.Build();
		loop.Init(dt);
		loop.Step(dt);
		loop.Destroy(dt);

		game.Dispose();

		Assert.Equal(1, log.Entries.Count(e => e == "scene-destroy"));
		Assert.Equal(1, log.Entries.Count(e => e == "scene-disposed"));
	}
}

public class CallLog
{
	public List<string> Entries { get; } = [];
}

public class SceneLoggingSystem(CallLog log) : IDisposable
{
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		log.Entries.Add("scene");
		next(dt);
	}

	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate next)
	{
		log.Entries.Add("scene-destroy");
		next(dt);
	}

	public void Dispose() => log.Entries.Add("scene-disposed");
}

public class AppLoggingSystem(CallLog log)
{
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		log.Entries.Add("app");
		next(dt);
	}
}

public class StageCounterSystem
{
	public int Init, First, FixedUpdate, Update, Render, Last, Destroy;

	[Init] public void OnInit(GameTime dt, GameLoopDelegate next) { Init++; next(dt); }
	[First] public void OnFirst(GameTime dt, GameLoopDelegate next) { First++; next(dt); }
	[FixedUpdate] public void OnFixedUpdate(GameTime dt, GameLoopDelegate next) { FixedUpdate++; next(dt); }
	[Update] public void OnUpdate(GameTime dt, GameLoopDelegate next) { Update++; next(dt); }
	[Render] public void OnRender(GameTime dt, GameLoopDelegate next) { Render++; next(dt); }
	[Last] public void OnLast(GameTime dt, GameLoopDelegate next) { Last++; next(dt); }
	[Destroy] public void OnDestroy(GameTime dt, GameLoopDelegate next) { Destroy++; next(dt); }
}
