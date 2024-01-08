
namespace Ion.Tests;

public class GameTests
{
    [Fact, Trait(CATEGORY, INTEGRATION)]
    public void GameLoopStages()
    {
		var dt = new GameTime()
		{
			Frame = 0,
			Delta = 0.01f,
			Alpha = 1f,
			Elapsed = TimeSpan.Zero,
		};
		using var _ = SetupWithSystems(out var services, out var game, typeof(TestSystem));

        var testSystem = services.GetRequiredService<TestSystem>();

        var gameLoop = game.Build();

        gameLoop.Init(dt);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(0, testSystem.UpdateCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

		gameLoop.Step(dt);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(1, testSystem.UpdateCount);
        Assert.Equal(1, testSystem.RenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

		gameLoop.Step(dt);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.RenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

		gameLoop.Destroy(dt);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.RenderCount);
        Assert.Equal(1, testSystem.DestroyCount);
    }
}
