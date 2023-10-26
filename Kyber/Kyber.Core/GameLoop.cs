using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Kyber.Core;

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
internal class GameLoop
{
	private bool _shouldExit;
	private readonly float _maxFrameTime = 0.1f; // 100ms

	private readonly IOptionsMonitor<GameConfig> _gameConfig;

	private float MaxFPS => _gameConfig.CurrentValue.MaxFPS < 1 ? 120 : _gameConfig.CurrentValue.MaxFPS;

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

	public GameLoop(IOptionsMonitor<GameConfig> gameConfig) {
		_gameConfig = gameConfig;
		
		GameTime = new();
		FixedGameTime = new()
		{
			Alpha = 1,
			Delta = 1f / MaxFPS,
		};
	}

	public void Run()
    {
		IsRunning = true;
		Initialize(GameTime);

        var stopwatch = Stopwatch.StartNew();

		var targetFrameTime = (int)(1000 / MaxFPS);
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

			while (accumulator >= FixedGameTime.Delta)
			{
				FixedUpdate(FixedGameTime);
				accumulator -= FixedGameTime.Delta;
			}

			GameTime.Alpha = accumulator / FixedGameTime.Delta;

			Update(GameTime);

			Render(GameTime);
		
			Last(GameTime);

			var delayTime = targetFrameTime - (int)((stopwatch.Elapsed.TotalSeconds - currentTime) * 1000);
			if (delayTime > 0)
			{
				Thread.Sleep(delayTime);
			}

			GameTime.Frame = FixedGameTime.Frame = (GameTime.Frame + 1);
		}

		Destroy(GameTime);
		IsRunning = false;

		Exiting?.Invoke(this, EventArgs.Empty);
	}

    public void Step(GameTime time)
    {
		First(time);

		FixedUpdate(time);
		Update(time);

		if (_shouldExit) return;

		Render(time);

		Last(time);
    }

	public void Exit()
    {
        _shouldExit = true;
    }
}
