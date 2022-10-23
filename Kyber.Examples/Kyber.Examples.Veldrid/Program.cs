global using Microsoft.Extensions.Logging;
global using Kyber;

using Kyber.Hosting;

using Kyber.Examples.Veldrid;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.WindowTitle = "Kyber Veldrid Example";
			game.Config.VSync = false;

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<QuadRendererSystem>()
				.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
