using Kyber.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Kyber.Tests.TestConstants;

namespace Kyber.Tests;

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

		var gameLoop = game.RunManually();

		gameLoop.Init(dt);

		Assert.Equal("Scene0", currentScene.Name);

		gameLoop.Step(dt);
		eventEmitter.Emit(new ChangeSceneEvent("Scene1"));
		gameLoop.Step(dt);

        Assert.Equal("Scene1", currentScene.Name);

		eventEmitter.Emit(new ChangeSceneEvent("Scene0"));
		gameLoop.Step(dt);

		Assert.Equal("Scene0", currentScene.Name);

		gameLoop.Destroy(dt);
    }
}
