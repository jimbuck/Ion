using Kyber.Extensions.Scenes;

namespace Kyber.Tests;

internal static class TestUtils
{
	public static IDisposable SetupWithScenes(int sceneCount, out IServiceProvider services, out KyberApplication game)
	{
		var builder = KyberApplication.CreateBuilder();
		builder.Services.AddScenes();

		game = builder.Build();
		game.UseEvents();

		for (var i = 0; i < sceneCount; i++) game.UseScene(i + 1, (scene) => { });

		services = game.Services;

		return game;
	}
}

public class TestSystem
{
	public bool IsEnabled { get; set; } = true;

	public int InitializeCount { get; private set; } = 0;
	public int FirstCount { get; private set; } = 0;
	public int FixedUpdateCount { get; private set; } = 0;
	public int UpdateCount { get; private set; } = 0;
	public int RenderCount { get; private set; } = 0;
	public int LastCount { get; private set; } = 0;
	public int DestroyCount { get; private set; } = 0;

	public void Initialize() { InitializeCount++; }
	public void First(GameTime dt) { FirstCount++; }
	public void FixedUpdate(GameTime dt) { FixedUpdateCount++; }
	public void Update(GameTime dt) { UpdateCount++; }
	public void Render(GameTime dt) { RenderCount++; }
	public void Last(GameTime dt) { LastCount++; }
	public void Destroy() { DestroyCount++; }

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
