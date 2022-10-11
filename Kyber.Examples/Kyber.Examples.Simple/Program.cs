global using Microsoft.Extensions.Logging;
global using Kyber.Core;
global using Kyber.Core.Hosting;

using Kyber.Examples.Simple;

var host = KyberHost.CreateDefaultBuilder()
    .ConfigureKyber((game, services) =>
    {
        game.Config.WindowTitle = "Kyber Simple Example";

        game.AddSystem<StartedLoggerSystem>();
    })
    .Build();

host.StartAsync().Wait();