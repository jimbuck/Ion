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
	private readonly float _maxFrameTime = 0.1f; // 100ms

	private readonly IGameConfig _gameConfig;
	private readonly Window _window;
	private readonly GraphicsContext _graphicsContext;
	private readonly SpriteBatch _spriteBatch;
	private readonly AssetManager _assetManager;
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
		IGraphicsContext graphicsContext,
		ISpriteBatch spriteBatch,
		IAssetManager assetManager,
		IInputState input,
		IEventEmitter eventEmitter,
		IEventListener events,
		IPersistentStorage storage,
		SystemGroup systems)
	{
		_gameConfig = gameConfig;
		_window = (Window)window;
		_graphicsContext = (GraphicsContext)graphicsContext;
		_spriteBatch = (SpriteBatch)spriteBatch;
		_assetManager = (AssetManager)assetManager;
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
		using var _ = MicroTimer.Start("Initialize");
		_storage.Initialize();
		_window.Initialize();
		_graphicsContext.Initialize();
		_assetManager.Initialize();
		_spriteBatch.Initialize();
		Systems.Initialize();
	}

	//public void First(GameTime time)
	//{
	//	using var _ = MicroTimer.Start("First");
	//	_window.Step();
	//	_input.Step();
	//	_graphicsContext.First();

	//	//if (_events.OnLatest<WindowResizeEvent>(out var e)) _graphicsContext.UpdateProjection(e.Data.Width, e.Data.Height);
	//	Systems.First(time);
	//}
	public void FixedUpdate(GameTime time)
	{
		using var _ = MicroTimer.Start("FixedUpdate");
		Systems.FixedUpdate(time);
	}

	public void Update(GameTime time)
	{
		using var _ = MicroTimer.Start("Update");
		_window.Step();
		_input.Step();
		_graphicsContext.First();

		Systems.Update(time);

		if (_events.On<WindowClosedEvent>() || _events.On<GameExitEvent>()) Exit();
	}

	public void Render(GameTime time)
	{
		using var _ = MicroTimer.Start("Render");
		_graphicsContext.BeginFrame(time);
		_spriteBatch.Begin(time);
		
		Systems.Render(time);

		_spriteBatch.End();
		_graphicsContext.EndFrame(time);

		_eventEmitter.Step();
	}

	//public void Last(GameTime time)
	//{
	//	using var _ = MicroTimer.Start("Last");
	//	Systems.Last(time);
	//	_eventEmitter.Step();
	//}

	public void Destroy()
	{
		using var _ = MicroTimer.Start("Destroy");
		Systems.Destroy();
	}

	public void Run()
    {
		IsRunning = true;
		Initialize();

        var stopwatch = Stopwatch.StartNew();

		var targetFrameTime = (int)(1000 / (_gameConfig.MaxFPS == 0 ? 60 : _gameConfig.MaxFPS));
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

			using var timer = MicroTimer.Start("First");

			//First(GameTime);
			timer.Then("Update");


			while (accumulator >= FixedGameTime.Delta)
			{
				FixedUpdate(FixedGameTime);
				accumulator -= FixedGameTime.Delta;
			}

			GameTime.Alpha = accumulator / FixedGameTime.Delta;

			Update(GameTime);

			if (_gameConfig.Output != GraphicsOutput.None)
			{
				timer.Then("Render");
				Render(GameTime);
			}

			timer.Then("Last");
			//Last(GameTime);

			var delayTime = targetFrameTime - (int)((stopwatch.Elapsed.TotalSeconds - currentTime) * 1000);
			if (delayTime > 0)
			{
				timer.Then("(idle)");
				Thread.Sleep(delayTime);
			}

			GameTime.Frame = FixedGameTime.Frame = (GameTime.Frame + 1);
		}

		MicroTimer.Export("./trace.json");

		Destroy();
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step(GameTime time)
    {
		using var timer = MicroTimer.Start("First");

		//First(time);

		timer.Then("Update");

		FixedUpdate(time);
		Update(time);

		if (_shouldExit || _window.HasClosed) return;

		if (_gameConfig.Output != GraphicsOutput.None)
		{
			timer.Then("Render");
			Render(time);
		}

		timer.Then("Last");
		//Last(time);
    }

	public void Exit()
    {
        _shouldExit = true;
    }
}
