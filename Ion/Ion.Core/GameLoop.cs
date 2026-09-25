using Ion.Extensions.Debug;

using Microsoft.Extensions.Options;

namespace Ion.Core;

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
/// <remarks>
/// <para>
/// Each frame (<see cref="Step()"/>) reads the frame start time from the injected <see cref="IClock"/>, clamps the frame
/// duration to <see cref="GameConfig.MaxFrameTime"/>, adds it to an accumulator and runs <see cref="FixedUpdate"/> once
/// per whole fixed step (<c>1 / </c><see cref="GameConfig.FixedUpdateRate"/> seconds) that fits, carrying the remainder
/// to the next frame. <see cref="GameTime.Alpha"/> is that remainder divided by the fixed step. Then
/// <see cref="Update"/>, <see cref="Render"/> and <see cref="Last"/> run once with the variable <see cref="GameTime"/>.
/// </para>
/// <para>
/// Frame pacing only limits rendering: unless <see cref="GameConfig.VSync"/> is set or <see cref="GameConfig.MaxFPS"/>
/// is below 1, the loop asks the clock to <see cref="IClock.Sleep"/> for what is left of <c>1 / MaxFPS</c> seconds.
/// </para>
/// <para>
/// Before invoking each stage the loop sets <see cref="ILoopContext.Stage"/> on <see cref="Context"/> (and increments
/// <see cref="ILoopContext.FixedStepCount"/> before each fixed step), so engine services can tell fixed-step consumers
/// from per-frame ones. This is how an input edge or an event that happens on a frame without a fixed step still reaches
/// the next fixed step exactly once.
/// </para>
/// </remarks>
public class GameLoop
{
	// Tolerance for floating point drift when comparing the accumulator against the fixed step (well below the 100 ns
	// resolution of TimeSpan, so it never produces an extra step for real frame times).
	private const double AccumulatorEpsilon = 1e-9;

	private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly IEventListener _events;
	private readonly ITraceTimer _trace;
	private readonly IClock _clock;
	private readonly GameLoopContext _context;

	private volatile bool _shouldExit;
	private TimeSpan _frameStart;
	private double _accumulator;

	/// <summary>
	/// Creates a game loop. Normally created by <see cref="IonApplication.Build"/>.
	/// </summary>
	/// <param name="gameConfig">The game configuration (fixed-step rate, frame time clamp, pacing).</param>
	/// <param name="events">The listener used to detect <see cref="ExitGameEvent"/>.</param>
	/// <param name="trace">The trace timer used to record idle time.</param>
	/// <param name="clock">The time source for every frame.</param>
	/// <param name="context">
	/// The loop context to keep up to date (the application's singleton <see cref="GameLoopContext"/>). When omitted the loop
	/// uses a private one, which only this loop's <see cref="Context"/> exposes.
	/// </param>
	public GameLoop(IOptionsMonitor<GameConfig> gameConfig, IEventListener events, ITraceTimer<GameLoop> trace, IClock clock, GameLoopContext? context = null)
	{
		_gameConfig = gameConfig;
		_events = events;
		_trace = trace;
		_clock = clock;
		_context = context ?? new GameLoopContext();

		_frameStart = clock.Elapsed;
		FixedGameTime.Alpha = 1;
		FixedGameTime.Delta = (float)GetFixedStepSeconds(gameConfig.CurrentValue);
	}

	/// <summary>
	/// The clock that drives this loop.
	/// </summary>
	public IClock Clock => _clock;

	/// <summary>
	/// Where the loop is: the running stage, the frame and the number of fixed steps started. Set before each stage runs.
	/// </summary>
	public ILoopContext Context => _context;

	/// <summary>
	/// The variable-rate time passed to First, Update, Render and Last.
	/// </summary>
	public GameTime GameTime { get; } = new();

