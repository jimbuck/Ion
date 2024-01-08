﻿global using Microsoft.Extensions.Logging;
global using Ion;

using Ion.Hosting;

using Ion.Examples.Veldrid;

var gameHost = IonHost.CreateDefaultBuilder()
		.ConfigureIon(static (game) =>
		{
			game.Config.Title = "Ion Veldrid Example";
			game.Config.WindowWidth = 900;
			game.Config.WindowHeight = 900;
			game.Config.VSync = false;
			game.Config.MaxFPS = 300;

			//game.AddSystem<TestLoggerSystem>()
			//	.AddSystem<QuadRendererSystem>()
			//	.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();