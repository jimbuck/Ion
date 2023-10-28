using Kyber;
using Kyber.Builder;
using Kyber.Scenes;

using Microsoft.Extensions.DependencyInjection;

var builder = KyberApplication.CreateBuilder(args);

builder.Services.AddScenes();
builder.Services.AddSingleton<TestMiddleware>();


var game = builder.Build();

//game.UseSystem<TestMiddleware>();

//game.UseFirst(next =>
//{
//	Console.WriteLine("Game First Setup");
//	return dt =>
//	{
//		Console.WriteLine("Game First");
//		next(dt);
//	};
//});

game.UseUpdate(next =>
{
	var sceneManager = game.Services.GetRequiredService<ISceneManager>();
	var total = 0f;
	var flip = false;
	return dt =>
	{
		next(dt);
		total += dt.Delta;
		if (total > 3)
		{
			flip = !flip;
			total = 0;
			sceneManager.LoadScene(flip ? "MainMenu" : "Gameplay");
		}
	};
});

game.UseRender(next => dt =>
{
	//Console.WriteLine("Game Render");
	next(dt);
});

game.UseScene("MainMenu", scene =>
{
	scene.UseRender(next =>
	{
		Console.WriteLine("MainMenu Scene Setup Render");
		return dt =>
		{
			Console.WriteLine("MainMenu Scene Render");
			next(dt);
		};
	});

	//scene.UseSystem<TestMiddleware>();
});

game.UseScene("Gameplay", scene =>
{
	scene.UseRender(next =>
	{
		Console.WriteLine("Gameplay Scene Setup Render");
		return dt =>
		{
			Console.WriteLine("Gameplay Scene Render");
			next(dt);
		};
	});
});

game.UseRender(next => dt => Console.WriteLine("NEVER GETTING CALLED!"));

game.Run();

public class TestMiddleware
{
	public TestMiddleware()
	{
		Console.WriteLine("TestMiddleware Constructor");
	}

	[First]
	public void CoolFirstMiddleware(GameTime dt, GameLoopDelegate next)
	{
		Console.WriteLine($"Class First {dt.Frame}");
		next(dt);
	}

	[FixedUpdate]
	public GameLoopDelegate FancyFixedUpdate(GameLoopDelegate next)
	{
		Console.WriteLine("Class Fixed Update SETUP");
		uint count = 0;
		return dt =>
		{
			Console.WriteLine($"Class Fixed Update inside {count++}");
			next(dt);
		};
	}
}
