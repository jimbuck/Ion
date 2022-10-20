namespace Kyber.Tests;

public class SceneTests
{
    [Fact, Trait(CATEGORY, INTEGRATION)]
    public void SceneManager_SwitchingScenes()
    {
        using var _ = SetupWithScenes(3, out var services, out var game);

        var sceneManager = services.GetRequiredService<SceneManager>();

		game.Initialize();

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

        game.Step(DT);
        sceneManager.LoadScene(sceneManager.Scenes[1]);
        game.Step(DT);

        Assert.Equal(sceneManager.Scenes[1], sceneManager.CurrentScene);

        sceneManager.LoadScene(sceneManager.Scenes[0]);
        game.Step(DT);

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

		game.Destroy();
    }
}
