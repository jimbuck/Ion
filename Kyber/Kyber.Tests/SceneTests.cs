namespace Kyber.Tests;

public class SceneTests
{
    [Fact, Trait(CATEGORY, INTEGRATION)]
    public void SceneManager_SwitchingScenes()
    {
		var dt = GameTime.FromDelta(0.01f);
		using var _ = SetupWithScenes(3, out var services, out var game);

        var sceneManager = services.GetRequiredService<SceneManager>();

		game.Initialize();

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

        game.Step(dt);
        sceneManager.LoadScene(sceneManager.Scenes[1]);
        game.Step(dt);

        Assert.Equal(sceneManager.Scenes[1], sceneManager.CurrentScene);

        sceneManager.LoadScene(sceneManager.Scenes[0]);
        game.Step(dt);

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

		game.Destroy();
    }
}
