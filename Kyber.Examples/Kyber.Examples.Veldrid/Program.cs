global using Microsoft.Extensions.Logging;

using Kyber.Hosting;

using Kyber.Examples.Veldrid;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.WindowTitle = "Kyber Veldrid Example";

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<QuadRendererSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
