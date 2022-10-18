using Microsoft.Extensions.DependencyInjection;

using Kyber.Hosting;
using Kyber.Hosting.Scenes;
using Kyber.Graphics;


namespace Kyber.Scenes.Tests;

public class SceneTests
{
    private const float _dt = 0.01f;

    [Fact]
    public void SceneManager_RunsAllPhases()
    {
        using var _ = _setup(1, out var services, out var game);

        var sceneManager = services.GetRequiredService<SceneManager>();

        game.Startup();

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

        game.Step(_dt);

        game.Shutdown();


    }

    [Fact]
    public void SceneManager_SwitchingScenes()
    {
        using var _ = _setup(3, out var services, out var game);

        var sceneManager = services.GetRequiredService<SceneManager>();

        game.Startup();

        Assert.Equal(sceneManager.Scenes[0], sceneManager.CurrentScene);

        game.Step(_dt);
        sceneManager.LoadScene(sceneManager.Scenes[1]);
        game.Step(_dt);

        Assert.Equal(sceneManager.Scenes[1], sceneManager.CurrentScene);
    }

    private IDisposable _setup(int sceneCount, out IServiceProvider services, out Game game)
    {
        var gameHost = KyberHost.CreateDefaultBuilder().ConfigureKyber((game) => {
            game.Config.GraphicsOutput = GraphicsOutput.None;

            for(var i = 0; i < sceneCount; i++)
            {
                game.AddScene($"Scene{i}", (scene) => { });
            }
        }).Build();

        services = gameHost.Services;
        game = gameHost.Services.GetRequiredService<Game>();

        return gameHost;
    }
}
