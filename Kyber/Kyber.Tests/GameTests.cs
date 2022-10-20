namespace Kyber.Tests;

public class GameTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public async Task Run_Exit()
    {
        using var _ = SetupWithSystems(GraphicsOutput.None, out var services, out var game);
        Assert.False(game.IsRunning);

        var gameTask = Task.Run(() => game.Run());
        await Task.Delay(50);
        Assert.True(game.IsRunning);

        game.Exit();
        await gameTask;
        Assert.False(game.IsRunning);
    }

    [Fact, Trait(CATEGORY, E2E)]
    public async Task Run_ExitOnWindowClose()
    {
        using var _ = SetupWithSystems(GraphicsOutput.Window, out var services, out var game);
        var window = services.GetRequiredService<Window>();
        Assert.False(game.IsRunning);

        var gameTask = Task.Run(() => game.Run());
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.True(game.IsRunning);

        window.Close();
        await gameTask;
        Assert.False(game.IsRunning);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void LifeCycle_NoGraphics()
    {
        using var _ = SetupWithSystems(GraphicsOutput.None, out var services, out var game, typeof(TestSystem));

        var testSystem = services.GetRequiredService<TestSystem>();

		game.Initialize();

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(0, testSystem.PreUpdateCount);
        Assert.Equal(0, testSystem.UpdateCount);
        Assert.Equal(0, testSystem.PostUpdateCount);
        Assert.Equal(0, testSystem.PreRenderCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Step(DT);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(1, testSystem.PreUpdateCount);
        Assert.Equal(1, testSystem.UpdateCount);
        Assert.Equal(1, testSystem.PostUpdateCount);
        Assert.Equal(0, testSystem.PreRenderCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Step(DT);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.PreUpdateCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.PostUpdateCount);
        Assert.Equal(0, testSystem.PreRenderCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Destroy();

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.PreUpdateCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.PostUpdateCount);
        Assert.Equal(0, testSystem.PreRenderCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.PostRenderCount);
        Assert.Equal(1, testSystem.DestroyCount);
    }

    [Fact, Trait(CATEGORY, E2E)]
    public void LifeCycle_Window()
    {
        using var _ = SetupWithSystems(GraphicsOutput.Window, out var services, out var game, typeof(TestSystem));

        var testSystem = services.GetRequiredService<TestSystem>();

		game.Initialize();

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(0, testSystem.PreUpdateCount);
        Assert.Equal(0, testSystem.UpdateCount);
        Assert.Equal(0, testSystem.PostUpdateCount);
        Assert.Equal(0, testSystem.PreRenderCount);
        Assert.Equal(0, testSystem.RenderCount);
        Assert.Equal(0, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Step(DT);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(1, testSystem.PreUpdateCount);
        Assert.Equal(1, testSystem.UpdateCount);
        Assert.Equal(1, testSystem.PostUpdateCount);
        Assert.Equal(1, testSystem.PreRenderCount);
        Assert.Equal(1, testSystem.RenderCount);
        Assert.Equal(1, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Step(DT);

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.PreUpdateCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.PostUpdateCount);
        Assert.Equal(2, testSystem.PreRenderCount);
        Assert.Equal(2, testSystem.RenderCount);
        Assert.Equal(2, testSystem.PostRenderCount);
        Assert.Equal(0, testSystem.DestroyCount);

        game.Destroy();

        Assert.Equal(1, testSystem.InitializeCount);
        Assert.Equal(2, testSystem.PreUpdateCount);
        Assert.Equal(2, testSystem.UpdateCount);
        Assert.Equal(2, testSystem.PostUpdateCount);
        Assert.Equal(2, testSystem.PreRenderCount);
        Assert.Equal(2, testSystem.RenderCount);
        Assert.Equal(2, testSystem.PostRenderCount);
        Assert.Equal(1, testSystem.DestroyCount);
    }
}
