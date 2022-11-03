global using System.Numerics;
global using Microsoft.Extensions.Logging;
global using Kyber;


using Kyber.Hosting;

using Kyber.Examples.SpriteRenderer;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.WindowTitle = "Kyber SpriteRenderer Example";
			game.Config.WindowWidth = 640;
			game.Config.WindowHeight = 480;
			//game.Config.PreferredBackend = Kyber.Graphics.GraphicsBackend.Vulkan;
			game.Config.VSync = false;
			game.Config.MaxFPS = 1000;

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<TestSpriteRendererSystem>()
				.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
