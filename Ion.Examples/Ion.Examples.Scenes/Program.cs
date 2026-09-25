using System.Collections;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Metrics;
using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;
using Ion.Extensions.Coroutines;

using Ion.Examples.Scenes;

var builder = IonApplication.CreateBuilder(args);

// Run with --Ion:Headless=true to use the headless graphics backend (no GPU or window), and --Ion:PrintSchedule=true to
// print every stage's steps (including each scene's) at startup.
var headless = builder.Configuration.IsHeadless();

builder.Services.AddMetrics(builder.Configuration);
if (headless)
{
	builder.Services.AddNullGraphics(builder.Configuration);
}
else
{
	builder.Services.AddVeldridGraphics(builder.Configuration, graphics =>
	{
		graphics.ClearColor = Color.CornflowerBlue;
		graphics.PreferredBackend = GraphicsBackend.Vulkan;
	});
}
builder.Services.AddScenes();
builder.Services.AddCoroutines();

builder.Services.AddSingleton<TestMiddleware>();

var game = builder.Build();

// Function steps: services in the parameter list are resolved once when the schedule is built. They run at the default
// order (0), after the engine's setup steps and the active scene, whatever the registration order.
game.Init((GameTime dt, IEvents events, IWindow window) =>
{
	window.IsResizable = true;
	events.Emit(42);
});

game.First((GameTime dt, IInputState input, ICoroutineRunner coroutine) =>
{
	if (input.Pressed(Key.Enter)) coroutine.Start(CountDown(5));
});

var logFrameNumber = Throttler.Wrap(TimeSpan.FromSeconds(0.5), (dt) =>
{
	//Console.WriteLine($"Frame: {dt.Frame}!");
});

// F5 starts profiling, F6 stops it and writes the kept frames (Ion:Metrics:TraceOutput). F9 captures the next 120 frames.
game.First((GameTime dt, IInputState input, IMetrics metrics) =>
{
	if (input.Pressed(Key.F5)) metrics.IsProfiling = true;
	if (input.Pressed(Key.F6))
	{
		metrics.IsProfiling = false;
		metrics.WriteTrace();
	}

	logFrameNumber(dt);
});

var gameplay = false;
// A reader is created once, outside the step, so it remembers what it has read (ION103).
var intEvents = game.Services.GetRequiredService<IEvents>().Reader<int>();

game.Update((GameTime dt, IEvents events, IInputState input) =>
{
	while (intEvents.TryRead(out var e)) Console.WriteLine($"Int event! {e}");

	// Tab switches between the two scenes.
	if (input.Pressed(Key.Tab))
	{
		gameplay = !gameplay;
		events.EmitChangeScene(gameplay ? Scene.Gameplay : Scene.MainMenu);
	}
});

game.Render((GameTime dt, IEvents events, IInputState input) =>
{
	if (input.Down(Key.Escape))
	{
		Console.WriteLine("Escape Pressed!");
		events.Emit<ExitGameEvent>();
	}
});

game.UseScene(Scene.MainMenu, scene =>
{
	scene.Render((GameTime dt, ISpriteBatch spriteBatch) => spriteBatch.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90)));
	scene.UseSystem<TestMiddleware>();
});

game.UseScene(Scene.Gameplay, scene =>
{
	scene.Render((GameTime dt, ISpriteBatch spriteBatch) => spriteBatch.DrawRect(Color.DarkRed, new RectangleF(10, 10, 90, 90)));
});

// The engine systems can be added after the game's own steps: engine steps use the reserved order bands.
game.UseMetrics();
game.UseEvents();
if (headless) game.UseNullGraphics();
else game.UseVeldridGraphics();
// Steps the shared ICoroutineRunner once per frame in the Update stage.
game.UseCoroutines();

game.Run();

static IEnumerator CountDown(int from)
{
	while (from > 0)
	{
		Console.WriteLine("Countdown: " + from--);
		yield return Wait.For(TimeSpan.FromSeconds(1));
	}

	Console.WriteLine("Countdown done!");
}

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
		private readonly Stopwatch _stopwatch = new();
		private uint _fixedUpdates;

		public TestMiddleware()
		{
			Console.WriteLine("TestMiddleware Constructor");
		}

		[First]
		public void CoolFirst(GameTime dt)
		{
			//Console.WriteLine($"Class First {dt.Frame}");
		}

		[FixedUpdate]
		public void CountFixedUpdates(GameTime dt) => _fixedUpdates++;

		// A scope around the rest of the scene's Render stage: EndRenderTimer runs after every scene render step, even if
		// one throws.
		[Begin(Stage.Render, Order = -100)]
		public void StartRenderTimer(GameTime dt) => _stopwatch.Restart();

		[End(Stage.Render, Order = -100)]
		public void EndRenderTimer(GameTime dt)
		{
			_stopwatch.Stop();
			_frameTimes.Enqueue((float)_stopwatch.Elapsed.TotalSeconds);
			while (_frameTimes.Count > 60) _frameTimes.Dequeue();
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
