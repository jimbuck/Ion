using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

/// <summary>Any scene id loads, including 0 and negative ids: "no scene" is its own state, not a sentinel id.</summary>
public class SceneIdTests
{
	private static GameTime NewGameTime() => new() { Frame = 0, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	private enum ZeroBased
	{
		Title,
		Level,
	}

	private static IonApplication Build(Action<IonApplication> scenes)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<CallLog>();
		var game = builder.Build();
		game.UseEvents();
		scenes(game);
		return game;
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void BeforeAnySceneLoadsThereIsNoScene()
	{
		using var game = Build(g => g.UseScene(0, _ => { }));
		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var current = game.Services.GetRequiredService<ICurrentScene>();

		Assert.False(scenes.HasScene);
		Assert.Null(scenes.ActiveScene);
		Assert.False(current.HasScene);
		Assert.True(current.IsRoot);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SceneZeroLoadsFirstAndRuns()
	{
		var dt = NewGameTime();
		using var game = Build(g => g.UseScene(0, scene => scene.Update((GameTime _, CallLog log) => log.Entries.Add("zero"))));
		var loop = game.Build();

		loop.Init(dt);
		loop.Step(dt);

		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var current = game.Services.GetRequiredService<ICurrentScene>();
		Assert.True(scenes.HasScene);
		Assert.Equal(0, scenes.CurrentSceneId);
		Assert.NotNull(scenes.ActiveScene);
		Assert.True(current.HasScene);
		Assert.False(current.IsRoot);
		Assert.Equal(0, current.SceneId);
		Assert.Equal(["zero"], game.Services.GetRequiredService<CallLog>().Entries);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ChangingToAndFromSceneZeroWorks()
	{
		var dt = NewGameTime();
		using var game = Build(g => g.UseScene(ZeroBased.Title, _ => { }).UseScene(ZeroBased.Level, _ => { }));
		var events = game.Services.GetRequiredService<IEvents>();
		var current = game.Services.GetRequiredService<ICurrentScene>();
		var loop = game.Build();

		loop.Init(dt);
		Assert.Equal((int)ZeroBased.Title, current.SceneId);
		Assert.True(current.HasScene);

		events.EmitChangeScene(ZeroBased.Level);
		loop.Step(dt);
		Assert.Equal((int)ZeroBased.Level, current.SceneId);

		events.EmitChangeScene(ZeroBased.Title);
		loop.Step(dt);
		Assert.Equal((int)ZeroBased.Title, current.SceneId);
		Assert.True(current.HasScene);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void NegativeSceneIdsLoad()
	{
		var dt = NewGameTime();
		using var game = Build(g => g.UseScene(-3, _ => { }).UseScene(5, _ => { }));
		var events = game.Services.GetRequiredService<IEvents>();
		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var loop = game.Build();

		loop.Init(dt);
		Assert.Equal(-3, scenes.CurrentSceneId);

		events.EmitChangeScene(5);
		loop.Step(dt);
		Assert.Equal(5, scenes.CurrentSceneId);

		events.EmitChangeScene(-3);
		loop.Step(dt);
		Assert.Equal(-3, scenes.CurrentSceneId);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RequestingTheActiveSceneZeroIsNotAReload()
	{
		var dt = NewGameTime();
		using var game = Build(g => g.UseScene(0, scene => scene.Init((GameTime _, CallLog log) => log.Entries.Add("init"))));
		var events = game.Services.GetRequiredService<IEvents>();
		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var loop = game.Build();

		loop.Init(dt);
		events.EmitChangeScene(0);
		loop.Step(dt);
		loop.Step(dt);

		Assert.False(scenes.IsLoading);
		Assert.Equal(["init"], game.Services.GetRequiredService<CallLog>().Entries);
	}
}
