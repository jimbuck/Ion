using Kyber.Assets;
using Kyber.Graphics;
using Kyber.Storage;
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
	private readonly float _maxFrameTime = 0.25f;

	private readonly IGameConfig _gameConfig;
	private readonly Window _window;
	private readonly GraphicsContext _graphics;
	private readonly SpriteRenderer _spriteRenderer;
	private readonly AssetManager _assets;
	private readonly InputState _input;
	private readonly EventEmitter _eventEmitter;
	private readonly PersistentStorage _storage;
	private readonly IEventListener _events;

	public GameTime GameTime { get; }
	public GameTime FixedGameTime { get; }
	public SystemGroup Systems { get; }

	public bool IsRunning { get; private set; } = false;

	public event EventHandler<EventArgs>? Exiting;

	public Game(
		IGameConfig gameConfig,
		IWindow window,
		IGraphicsContext graphics,
		ISpriteRenderer spriteRenderer,
		IAssetManager assets,
		IInputState input,
		IEventEmitter eventEmitter,
		IEventListener events,
		IPersistentStorage storage,
		SystemGroup systems)
	{
		_gameConfig = gameConfig;
		_window = (Window)window;
		_graphics = (GraphicsContext)graphics;
		_spriteRenderer = (SpriteRenderer)spriteRenderer;
		_assets = (AssetManager)assets;
		_input = (InputState)input;
		_eventEmitter = (EventEmitter)eventEmitter;
		_events = events;
		_storage = (PersistentStorage)storage;
		
		Systems = systems;
		GameTime = new();
		FixedGameTime = new()
		{
			Alpha = 1,
			Delta = _gameConfig.MaxFPS == 0 ? (1f / 60f) : (1f / _gameConfig.MaxFPS)
		};
	}

	public void Initialize()
	{
		using var _ = MicroTimer.Start("Game.Initialize", 1);
		_storage.Initialize();
		_window.Initialize();
		_graphics.Initialize();
		_assets.Initialize();
		_spriteRenderer.Initialize();
		Systems.Initialize();

		_graphics.UpdateProjection((uint)_window.Width, (uint)_window.Height);
	}

	public void First(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.First");
		_window.Step();
		_input.Step();
		if (_events.OnLatest<WindowResizeEvent>(out var e)) _graphics.UpdateProjection(e.Data.Width, e.Data.Height);
		Systems.First(time);
	}

	public void PreUpdate(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.PreUpdate");
		_eventEmitter.Step();
		Systems.PreUpdate(time);
	}

	public void FixedUpdate(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.FixedUpdate");
		Systems.FixedUpdate(time);
	}

	public void Update(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.Update");
		Systems.Update(time);
	}

	public void PostUpdate(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.PostUpdate");
		Systems.PostUpdate(time);
		if (_events.On<WindowClosedEvent>() || _events.On<GameExitEvent>()) Exit();
	}

	public void PreRender(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.PreRender");
		_graphics.BeginFrame(time);
		_spriteRenderer.Begin(time);
		Systems.PreRender(time);
	}

	public void Render(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.Render");
		Systems.Render(time);
	}

	public void PostRender(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.PostRender");
		Systems.PostRender(time);
		_spriteRenderer.End();
		_graphics.EndFrame(time);
	}

	public void Last(GameTime time)
	{
		using var _ = MicroTimer.Start("Game.Last");
		Systems.Last(time);
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
		float accumulator = 0;

		while (_shouldExit == false)
        {
			GameTime.Alpha = FixedGameTime.Alpha = 1;
			GameTime.Elapsed = FixedGameTime.Elapsed = stopwatch.Elapsed;
			var newTime = GameTime.Elapsed.TotalSeconds;
			GameTime.Delta = (float)(newTime - currentTime);

			if (GameTime.Delta > _maxFrameTime) GameTime.Delta = _maxFrameTime;
			currentTime = newTime;

			accumulator += GameTime.Delta;

			First(GameTime);
			PreUpdate(GameTime);

			while (accumulator >= GameTime.Delta)
			{
				FixedUpdate(FixedGameTime);
				accumulator -= GameTime.Delta;
			}

			GameTime.Alpha = accumulator / FixedGameTime.Delta;

			Update(GameTime);
			PostUpdate(GameTime);

			if (_gameConfig.Output != GraphicsOutput.None) RenderStep(GameTime);
			Last(GameTime);

			GameTime.Frame = FixedGameTime.Frame = (GameTime.Frame + 1);
		}

		Destroy();
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step(GameTime time)
    {
		First(time);
		UpdateStep(time);
		RenderStep(time);
		Last(time);
    }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateStep(GameTime time)
	{
		PreUpdate(time);
		FixedUpdate(time);
		Update(time);
		PostUpdate(time);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RenderStep(GameTime time)
	{
		if (_shouldExit || _window.HasClosed) return;

		PreRender(time);
		Render(time);
		PostRender(time);
	}

	public void Exit()
    {
        _shouldExit = true;
    }
}
