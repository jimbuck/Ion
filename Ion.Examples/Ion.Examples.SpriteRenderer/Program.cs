global using System.Numerics;
global using Microsoft.Extensions.Logging;
global using Ion;

using Ion.Hosting;
using Ion.Examples.SpriteRenderer;


var gameHost = IonHost.CreateDefaultBuilder()
		.ConfigureIon(static (game) =>
		{
			game.Config.Title = "Ion SpriteRenderer Example";
			game.Config.WindowWidth = 1920;
			game.Config.WindowHeight = 1080;
			//game.Config.PreferredBackend = Veldrid.GraphicsBackend.Vulkan;
			//game.Config.PreferredBackend = Veldrid.GraphicsBackend.OpenGL;
			game.Config.VSync = false;
			game.Config.MaxFPS = 120;
			game.Config.ClearColor = Color.CornflowerBlue;

			//game.AddSystem<TestLoggerSystem>()
			//	.AddSystem<TestSpriteRendererSystem>()
			//	.AddSystem<UserInputSystem>();
		})
		.Build();

gameHost.StartAsync().Wait();
