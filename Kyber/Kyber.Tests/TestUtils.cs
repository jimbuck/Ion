﻿namespace Kyber.Tests;

internal static class TestUtils
{
    public static IDisposable SetupWithSystems(bool render, out IServiceProvider services, out Game game, params Type[] systems)
    {
        var gameHost = KyberHost.CreateDefaultBuilder().ConfigureKyber((game) => {
			game.Config.Output = render ? GraphicsOutput.Window : GraphicsOutput.None;
			foreach (var type in systems) game.AddSystem(type);
        }).Build();

        services = gameHost.Services;
        game = gameHost.Services.GetRequiredService<Game>();

        return gameHost;
    }

    public static IDisposable SetupWithScenes(int sceneCount, out IServiceProvider services, out Game game)
    {
        var gameHost = KyberHost.CreateDefaultBuilder().ConfigureKyber((game) => {

            for (var i = 0; i < sceneCount; i++) game.AddScene($"Scene{i}", (scene) => { });
		}).Build();

        services = gameHost.Services;
        game = gameHost.Services.GetRequiredService<Game>();

        return gameHost;
    }
}

public class TestSystem : ISystem, IFirstSystem, ILastSystem
{
	public bool IsEnabled { get; set; } = true;

	public int InitializeCount { get; private set; } = 0;
	public int FirstCount { get; private set; } = 0;
	public int PreUpdateCount { get; private set; } = 0;
	public int FixedUpdateCount { get; private set; } = 0;
	public int UpdateCount { get; private set; } = 0;
	public int PostUpdateCount { get; private set; } = 0;
	public int PreRenderCount { get; private set; } = 0;
	public int RenderCount { get; private set; } = 0;
	public int PostRenderCount { get; private set; } = 0;
	public int LastCount { get; private set; } = 0;
	public int DestroyCount { get; private set; } = 0;

	public void Initialize() { InitializeCount++; }
	public void First(GameTime dt) { FirstCount++; }
	public void PreUpdate(GameTime dt) { PreUpdateCount++; }
	public void FixedUpdate(GameTime dt) { FixedUpdateCount++; }
	public void Update(GameTime dt) { UpdateCount++; }
	public void PostUpdate(GameTime dt) { PostUpdateCount++; }
	public void PreRender(GameTime dt) { PreRenderCount++; }
	public void Render(GameTime dt) { RenderCount++; }
	public void PostRender(GameTime dt) { PostRenderCount++; }
	public void Last(GameTime dt) { LastCount++; }
	public void Destroy() { DestroyCount++; }

	public void Reset()
	{
		InitializeCount = 0;
		FirstCount = 0;
		PreUpdateCount = 0;
		FixedUpdateCount = 0;
		UpdateCount = 0;
		PostUpdateCount = 0;
		PreRenderCount = 0;
		RenderCount = 0;
		PostRenderCount = 0;
		LastCount = 0;
		DestroyCount = 0;
	}
}
