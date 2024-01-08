using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

public class SceneTests
{
    [Fact, Trait(CATEGORY, INTEGRATION)]
    public void SwitchingScenes()
    {
		var dt = new GameTime()
		{
			Frame = 0,
			Delta = 0.01f,
			Alpha = 1f,
			Elapsed = TimeSpan.Zero,
		};
		using var _ = TestUtils.SetupWithScenes(3, out var services, out var game);

		var eventEmitter = services.GetRequiredService<IEventEmitter>();
		var currentScene = services.GetRequiredService<ICurrentScene>();

		var gameLoop = game.Build();

		gameLoop.Init(dt);

		Assert.Equal(1, currentScene.SceneId);

		gameLoop.Step(dt);
		eventEmitter.Emit(new ChangeSceneEvent(2));
		gameLoop.Step(dt);

        Assert.Equal(2, currentScene.SceneId);

		eventEmitter.Emit(new ChangeSceneEvent(1));
		gameLoop.Step(dt);

		Assert.Equal(1, currentScene.SceneId);

		gameLoop.Destroy(dt);
    }
}
