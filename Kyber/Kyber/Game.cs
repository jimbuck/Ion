using Kyber.Assets;
using Kyber.Graphics;
using Kyber.Utils;

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
	private readonly Window _window;
	private readonly GraphicsDevice _graphics;
	private readonly SpriteRenderer _spriteRenderer;
	private readonly AssetManager _assets;
	private readonly InputState _input;
	private readonly EventEmitter _eventEmitter;
	private readonly IEventListener _events;

	public SystemGroup Systems { get; }

	public bool IsRunning { get; private set; } = false;

	public event EventHandler<EventArgs>? Exiting;

	public Game(
		IGameConfig gameConfig,
		IWindow window,
		IGraphicsDevice graphics,
		ISpriteRenderer spriteRenderer,
		IAssetManager assets,
		IInputState input,
		IEventEmitter eventEmitter,
		IEventListener events,
		SystemGroup systems)
	{
		_gameConfig = gameConfig;
		_window = (Window)window;
		_graphics = (GraphicsDevice)graphics;
		_spriteRenderer = (SpriteRenderer)spriteRenderer;
		_assets = (AssetManager)assets;
		_input = (InputState)input;
		_eventEmitter = (EventEmitter)eventEmitter;
		_events = events;
		
		Systems = systems;
	}

	public void Initialize()
	{
		using var _ = MicroTimer.Start("Game.Initialize", 1);
		_window.Initialize();
		_graphics.Initialize();
		_assets.Initialize();
		_spriteRenderer.Initialize();
		Systems.Initialize();

		_graphics.UpdateProjection((uint)_window.Width, (uint)_window.Height);
	}

	public void First(float dt)
	{
		using var _ = MicroTimer.Start("Game.First");
		_window.Step();
		_input.Step();
		if (_events.OnLatest<WindowResizeEvent>(out var e)) _graphics.UpdateProjection(e.Data.Width, e.Data.Height);
		Systems.First(dt);
	}

	public void PreUpdate(float dt)
	{
		using var _ = MicroTimer.Start("Game.PreUpdate");
		_eventEmitter.Step();
		Systems.PreUpdate(dt);
	}

	public void Update(float dt)
	{
		using var _ = MicroTimer.Start("Game.Update");
		Systems.Update(dt);
	}

	public void PostUpdate(float dt)
	{
		using var _ = MicroTimer.Start("Game.PostUpdate");
		Systems.PostUpdate(dt);
		if (_events.On<WindowClosedEvent>() || _events.On<GameExitEvent>()) Exit();
	}

	public void PreRender(float dt)
	{
		using var _ = MicroTimer.Start("Game.PreRender");
		_graphics.BeginFrame(dt);
		_spriteRenderer.Begin(dt);
		Systems.PreRender(dt);
	}

	public void Render(float dt)
	{
		using var _ = MicroTimer.Start("Game.Render");
		Systems.Render(dt);
	}

	public void PostRender(float dt)
	{
		using var _ = MicroTimer.Start("Game.PostRender");
		Systems.PostRender(dt);
		_spriteRenderer.End();
		_graphics.EndFrame(dt);
	}

	public void Last(float dt)
	{
		using var _ = MicroTimer.Start("Game.Last");
		Systems.Last(dt);
	}

	public void Destroy()
	{
		using var _ = MicroTimer.Start("Game.Destroy", 1);
		Systems.Destroy();
	}

	public void Run()
    {
		IsRunning = true;
		Initialize();

        var stopwatch = Stopwatch.StartNew();

		var currentTime = stopwatch.Elapsed.TotalSeconds;
		const double maxFrameTime = 0.25;
		double t = 0;
		double dt = _gameConfig.MaxFPS == 0 ? (1f / 60f) : (1f / _gameConfig.MaxFPS);
		double accumulator = 0;

		while (_shouldExit == false)
        {
			var newTime = stopwatch.Elapsed.TotalSeconds;
			var frameTime = newTime - currentTime;

			if (frameTime > maxFrameTime) frameTime = maxFrameTime;
			currentTime = newTime;

			accumulator += frameTime;

			First((float)frameTime);

			while (accumulator >= dt)
			{
				UpdateStep((float)dt);
				t += dt;
				accumulator -= dt;
			}

			// TODO: Figure out how/where to use this alpha.
			var alpha = accumulator / dt;

			if (_gameConfig.Output != GraphicsOutput.None) RenderStep((float)frameTime);
			Last((float)frameTime);
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
		PreUpdate(dt);
		Update(dt);
		PostUpdate(dt);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RenderStep(float dt)
	{
		if (_shouldExit || _window.HasClosed) return;

		PreRender(dt);
		Render(dt);
		PostRender(dt);
	}

	public void Exit()
    {
        _shouldExit = true;
    }
}
