
namespace Ion.Tests;

internal static class TestUtils
{
	public static IDisposable SetupWithSystems(out IServiceProvider services, out IonApplication game, params Type[] systems)
	{
		var builder = IonApplication.CreateBuilder();

		foreach (var system in systems) builder.Services.AddSingleton(system);

		game = builder.Build();
		game.UseEvents();

		foreach (var system in systems) game.UseSystem(system);

			services = game.Services;

        return game;
    }
}

public class TestSystem
{
	public int InitializeCount { get; private set; } = 0;
	public int FirstCount { get; private set; } = 0;
	public int FixedUpdateCount { get; private set; } = 0;
	public int UpdateCount { get; private set; } = 0;
	public int RenderCount { get; private set; } = 0;
	public int LastCount { get; private set; } = 0;
	public int DestroyCount { get; private set; } = 0;

	[Init]
	public void Initialize(GameTime dt, GameLoopDelegate next) {
		InitializeCount++;
		next(dt);
	}
	[First]
	public void First(GameTime dt, GameLoopDelegate next) {
		FirstCount++;
		next(dt);
	}
	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate next) {
		FixedUpdateCount++;
		next(dt);
	}
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next) {
		UpdateCount++;
		next(dt);
	}
	[Render]
	public void Render(GameTime dt, GameLoopDelegate next) {
		RenderCount++;
		next(dt);
	}
	[Last]
	public void Last(GameTime dt, GameLoopDelegate next) {
		LastCount++;
		next(dt);
	}
	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate next) {
		DestroyCount++;
		next(dt);
	}

	public void Reset()
	{
		InitializeCount = 0;
		FirstCount = 0;
		FixedUpdateCount = 0;
		UpdateCount = 0;
		RenderCount = 0;
		LastCount = 0;
		DestroyCount = 0;
	}
}
