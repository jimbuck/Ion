using System.Diagnostics;

using Ion.Extensions.Debug;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ion.Core;

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
public class GameLoop(IOptionsMonitor<GameConfig> gameConfig, IEventListener events, ITraceTimer<GameLoop> trace)
{
	private bool _shouldExit;
	private readonly float _maxFrameTime = 0.1f; // 100ms
	private readonly ITraceTimer _trace = trace;

	private float MaxFPS => gameConfig.CurrentValue.MaxFPS < 1 ? 120 : gameConfig.CurrentValue.MaxFPS;

	public GameTime GameTime { get; } = new();
	public GameTime FixedGameTime { get; private set; } = new();

	public bool IsRunning { get; private set; } = false;

	public GameLoopDelegate Init { get; set; } = (dt) => { };
	public GameLoopDelegate First { get; set; } = (dt) => { };
	public GameLoopDelegate Update { get; set; } = (dt) => { };
	public GameLoopDelegate FixedUpdate { get; set; } = (dt) => { };
	public GameLoopDelegate Render { get; set; } = (dt) => { };
	public GameLoopDelegate Last { get; set; } = (dt) => { };
	public GameLoopDelegate Destroy { get; set; } = (dt) => { };

	public bool Rebuild { get; set; } = false;

	public MiddlewarePipelineBuilder InitBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder FirstBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder UpdateBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder FixedUpdateBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder RenderBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder LastBuilder { get; set; } = default!;
	public MiddlewarePipelineBuilder DestroyBuilder { get; set; } = default!;

	public void Build()
	{
		Init = InitBuilder.Build();
		First = FirstBuilder.Build();
		Update = UpdateBuilder.Build();
		FixedUpdate = FixedUpdateBuilder.Build();
		Render = RenderBuilder.Build();
		Last = LastBuilder.Build();
		Destroy = DestroyBuilder.Build();
	}

	public void Run()
    {
		IsRunning = true;

		FixedGameTime = new()
		{
			Alpha = 1,
			Delta = 1f / MaxFPS,
		};

		Init(GameTime);

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

			if (events.On<ExitGameEvent>()) _shouldExit = true;

			Last(GameTime);

			var delayTime = targetFrameTime - (int)((stopwatch.Elapsed.TotalSeconds - currentTime) * 1000);
			if (delayTime > 0)
			{
				var timer = _trace.Start("Idle");
				Thread.Sleep(delayTime);
				timer.Stop();
			}

			GameTime.Frame = FixedGameTime.Frame = (GameTime.Frame + 1);

			if (Rebuild)
			{
				Build();
				Rebuild = false;
			}
		}

		Destroy(GameTime);
		IsRunning = false;
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

	public void Stop()
	{
		_shouldExit = true;
	}
}
