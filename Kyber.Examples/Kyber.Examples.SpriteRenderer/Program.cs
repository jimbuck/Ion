global using System.Numerics;
global using Microsoft.Extensions.Logging;
global using Kyber;

using Kyber.Hosting;

using Kyber.Examples.SpriteRenderer;

var gameHost = KyberHost.CreateDefaultBuilder()
		.ConfigureKyber(static (game) =>
		{
			game.Config.Title = "Kyber SpriteRenderer Example";
			game.Config.WindowWidth = 1920;
			game.Config.WindowHeight = 1080;
			//game.Config.PreferredBackend = Kyber.Graphics.GraphicsBackend.Vulkan;
			//game.Config.PreferredBackend = Kyber.Graphics.GraphicsBackend.OpenGL;
			game.Config.VSync = false;
			game.Config.MaxFPS = 3000;
			game.Config.ClearColor = Color.CornflowerBlue;

			game.AddSystem<TestLoggerSystem>()
				.AddSystem<TestSpriteRendererSystem>()
				.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
