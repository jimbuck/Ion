using Kyber;
using Kyber.Extensions.Debug;
using Kyber.Extensions.Graphics;
using Kyber.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

var builder = KyberApplication.CreateBuilder(args);


builder.Services.AddVeldridGraphics(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = Color.CornflowerBlue;
});
builder.Services.AddScenes();

builder.Services.AddSingleton<TestMiddleware>();
builder.Services.AddSingleton<MicroTimerSystem>();

var game = builder.Build();
game.UseSystem<MicroTimerSystem>();
game.UseEvents();
game.UseVeldridGraphics();

game.UseInit((GameLoopDelegate next, IEventEmitter eventEmitter) =>
{
	return dt => {
		eventEmitter.Emit<int>(42);
		next(dt);
	};
});

game.UseFirst(next =>
{
	var logFrameNumber = Throttler.Wrap(TimeSpan.FromSeconds(0.5), (dt) => {
		Console.WriteLine($"Frame: {dt.Frame}");
	});
	return dt =>
	{
		logFrameNumber(dt);
		next(dt);
	};
});

game.UseUpdate((GameLoopDelegate next, IEventEmitter eventEmitter, IEventListener events) =>
{
	var flip = false;
	var switchScene = Throttler.Wrap(TimeSpan.FromSeconds(1), (dt) => {
		eventEmitter.Emit(new ChangeSceneEvent(flip ? "MainMenu" : "Gameplay"));
		flip = !flip;
	});

	return dt =>
	{
		if (events.On<int>(out var e)) Console.WriteLine($"Int event! {e.Data}");
		next(dt);
		switchScene(dt);
	};
});

game.UseRender((GameLoopDelegate next, IEventEmitter eventEmitter) =>
{
	return dt =>
	{
		//Console.WriteLine("Game Render");
		next(dt);

		if (dt.Frame > 5000)
		{
			Console.WriteLine($"Frames exceeded ({dt.Frame}), exiting!");
			eventEmitter.Emit<ExitGameEvent>();
		}
	};
});

game.UseScene("MainMenu", scene =>
{
	scene.UseRender((GameLoopDelegate next, ISpriteBatch spriteBatch) =>
	{
		return dt =>
		{
			spriteBatch.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90));
			next(dt);
		};
	});

	//scene.UseSystem<TestMiddleware>();
});

game.UseScene("Gameplay", scene =>
{
	scene.UseRender((GameLoopDelegate next, ISpriteBatch spriteBatch) =>
	{
		return dt =>
		{
			spriteBatch.DrawRect(Color.DarkRed, new RectangleF(10, 10, 90, 90));
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

static class Throttler
{
	public static Action<GameTime> Wrap(TimeSpan interval, Action<GameTime> action)
	{
		var total = 0f;

		return (dt) =>
		{
			total += dt.Delta;
			if (total > interval.TotalSeconds)
			{
				total = 0;
				action(dt);
			}
		};
	}
}