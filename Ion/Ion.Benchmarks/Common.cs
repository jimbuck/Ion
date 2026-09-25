using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ion.Benchmarks;

/// <summary>
/// A user system shaped exactly like the ones in the samples: one leaf step per stage, <c>[Update] void M(GameTime dt)</c>.
/// The body is intentionally trivial so that the measured cost is the engine's dispatch overhead, not the game logic.
/// </summary>
public sealed class CounterSystem
{
	public int Init, First, FixedUpdate, Update, Render, Last, Destroy;

	[Init] public void OnInit(GameTime dt) { Init++; }
	[First] public void OnFirst(GameTime dt) { First++; }
	[FixedUpdate] public void OnFixedUpdate(GameTime dt) { FixedUpdate++; }
	[Update] public void OnUpdate(GameTime dt) { Update++; }
	[Render] public void OnRender(GameTime dt) { Render++; }
	[Last] public void OnLast(GameTime dt) { Last++; }
	[Destroy] public void OnDestroy(GameTime dt) { Destroy++; }

	/// <summary>The same work as <see cref="OnUpdate"/>, called directly by the flat-loop baseline.</summary>
	public void UpdateDirect(GameTime dt) { Update++; }
}

/// <summary>
/// The same system in the legacy middleware form (<c>GameLoopDelegate next</c>), which the schedule still runs as opaque
/// middleware (and warns about, ION010). This is the shape every system had before 0.3.
/// </summary>
public sealed class LegacyCounterSystem
{
	public int Update;

	[Update] public void OnUpdate(GameTime dt, GameLoopDelegate next) { Update++; next(dt); }
}

// Distinct system types so that DI resolves distinct singleton instances (mirrors a real game with many systems).
public sealed class System0 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System1 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System2 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System3 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System4 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System5 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System6 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }
public sealed class System7 : IStageSystem { public int Count; [Update] public void OnUpdate(GameTime dt) { Count++; } public void UpdateDirect(GameTime dt) { Count++; } }

public interface IStageSystem
{
	void OnUpdate(GameTime dt);
	void UpdateDirect(GameTime dt);
}

public static class BenchUtils
{
	public static readonly Type[] EightSystemTypes = [typeof(System0), typeof(System1), typeof(System2), typeof(System3), typeof(System4), typeof(System5), typeof(System6), typeof(System7)];

	public static GameTime NewGameTime() => new() { Frame = 0, Delta = 1f / 60f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	/// <summary>Builds a headless application (no graphics, no audio) with the given system types registered as singletons and added with UseSystem.</summary>
	public static IonApplication BuildHeadless(Action<IServiceCollection>? services, Action<IIonApplication>? use, params Type[] systems)
	{
		var builder = IonApplication.CreateBuilder([]);
		builder.Services.AddLogging(l => l.ClearProviders());
		foreach (var s in systems) builder.Services.AddSingleton(s);
		services?.Invoke(builder.Services);

		var app = builder.Build();
		app.UseEvents();
		use?.Invoke(app);
		foreach (var s in systems) app.UseSystem(s);
		return app;
	}
}
