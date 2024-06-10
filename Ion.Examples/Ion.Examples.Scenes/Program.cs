using System.Collections;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Debug;
using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;
using Ion.Extensions.Coroutines;

using Ion.Examples.Scenes;

var builder = IonApplication.CreateBuilder(args);

builder.Services.AddDebugUtils(builder.Configuration);
builder.Services.AddVeldridGraphics(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = Color.CornflowerBlue;
	graphics.PreferredBackend = GraphicsBackend.Vulkan;
});
builder.Services.AddScenes();
builder.Services.AddCoroutines();

builder.Services.AddSingleton<TestMiddleware>();

var game = builder.Build();
game.UseDebugUtils();
game.UseEvents();
game.UseVeldridGraphics();

game.UseFirst((GameLoopDelegate next, IInputState input, ICoroutineRunner coroutine) =>
{
	IEnumerator CountDown(int from)
	{
		while (from >= 0)
		{
			Console.WriteLine("Countdown: " + from--);
			yield return Wait.Until(() => input.Pressed(Key.Space));
		}
	}

	return dt =>
	{
		if (input.Pressed(Key.Enter))
		{
			coroutine.Start(CountDown(5));
		}

		coroutine.Update(dt);
		
		next(dt);
	};
});

game.UseInit((GameLoopDelegate next, IEventEmitter eventEmitter, IWindow window) =>
{
	return dt => {
		window.IsResizable = true;

		eventEmitter.Emit<int>(42);
		next(dt);
	};
});

game.UseFirst((GameLoopDelegate next, IInputState input, ITraceManager traceManager) =>
{
	var logFrameNumber = Throttler.Wrap(TimeSpan.FromSeconds(0.5), (dt) =>
	{
		Console.WriteLine($"Frame: {dt.Frame}!");
	});

	return dt =>
	{
		if (input.Pressed(Key.F5)) traceManager.Start();
		if (input.Pressed(Key.F6))
		{
			traceManager.Stop();
			traceManager.OutputTrace();
		}

		logFrameNumber(dt);
		next(dt);
	};
});

game.UseUpdate((GameLoopDelegate next, IEventEmitter eventEmitter, IEventListener events) =>
{
	var flip = false;
	var switchScene = Throttler.Wrap(TimeSpan.FromSeconds(3), (dt) => {
		eventEmitter.EmitChangeScene(flip ? Scene.MainMenu : Scene.Gameplay);
		flip = !flip;
	});

	return dt =>
	{
		if (events.On<int>(out var e)) Console.WriteLine($"Int event! {e.Data}");
		next(dt);
		switchScene(dt);
	};
});

game.UseRender((GameLoopDelegate next, IEventEmitter eventEmitter, IInputState input) =>
{
	return dt =>
	{
		//Console.WriteLine("Game Render");
		next(dt);

		if (input.Down(Key.Escape))
		{
			Console.WriteLine("Escape Pressed!");
			eventEmitter.Emit<ExitGameEvent>();
		}
	};
});

game.UseScene(Scene.MainMenu, scene =>
{
	scene.UseRender((GameLoopDelegate next, ISpriteBatch spriteBatch) =>
	{
		return dt =>
		{
			spriteBatch.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90));
			next(dt);
		};
	});

	scene.UseSystem<TestMiddleware>();
});

game.UseScene(Scene.Gameplay, scene =>
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

namespace Ion.Examples.Scenes
{
	public enum Scene
	{
		MainMenu = 1,
		Gameplay,
		Test,
	}

	public partial class TestMiddleware
	{
		private readonly Queue<float> _frameTimes = new();

		public TestMiddleware()
		{
			Console.WriteLine("TestMiddleware Constructor");
		}

		[First]
		public void CoolFirstMiddleware(GameTime dt, GameLoopDelegate next)
		{
			//Console.WriteLine($"Class First {dt.Frame}");
			next(dt);
		}

		[FixedUpdate]
		public GameLoopDelegate FancyFixedUpdate(GameLoopDelegate next)
		{
			Console.WriteLine("Class Fixed Update SETUP");
			uint count = 0;
			return dt =>
			{
				count++;
				//Console.WriteLine($"Class Fixed Update inside {count++}");
				next(dt);
			};
		}

		[Render]
		public GameLoopDelegate Render(GameLoopDelegate next)
		{
			var stopwatch = new Stopwatch();

			return dt =>
			{
				stopwatch.Restart();
				next(dt);
				stopwatch.Stop();
				_frameTimes.Enqueue((float)stopwatch.Elapsed.TotalSeconds);
				while (_frameTimes.Count > 60) _frameTimes.Dequeue();
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

}