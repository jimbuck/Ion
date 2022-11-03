﻿using System.Diagnostics;
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
	private readonly IWindow _window;

	private readonly Stopwatch _updateStopwatch = new();
	private readonly Stopwatch _renderStopwatch = new();

	public SystemGroup Systems { get; }

	public bool IsRunning { get; private set; } = false;

	public event EventHandler<EventArgs>? Exiting;

	public Game(IGameConfig gameConfig, SystemGroup systems, IWindow window)
	{
		_gameConfig = gameConfig;
		Systems = systems;
		_window = window;
	}

	public void Initialize() => Systems.Initialize();

	public void First(float dt) => Systems.First(dt);

	public void PreUpdate(float dt) => Systems.PreUpdate(dt);

	public void Update(float dt) => Systems.Update(dt);

	public void PostUpdate(float dt) => Systems.PostUpdate(dt);

    public void PreRender(float dt) => Systems.PreRender(dt);

    public void Render(float dt) => Systems.Render(dt);

    public void PostRender(float dt) => Systems.PostRender(dt);

	public void Last(float dt) => Systems.Last(dt);

	public void Destroy() => Systems.Destroy();

	public void Run(bool render = true)
    {
		IsRunning = true;
		Initialize();

        var stopwatch = Stopwatch.StartNew();

		var targetFrameTime = _gameConfig.MaxFPS == 0 ? 0f : 1000f / _gameConfig.MaxFPS;

		while (_shouldExit == false)
        {
            var dt = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
			First(dt);
			UpdateStep(dt);
			if (render) RenderStep(dt);
			Last(dt);
			//var timeDiff = (int)(targetFrameTime - dt);
			//if (timeDiff > 0) Thread.Sleep(timeDiff);
		}

		Destroy();
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step(float dt)
    {
		First(dt);
		UpdateStep(dt);
		RenderStep(dt);
		Last(dt);
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateStep(float dt)
	{
		_updateStopwatch.Restart();
		PreUpdate(dt);
		Update(dt);
		PostUpdate(dt);
		_updateStopwatch.Stop();
		// TODO: Emit/Store Update time.
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RenderStep(float dt)
	{
		if (_shouldExit || _window.HasClosed) return;

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
