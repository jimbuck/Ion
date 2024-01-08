using Kyber.Builder;
using Kyber.Graphics;
using Kyber.Utils;

using System.Diagnostics;

namespace Kyber;

public record struct GameExitEvent();

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
internal class GameLoop
{
	private bool _shouldExit;
	private readonly float _maxFrameTime = 0.1f; // 100ms

	private readonly IGameConfig _gameConfig;

	public GameTime GameTime { get; }
	public GameTime FixedGameTime { get; }

	public bool IsRunning { get; private set; } = false;

	public event EventHandler<EventArgs>? Exiting;

	public GameLoopDelegate Initialize { get; set; } = (dt) => { };
	public GameLoopDelegate First { get; set; } = (dt) => { };
	public GameLoopDelegate Update { get; set; } = (dt) => { };
	public GameLoopDelegate FixedUpdate { get; set; } = (dt) => { };
	public GameLoopDelegate Render { get; set; } = (dt) => { };
	public GameLoopDelegate Last { get; set; } = (dt) => { };
	public GameLoopDelegate Destroy { get; set; } = (dt) => { };

	public GameLoop(
		IGameConfig gameConfig
	) {
		_gameConfig = gameConfig;
		
		GameTime = new();
		FixedGameTime = new()
		{
			Alpha = 1,
			Delta = _gameConfig.MaxFPS == 0 ? (1f / 60f) : (1f / _gameConfig.MaxFPS)
		};
	}

	public void Run()
    {
		IsRunning = true;
		Initialize(GameTime);

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

			First(GameTime);
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
			Last(GameTime);

			var delayTime = targetFrameTime - (int)((stopwatch.Elapsed.TotalSeconds - currentTime) * 1000);
			if (delayTime > 0)
			{
				timer.Then("(idle)");
				Thread.Sleep(delayTime);
			}

			GameTime.Frame = FixedGameTime.Frame = (GameTime.Frame + 1);
		}

		MicroTimer.Export("./trace.json");

		Destroy(GameTime);
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

    public void Step(GameTime time)
    {
		using var timer = MicroTimer.Start("First");

		First(time);

		timer.Then("Update");

		FixedUpdate(time);
		Update(time);

		if (_shouldExit) return; //if (_shouldExit || _window.HasClosed) return;

		if (_gameConfig.Output != GraphicsOutput.None)
		{
			timer.Then("Render");
			Render(time);
		}

		timer.Then("Last");
		Last(time);
    }

	public void Exit()
    {
        _shouldExit = true;
    }
}
