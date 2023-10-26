global using Microsoft.Extensions.Logging;
global using Kyber.Scenes;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Hosting;
using Kyber.Hosting.Scenes;

using Kyber.Examples.Scenes;
using Kyber.Builder;


//var gameHost = KyberHost.CreateDefaultBuilder()
//    .ConfigureKyber(static (game) => {
//		game.Config.Title = "Kyber Simple Example";

//		//game.AddSystem<TestLoggerSystem>()
//		//	.AddSystem<SceneSwitcherSystem>()
//		//	.AddScene<LoadingScene>()
//		//	.AddScene<Scenes.Main>() // Class with interface
//		//	.AddScene(Scenes.Gameplay) // Named method
//		//	.AddScene(NamedFunction)
//		//	.AddScene("Inline", static (scene) => scene.AddSystem<TestLoggerSystem>());
//	})  
//    .Build();


var builder = GameApplication.CreateBuilder(args);

builder.Services.AddSingleton<TestLoggerSystem>();
builder.Services.AddSingleton<SceneSwitcherSystem>();

builder.Services.AddGameConfig(config =>
{
	config.Title = "Kyber Scene Example";
});

var game = builder.Build();

game.UseInit((next) => (dt) =>
{
	Console.WriteLine("Init");
	next(dt);
});

game.UseFirst(next => dt =>
{
	Console.WriteLine("First");
	next(dt);
});

game.UseFixedUpdate(next => dt =>
{
	Console.WriteLine("FixedUpdate");
	next(dt);
});

game.UseUpdate(next => dt =>
{
	Console.WriteLine("Update");
	next(dt);
});

game.UseRender(next => dt =>
{
	Console.WriteLine("Render");
	next(dt);
});

game.UseLast(next => dt =>
{
	Console.WriteLine("Last");
	next(dt);
});

game.UseDestroy(next => (dt) =>
{
	Console.WriteLine("Destroy");
	next(dt);
});

game.Run();