global using System.Numerics;
global using Microsoft.Extensions.Logging;
global using Kyber;


using Kyber.Hosting;

using Kyber.Examples.SpriteRenderer;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.WindowTitle = "Kyber SpriteRenderer Example";
			//game.Config.PreferredBackend = Kyber.Graphics.GraphicsBackend.Vulkan;
			game.Config.VSync = false;
			game.Config.MaxFPS = 300;

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<TestSpriteRendererSystem>()
				.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
