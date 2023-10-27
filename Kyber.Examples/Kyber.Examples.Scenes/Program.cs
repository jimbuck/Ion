using Kyber;
using Kyber.Builder;

using Microsoft.Extensions.DependencyInjection;


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


var builder = KyberApplication.CreateBuilder(args);

builder.Services.AddSingleton<TestMiddleware>();

var game = builder.Build();

game.UseSystem<TestMiddleware>();

game.UseFirst((dt, next) =>
{
	Console.WriteLine("Inline First");
	next(dt);
});

game.UseFirst(dt =>
{
	Console.WriteLine("Inline First");
});

game.UseFixedUpdate(next => dt =>
{
	Console.WriteLine("Inline FixedUpdate");
	next(dt);
});

game.UseUpdate(next => dt =>
{
	Console.WriteLine("Inline Update");
	next(dt);
});

game.UseRender(next => dt =>
{
	Console.WriteLine("Inline Render");
	next(dt);
});


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
		Console.WriteLine("Class Fixed Update outside");
		uint count = 0;
		return dt =>
		{
			Console.WriteLine($"Class Fixed Update inside {count++}");
			next(dt);
		};
	}
}

public class SceneBuilder
{

}

public class Scenes
{
	public void TestScene(SceneBuilder builder)
	{

	}
}

public class SceneLoop
{

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[Last]
	public void Last(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}

	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate next)
	{
		// TODO: Run on current scene.
		next(dt);
	}
}

public static class KyberApplicationExtensions_Scenes
{
	public static KyberApplication UseScene<T>(this KyberApplication app)
	{

		return app;
	}
}

public interface IScene
{
	void Configure(KyberApplication app);
}


public class TestScene : IScene
{
	public void Configure(KyberApplication app)
	{
		
	}
}