	/// <summary>
	/// The fixed-rate time passed to FixedUpdate. <see cref="GameTime.Delta"/> is always the fixed step and
	/// <see cref="GameTime.Elapsed"/> is the simulated time (the sum of all fixed steps run so far).
	/// </summary>
	public GameTime FixedGameTime { get; } = new();

	/// <summary>
	/// Whether <see cref="Run"/> or <see cref="RunFrames"/> is currently executing.
	/// </summary>
	public bool IsRunning { get; private set; } = false;

	/// <summary>
	/// The time carried over to the next frame by the fixed-step accumulator, in seconds.
	/// </summary>
	internal double Accumulator => _accumulator;

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

	/// <summary>
	/// Runs Init, then frames until <see cref="Stop"/> is called, an <see cref="ExitGameEvent"/> is received or
	/// <paramref name="cancellationToken"/> is cancelled, then Destroy.
	/// </summary>
	/// <param name="cancellationToken">Stops the loop (after the current frame) when cancelled.</param>
	/// <exception cref="InvalidOperationException">The loop is already running.</exception>
	public void Run(CancellationToken cancellationToken = default)
	{
		RunCore(long.MaxValue, cancellationToken);
	}

	/// <summary>
	/// Runs Init, then at most <paramref name="frames"/> frames (fewer if the loop is stopped or an
	/// <see cref="ExitGameEvent"/> is received), then Destroy.
	/// </summary>
	/// <param name="frames">The number of frames to run. Must not be negative.</param>
	/// <exception cref="InvalidOperationException">The loop is already running.</exception>
	public void RunFrames(int frames)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		RunCore(frames, CancellationToken.None);
	}

	private void RunCore(long maxFrames, CancellationToken cancellationToken)
	{
		if (IsRunning) throw new InvalidOperationException("The game loop is already running.");

		IsRunning = true;
		_shouldExit = false;

		try
		{
			Initialize();

			using var timerResolution = _gameConfig.CurrentValue.VSync ? default : WindowsTimerResolution.Begin();

			for (long frame = 0; frame < maxFrames && !_shouldExit && !cancellationToken.IsCancellationRequested; frame++)
			{
				Step();
			}

			Shutdown();
		}
		finally
		{
			_context.Stage = GameLoopStage.None;
			IsRunning = false;
		}
	}

	/// <summary>
	/// Runs the Init stage and restarts frame timing, so that loading time does not count as the first frame. Called by
	/// <see cref="Run"/> and <see cref="RunFrames"/>; call it directly only when driving the loop with <see cref="Step()"/>
	/// (as test hosts do), once, before the first frame.
	/// </summary>
	public void Initialize()
	{
		_shouldExit = false;
		_context.Frame = GameTime.Frame;
		_context.Stage = GameLoopStage.Init;
		try
		{
			Init(GameTime);
		}
		finally
		{
			_context.Stage = GameLoopStage.None;
		}

		_frameStart = _clock.Elapsed;
	}

	/// <summary>
	/// Runs the Destroy stage. Called by <see cref="Run"/> and <see cref="RunFrames"/> after the last frame; call it
	/// directly only when driving the loop with <see cref="Step()"/>, once, after the last frame.
	/// </summary>
	public void Shutdown()
	{
		_context.Stage = GameLoopStage.Destroy;
		try
		{
			Destroy(GameTime);
		}
		finally
		{
			_context.Stage = GameLoopStage.None;
		}
	}

	/// <summary>
	/// Whether the loop has been asked to exit, by <see cref="Stop"/> or an <see cref="ExitGameEvent"/>. <see cref="Run"/>
	/// and <see cref="RunFrames"/> return after the frame in which this becomes true; <see cref="Step()"/> ignores it.
	/// </summary>
	public bool IsExitRequested => _shouldExit;

	/// <summary>
	/// Runs one complete, timed frame: First, as many FixedUpdate steps as the accumulated time allows, Update, Render,
	/// Last, then frame pacing. Does not run Init or Destroy. Time is measured from the previous frame (or from when the
	/// loop was created or started) using <see cref="Clock"/>.
	/// </summary>
	public void Step()
	{
		var config = _gameConfig.CurrentValue;
		var fixedStep = GetFixedStepSeconds(config);
		var maxFrameTime = config.MaxFrameTime > TimeSpan.Zero ? config.MaxFrameTime : GameConfig.DefaultMaxFrameTime;

		var frameStart = _clock.NextFrame();
		var delta = frameStart - _frameStart;
		if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
		if (delta > maxFrameTime) delta = maxFrameTime;
		_frameStart = frameStart;

		GameTime.Elapsed = frameStart;
		GameTime.Delta = (float)delta.TotalSeconds;
		GameTime.Alpha = 1;
		FixedGameTime.Delta = (float)fixedStep;
		FixedGameTime.Alpha = 1;

		_accumulator += delta.TotalSeconds;

		var context = _context;
		context.Frame = GameTime.Frame;

		context.Stage = GameLoopStage.First;
		First(GameTime);

		while (_accumulator + AccumulatorEpsilon >= fixedStep)
		{
			FixedGameTime.Elapsed += TimeSpan.FromSeconds(fixedStep);
			context.FixedStepCount++;
			context.Stage = GameLoopStage.FixedUpdate;
			FixedUpdate(FixedGameTime);
			_accumulator -= fixedStep;
		}

		if (_accumulator < 0) _accumulator = 0;
		GameTime.Alpha = (float)(_accumulator / fixedStep);

		context.Stage = GameLoopStage.Update;
		Update(GameTime);

		context.Stage = GameLoopStage.Render;
		Render(GameTime);

		if (_events.On<ExitGameEvent>()) _shouldExit = true;

		context.Stage = GameLoopStage.Last;
		Last(GameTime);

		context.Stage = GameLoopStage.None;

		Pace(config, frameStart);

		GameTime.Frame = FixedGameTime.Frame = GameTime.Frame + 1;
		context.Frame = GameTime.Frame;

		if (Rebuild)
		{
			Build();
			Rebuild = false;
		}
	}

	/// <summary>
	/// Runs one untimed pass of every per-frame stage with the given time: First, FixedUpdate (once), Update, Render
	/// and Last. Render and Last are skipped once the loop has been asked to stop. No clock, accumulator or pacing is
	/// involved, which makes it the cheapest way to drive systems from tests and benchmarks.
	/// </summary>
	/// <param name="time">The time passed to every stage.</param>
	public void Step(GameTime time)
	{
		var context = _context;
		context.Frame = time.Frame;

		context.Stage = GameLoopStage.First;
		First(time);

		context.FixedStepCount++;
		context.Stage = GameLoopStage.FixedUpdate;
		FixedUpdate(time);

		context.Stage = GameLoopStage.Update;
		Update(time);

		if (!_shouldExit)
		{
			context.Stage = GameLoopStage.Render;
			Render(time);

			context.Stage = GameLoopStage.Last;
			Last(time);
		}

		context.Stage = GameLoopStage.None;
	}

	/// <summary>
	/// Asks the loop to exit after the current frame.
	/// </summary>
	public void Stop()
	{
		_shouldExit = true;
	}

	private void Pace(GameConfig config, TimeSpan frameStart)
	{
		if (config.VSync || config.MaxFPS < 1) return;

		var targetFrameTime = TimeSpan.FromSeconds(1.0 / config.MaxFPS);
		var remaining = targetFrameTime - (_clock.Elapsed - frameStart);
		if (remaining <= TimeSpan.Zero) return;

		var timer = _trace.Start("Idle");
		_clock.Sleep(remaining);
		timer.Stop();
	}

	private static double GetFixedStepSeconds(GameConfig config)
	{
		var rate = config.FixedUpdateRate > 0 && double.IsFinite(config.FixedUpdateRate) ? config.FixedUpdateRate : GameConfig.DefaultFixedUpdateRate;
		return 1.0 / rate;
	}
}
