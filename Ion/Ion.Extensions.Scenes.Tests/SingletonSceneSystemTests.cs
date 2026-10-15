using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

/// <summary>
/// A system used by a scene is created from the scene's scope whatever its registration lifetime, so a system registered
/// as a singleton (for example with <c>builder.AddSystem&lt;T&gt;()</c>) sees the scene's scoped services, not the root's.
/// </summary>
public class SingletonSceneSystemTests
{
	private static GameTime NewGameTime() => new() { Frame = 0, Delta = 0.01f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	private static (IonApplication Game, IEvents Events) Build(Action<IonApplicationBuilder> register, Action<IonApplication> use)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton<SceneTokenLog>();
		builder.Services.AddScoped<SceneToken>();
		register(builder);
		var game = builder.Build();
		game.UseEvents();
		use(game);
		return (game, game.Services.GetRequiredService<IEvents>());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASingletonSceneSystemGetsTheScenesScopedServices()
	{
		var dt = NewGameTime();
		var (game, events) = Build(
			builder => builder.AddSystem<TokenSystem>(),
			app =>
			{
				app.UseScene(1, scene => scene.UseSystem<TokenSystem>());
				app.UseScene(2, scene => scene.UseSystem<TokenSystem>());
			});
		using var _ = game;
		var loop = game.Build();
		var log = game.Services.GetRequiredService<SceneTokenLog>();
		var scenes = game.Services.GetRequiredService<SceneSystem>();

		loop.Init(dt);
		var first = scenes.ActiveScene!.Services.GetRequiredService<SceneToken>();
		events.EmitChangeScene(2);
		loop.Step(dt);
		var second = scenes.ActiveScene!.Services.GetRequiredService<SceneToken>();

		Assert.NotSame(first, second);
		Assert.Equal([first, second], log.Tokens);
		Assert.True(first.IsDisposed);
		Assert.False(second.IsDisposed);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASingletonSceneSystemIsCreatedPerLoadAndDisposedWithTheScene()
	{
		var dt = NewGameTime();
		var (game, events) = Build(
			builder => builder.AddSystem<TokenSystem>(),
			app =>
			{
				app.UseScene(1, scene => scene.UseSystem<TokenSystem>());
				app.UseScene(2, _ => { });
			});
		using var _ = game;
		var loop = game.Build();
		var log = game.Services.GetRequiredService<SceneTokenLog>();

		loop.Init(dt);
		events.EmitChangeScene(2);
		loop.Step(dt);
		events.EmitChangeScene(1);
		loop.Step(dt);

		Assert.Equal(2, log.Systems.Count);
		Assert.NotSame(log.Systems[0], log.Systems[1]);
		Assert.True(log.Systems[0].IsDisposed);
		Assert.False(log.Systems[1].IsDisposed);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASingletonRegisteredWithAFactoryIsBuiltFromTheScenesScope()
	{
		var dt = NewGameTime();
		var (game, _) = Build(
			builder => builder.Services.AddSingleton(sp => new TokenSystem(sp.GetRequiredService<SceneToken>(), sp.GetRequiredService<SceneTokenLog>())),
			app => app.UseScene(1, scene => scene.UseSystem<TokenSystem>()));
		using var __ = game;
		var loop = game.Build();

		loop.Init(dt);

		var scenes = game.Services.GetRequiredService<SceneSystem>();
		var token = scenes.ActiveScene!.Services.GetRequiredService<SceneToken>();
		Assert.Equal([token], game.Services.GetRequiredService<SceneTokenLog>().Tokens);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheRootScheduleKeepsTheSingletonInstance()
	{
		var dt = NewGameTime();
		var (game, _) = Build(
			builder =>
			{
				builder.Services.AddSingleton<CounterLog>();
				builder.AddSystem<RootCounter>();
			},
			app =>
			{
				app.UseSystem<RootCounter>();
				app.UseScene(1, scene => scene.UseSystem<RootCounter>());
			});
		using var _ = game;
		var loop = game.Build();

		loop.Init(dt);
		loop.Step(dt);

		// The root schedule runs the registered singleton; the scene runs an instance of its own.
		var singleton = game.Services.GetRequiredService<RootCounter>();
		var counters = game.Services.GetRequiredService<CounterLog>().Instances;
		Assert.Equal(1, singleton.Updates);
		var sceneCounter = Assert.Single(counters, c => !ReferenceEquals(c, singleton));
		Assert.Equal(1, sceneCounter.Updates);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ASingletonInstanceUsedByASceneIsAnError()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddScenes();
		builder.Services.AddSingleton(new StatelessSystem());
		using var game = builder.Build();
		game.UseEvents();
		game.UseScene(1, scene => scene.UseSystem<StatelessSystem>());

		var ex = Assert.Throws<IonScheduleException>(() => game.Build());
		Assert.Equal([ScheduleDiagnosticCodes.SingletonInstanceInScene], ex.Codes.ToArray());
		Assert.Contains("StatelessSystem", ex.Message, StringComparison.Ordinal);
		Assert.Contains("AddSystem", ex.Message, StringComparison.Ordinal);
	}
}

public sealed class SceneToken : IDisposable
{
	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;
}

public sealed class SceneTokenLog
{
	public List<SceneToken> Tokens { get; } = [];

	public List<TokenSystem> Systems { get; } = [];
}

public sealed class TokenSystem(SceneToken token, SceneTokenLog log) : IDisposable
{
	public bool IsDisposed { get; private set; }

	[Init]
	public void Init(GameTime dt)
	{
		log.Tokens.Add(token);
		log.Systems.Add(this);
	}

	public void Dispose() => IsDisposed = true;
}

public sealed class CounterLog
{
	public List<RootCounter> Instances { get; } = [];
}

public sealed class RootCounter
{
	public RootCounter(CounterLog log) => log.Instances.Add(this);

	public int Updates { get; private set; }

	[Update]
	public void Update(GameTime dt) => Updates++;
}

public sealed class StatelessSystem
{
	[Update]
	public void Update(GameTime dt) { }
}
