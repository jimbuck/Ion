global using Microsoft.Extensions.Logging;
global using Kyber.Scenes;

using Kyber.Hosting;
using Kyber.Hosting.Scenes;

using Kyber.Examples.Simple;

var gameHost = KyberHost.CreateDefaultBuilder()
    .ConfigureKyber(static (game) => {
        game.Config.WindowTitle = "Kyber Simple Example";

        game.AddSystem<TestLoggerSystem>()
            //.AddSystem<SceneSwitcherSystem>()
            .AddScene<Scenes.Main>() // Class with interface
            .AddScene(Scenes.Gameplay) // Named method
            .AddScene("Inline", static (scene) => scene.AddSystem<TestLoggerSystem>());
    })    
    .Build();

gameHost.StartAsync().Wait();
