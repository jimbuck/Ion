using System.Collections;

using Ion.Extensions.Coroutines;
using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

/// <summary>Coroutines started from a scene stop when it unloads; the application's keep running.</summary>
public class SceneCoroutineTests
{
	private static GameTime Frame(uint frame) => new() { Frame = frame, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	private static IonApplication Build(Action<IonApplicationBuilder>? register = null, Action<IonApplication>? use = null)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddCoroutines();
		builder.Services.AddSingleton<CallLog>();
		builder.Services.AddScoped<CoroutineStarter>();
		register?.Invoke(builder);
		var game = builder.Build();
		game.UseEvents();
		game.UseCoroutines();
		game.UseScene(1, scene => scene.UseSystem<CoroutineStarter>());
		game.UseScene(2, _ => { });
		use?.Invoke(game);
		return game;
	}

	internal static IEnumerator<Wait> Tick(CallLog log, string name)
	{
		while (true)
		{
			log.Entries.Add(name);
			yield return Wait.None;
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASceneSystemsCoroutineStopsWhenTheSceneUnloads()
	{
		using var game = Build(use: app => app.Init((GameTime dt, ICoroutineRunner runner, CallLog log) => runner.Start(Tick(log, "app"))));
		var log = game.Services.GetRequiredService<CallLog>();
		var runner = game.Services.GetRequiredService<CoroutineRunner>();
		var loop = game.Build();

		loop.Init(Frame(0));
		loop.Step(Frame(1));
		Assert.Equal(2, runner.Count);
		Assert.Contains("scene", log.Entries);

		game.Services.GetRequiredService<IEvents>().EmitChangeScene(2);
		loop.Step(Frame(2));
		log.Entries.Clear();
		loop.Step(Frame(3));

		Assert.Equal(1, runner.Count);
		Assert.Equal(["app"], log.Entries);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASceneSystemCanStartACoroutineThatOutlivesTheScene()
	{
		using var game = Build(builder => builder.Services.AddScoped<LongLivedStarter>(), app => app.UseScene(3, scene => scene.UseSystem<LongLivedStarter>()));
		var log = game.Services.GetRequiredService<CallLog>();
		var events = game.Services.GetRequiredService<IEvents>();
		var loop = game.Build();

		loop.Init(Frame(0));
		events.EmitChangeScene(3);
		loop.Step(Frame(1));
		events.EmitChangeScene(2);
		loop.Step(Frame(2));
		log.Entries.Clear();
		loop.Step(Frame(3));

		Assert.Equal(["long lived"], log.Entries);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheActiveScenesRunnerTiesACoroutineToTheSceneFromOutside()
	{
		using var game = Build();
		var log = game.Services.GetRequiredService<CallLog>();
		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var loop = game.Build();

		loop.Init(Frame(0));
		scenes.ActiveScene!.Services.GetRequiredService<ICoroutineRunner>().Start(Tick(log, "tied"));
		loop.Step(Frame(1));
		Assert.Contains("tied", log.Entries);

		game.Services.GetRequiredService<IEvents>().EmitChangeScene(2);
		loop.Step(Frame(2));
		log.Entries.Clear();
		loop.Step(Frame(3));

		Assert.DoesNotContain("tied", log.Entries);
	}
}

public sealed class CoroutineStarter(ICoroutineRunner runner, CallLog log)
{
	[Init]
	public void Init(GameTime dt) => runner.Start(SceneCoroutineTests.Tick(log, "scene"));
}

public sealed class LongLivedStarter(CoroutineRunner runner, CallLog log)
{
	[Init]
	public void Init(GameTime dt) => runner.Start(SceneCoroutineTests.Tick(log, "long lived"));
}
