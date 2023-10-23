﻿global using Microsoft.Extensions.Logging;
global using Kyber;

using Kyber.Hosting;

using Kyber.Examples.Generators;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.Title = "Kyber Veldrid Example";
			game.Config.WindowWidth = 900;
			game.Config.WindowHeight = 900;
			game.Config.VSync = false;
			game.Config.MaxFPS = 300;

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<QuadRendererSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();