namespace Ion.Generators.Tests;

/// <summary>
/// Applications that use events, compiled with the generator (the generated bus) and without it (the runtime bus), so the
/// two can be compared. Each defines <c>App.Run()</c>, which returns the log of what the systems saw and the name of the
/// bus type.
/// </summary>
internal static class EventScenarios
{
	public const string Preamble = """
		using System;
		using System.Collections.Generic;
		using Ion;
		using Ion.Core;
		using Microsoft.Extensions.DependencyInjection;
		using Microsoft.Extensions.Logging;

		public static class Log
		{
			public static readonly List<string> Calls = new();
		}

		""";

	/// <summary>
	/// Emits and reads in several stages at 120 fps with a 60 Hz fixed step (so about every other frame runs no fixed step):
	/// fixed-step readers, frame readers, latest-only readers, a reader of an event emitted later in the frame, a reader set,
	/// and a plugin event type the generator cannot see (emitted and read through generic helpers).
	/// </summary>
	public const string Game = Preamble + """
		public record struct Hit(int Id);
		public record struct Scored(int Points);
		public record struct Tick;
		public record struct PluginEvent(int Value);

		public static class Plugin
		{
			// Generic helpers: the generator cannot see which type they are called with, so the type stays on the runtime path.
			public static void Raise<T>(IEvents events, T e) where T : unmanaged => events.Emit(e);
			public static bool Next<T>(EventReaderSet readers, out T e) where T : unmanaged => readers.TryRead(out e);
		}

		public sealed class Producer(IEvents events)
		{
			private int _steps;

			[FixedUpdate]
			public void Step(GameTime dt)
			{
				_steps++;
				for (var i = 0; i < 3; i++) events.Emit(new Hit(_steps * 10 + i));
			}

			[Update]
			public void Update(GameTime dt)
			{
				if (dt.Frame % 2 == 0) events.Emit<Tick>();
				if (dt.Frame % 3 == 0) Plugin.Raise(events, new PluginEvent((int)dt.Frame));
			}
		}

		public sealed class Scorer(IEvents events)
		{
			private EventReader<Hit> _hits = events.Reader<Hit>();

			[Update(Order = 10)]
			public void Score(GameTime dt)
			{
				foreach (ref readonly var hit in _hits.Read())
				{
					Log.Calls.Add("hit " + hit.Id + " @" + dt.Frame);
					events.Emit(new Scored(hit.Id));
				}
			}
		}

		public sealed class Observer(IEvents events)
		{
			private EventReader<Scored> _scored = events.Reader<Scored>();
			private EventReader<Tick> _ticks = events.Reader<Tick>();
			private EventReader<Hit> _fixedHits = events.Reader<Hit>();
			private readonly EventReaderSet _plugin = new(events);

			[FixedUpdate(Order = -10)]
			public void Fixed(GameTime dt)
			{
				while (_fixedHits.TryRead(out var hit)) Log.Calls.Add("fixed saw " + hit.Id);
			}

			[First]
			public void First(GameTime dt)
			{
				if (_scored.TryReadLatest(out var s)) Log.Calls.Add("latest score " + s.Points + " @" + dt.Frame);
			}

			[Last]
			public void Last(GameTime dt)
			{
				Log.Calls.Add("ticks " + _ticks.Count + " @" + dt.Frame);
				_ticks.Skip();
				while (Plugin.Next<PluginEvent>(_plugin, out var p)) Log.Calls.Add("plugin " + p.Value);
			}
		}

		public static class App
		{
			public static object Run()
			{
				Log.Calls.Clear();
				var builder = IonApplication.CreateBuilder(new[] { "--Ion:Headless=true" });
				builder.Services.AddLogging(logging => logging.ClearProviders());
				builder.Services.AddSingleton<IClock>(new ManualClock());
				builder.Services.Configure<GameConfig>(c => { c.MaxFPS = 120; c.FixedUpdateRate = 60; });
				builder.Services.AddSingleton<Producer>().AddSingleton<Scorer>().AddSingleton<Observer>();
				using var app = builder.Build();
				app.UseEvents();
				app.UseSystem<Observer>();
				app.UseSystem<Producer>();
				app.UseSystem<Scorer>();
				var loop = app.Build();
				loop.RunFrames(12);

				var bus = app.Services.GetRequiredService<EventBus>();
				var plugin = bus.Find(typeof(PluginEvent));
				Log.Calls.Add("plugin channel " + (plugin is null ? "missing" : "runtime id " + (plugin.Id >= EventIds.FirstRuntimeId)));
				return (string.Join("\n", Log.Calls), bus.GetType().Name, EventId<Hit>.IsGenerated);
			}
		}
		""";
}
