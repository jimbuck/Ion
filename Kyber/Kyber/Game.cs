using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kyber;

public record struct GameExitEvent();

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
internal class Game
{
	private bool _shouldExit;

	private readonly IGameConfig _gameConfig;

	private readonly Stopwatch _updateStopwatch = new();
	private readonly Stopwatch _renderStopwatch = new();

	public SystemGroup Systems { get; init; }

	public bool IsRunning { get; private set; } = false;

	public event EventHandler<EventArgs>? Exiting;

	public Game(IGameConfig gameConfig, SystemGroup systems)
	{
		_gameConfig = gameConfig;
		Systems = systems;
	}

	public void Initialize() => Systems.Initialize();

	public void PreUpdate(float dt) => Systems.PreUpdate(dt);

	public void Update(float dt) => Systems.Update(dt);

	public void PostUpdate(float dt) => Systems.PostUpdate(dt);

    public void PreRender(float dt) => Systems.PreRender(dt);

    public void Render(float dt) => Systems.Render(dt);

    public void PostRender(float dt) => Systems.PostRender(dt);

	public void Destroy() => Systems.Destroy();

	public void Run()
    {
		IsRunning = true;
		Initialize();

        var stopwatch = Stopwatch.StartNew();

        while (_shouldExit == false)
        {
            var dt = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            Step(dt);
        }

		Destroy();
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step(float dt)
    {
		UpdateStep(dt);
		RenderStep(dt);
    }

	public void RunNoRender()
	{
		IsRunning = true;
		Initialize();

		var stopwatch = Stopwatch.StartNew();

		while (_shouldExit == false)
		{
			var dt = (float)stopwatch.Elapsed.TotalSeconds;
			stopwatch.Restart();
			UpdateStep(dt);
		}

		Destroy();
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

	public void UpdateStep(float dt)
	{
		_updateStopwatch.Restart();
		PreUpdate(dt);
		Update(dt);
		PostUpdate(dt);
		_updateStopwatch.Stop();
		// TODO: Emit/Store Update time.
	}

	public void RenderStep(float dt)
	{
		_renderStopwatch.Start();
		PreRender(dt);
		Render(dt);
		PostRender(dt);
		_renderStopwatch.Stop();
		// TODO: Emit/Store Render time.
	}

	public void Exit()
    {
        _shouldExit = true;
    }
}
