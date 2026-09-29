namespace Ion.Generators.Tests;

/// <summary>
/// Small applications covering what the generator emits. Each defines <c>App.Run()</c>, which builds the application
/// (the registrations and the <c>Build()</c> call in one method, so the generator sees them all), runs Init, one frame and
/// Destroy, and returns the printed schedule, whether the generated schedule ran, and the calls the steps logged.
/// </summary>
internal static class GoldenScenarios
{
	public const string Preamble = """
		using System;
		using System.Collections.Generic;
		using Ion;
		using Ion.Core;
		using Ion.Extensions.Scenes;
		using Microsoft.Extensions.DependencyInjection;
		using Microsoft.Extensions.Logging;

		public static class Log
		{
			public static readonly List<string> Calls = new();
		}

		""";

	private static string App(string services, string registrations, string frame = "loop.Step(new GameTime());") => $$"""
		public static class App
		{
			public static object Run()
			{
				var builder = IonApplication.CreateBuilder(new[] { "--Ion:Headless=true" });
				builder.Services.AddLogging(logging => logging.ClearProviders());
				{{services}}
				using var app = builder.Build();
				{{registrations}}
				var loop = app.Build();
				loop.Initialize();
				{{frame}}
				loop.Shutdown();
				return (app.PrintSchedule(), loop.Schedule!.IsGenerated, string.Join(", ", Log.Calls));
			}
		}
		""";

	public static readonly (string Name, string Source)[] All =
	[
		("SmallApp", Preamble + """
			public sealed class Clock { public int Ticks; }

			public sealed class Physics(Clock clock)
			{
				[Init] public void Load() => Log.Calls.Add("physics load");
				[FixedUpdate] public void Step(GameTime dt) { clock.Ticks++; Log.Calls.Add("physics step"); }
			}

			[After<Physics>]
			public sealed class Paddle
			{
				[FixedUpdate(Order = -10)] public void Move(GameTime dt, Clock clock) => Log.Calls.Add("paddle move " + clock.Ticks);
				[Render] public static void Draw(GameTime dt) => Log.Calls.Add("paddle draw");
			}

			""" + App(
				"builder.Services.AddSingleton<Clock>().AddSingleton<Physics>().AddSingleton<Paddle>();",
				"app.UseSystem<Paddle>().UseSystem<Physics>();")),

		("Scopes", Preamble + """
			public sealed class Frame
			{
				[Begin(Stage.Render, Order = -900)] public void Begin(GameTime dt) => Log.Calls.Add("frame begin");
				[End(Stage.Render)] public void End() => Log.Calls.Add("frame end");
			}

			public sealed class Batch
			{
				[Begin(Stage.Render, Order = -850, ScopeName = "sprites")] public void Open(GameTime dt) => Log.Calls.Add("batch open");
				[End(Stage.Render, ScopeName = "sprites")] public void Close(GameTime dt, ILoopContext context) => Log.Calls.Add("batch close " + context.Stage);
				[Begin(Stage.Update, ScopeName = "profile")] public void Start(GameTime dt) => Log.Calls.Add("profile start");
				[End(Stage.Update, ScopeName = "profile")] public void Stop(GameTime dt) => Log.Calls.Add("profile stop");
			}

			public sealed class Sprites
			{
				[Render] public void Draw(GameTime dt) => Log.Calls.Add("draw");
				[Update] public void Tick(GameTime dt) => Log.Calls.Add("tick");
			}

			""" + App(
				"builder.Services.AddSingleton<Frame>().AddSingleton<Batch>().AddSingleton<Sprites>();",
				"app.UseSystem<Sprites>().UseSystem<Batch>().UseSystem<Frame>();")),

		("FunctionSteps", Preamble + """
			public sealed class Counter
			{
				public int Count;
				[Update] public void Tick(GameTime dt) { Count++; Log.Calls.Add("counter tick"); }
			}

			public static class Hud
			{
				public static void Draw(GameTime dt, Counter counter) => Log.Calls.Add("hud " + counter.Count);
			}

			""" + App(
				"builder.Services.AddSingleton<Counter>();",
				"""
				app.UseSystem<Counter>();
					app.Init((GameTime dt) => Log.Calls.Add("init function"));
					app.Update([After<Counter>] (GameTime dt, Counter counter) => Log.Calls.Add("after counter " + counter.Count), order: -1);
					app.Render<Counter>(Hud.Draw);
					if (Environment.GetEnvironmentVariable("ION_NEVER_SET") is null) app.Last((GameTime dt, ILoopContext context) => Log.Calls.Add("conditional last"));
				""")),

		("Scene", Preamble + """
			public sealed class Menu
			{
				[Init] public void Open(GameTime dt) => Log.Calls.Add("menu open");
				[Update] public void Tick(GameTime dt) => Log.Calls.Add("menu tick");
				[Destroy] public void Close(GameTime dt) => Log.Calls.Add("menu close");
			}

			public enum Scene { Menu = 1, Game }

			""" + App(
				"builder.Services.AddScenes().AddScoped<Menu>();",
				"""
				app.UseEvents();
					app.UseScene(Scene.Menu, scene =>
					{
						scene.UseSystem<Menu>();
						scene.Render((GameTime dt, ICurrentScene current) => Log.Calls.Add("render scene " + current.SceneId));
					});
					app.UseScene(2, scene => scene.Update((GameTime dt) => Log.Calls.Add("game tick")));
					app.Update((GameTime dt) => Log.Calls.Add("root tick"));
				""",
				"loop.Step(new GameTime()); loop.Step(new GameTime());")),

		// A system added again is not added twice (UseSystem is idempotent, like modules that add their dependencies):
		// directly, through a helper, after a conditional registration, and in a scene.
		("RepeatedSystems", Preamble + """
			public sealed class Input { [First] public void Poll(GameTime dt) => Log.Calls.Add("input poll"); }
			public sealed class Renderer { [Render(Order = -10)] public void Draw(GameTime dt) => Log.Calls.Add("renderer draw"); }
			public sealed class Extraction { [Render] public void Extract(GameTime dt) => Log.Calls.Add("extract"); }
			public sealed class Menu { [Update] public void Tick(GameTime dt) => Log.Calls.Add("menu tick"); }
			public sealed class Late { [Last] public void Finish(GameTime dt) => Log.Calls.Add("late finish"); }

			public static class Modules
			{
				public static IIonApplication UseRenderer(this IIonApplication app) => app.UseSystem<Input>().UseSystem<Renderer>();
				public static IIonApplication UseExtraction(this IIonApplication app) => app.UseRenderer().UseSystem<Extraction>();
			}

			""" + App(
				"builder.Services.AddScenes().AddSingleton<Input>().AddSingleton<Renderer>().AddSingleton<Extraction>().AddSingleton<Late>().AddScoped<Menu>();",
				"""
				app.UseEvents().UseRenderer().UseExtraction().UseRenderer().UseSystem<Input>();
					if (Environment.GetEnvironmentVariable("ION_NEVER_SET") is not null) app.UseSystem<Late>();
					app.UseScene(1, scene => scene.UseSystem<Menu>().UseSystem<Menu>());
					app.UseSystem<Extraction>().UseSystem<Late>();
					app.Services.GetRequiredService<IEvents>().EmitChangeScene(1);
				""",
				"loop.Step(new GameTime()); loop.Step(new GameTime());")),
	];
}
