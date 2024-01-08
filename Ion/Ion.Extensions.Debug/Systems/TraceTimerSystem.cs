
using Microsoft.Extensions.Options;

namespace Ion.Extensions.Debug;

public class TraceTimerSystem
{
	private readonly IOptionsMonitor<DebugConfig> _debugConfig;
	private readonly TraceManager _traceManager;
	private readonly ITraceTimer _trace;

	public TraceTimerSystem(IOptionsMonitor<DebugConfig> debugConfig, ITraceManager traceManager)
	{
		_debugConfig = debugConfig;
		_traceManager = (TraceManager)traceManager;
		_trace = new TraceTimer(_traceManager, "GameLoop");
	}

#if DEBUG

	[Init]
	public GameLoopDelegate Init(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("Init");
		next(dt);
		timer.Stop();
	};

	[First]
	public GameLoopDelegate First(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("First");
		next(dt);
		timer.Stop();
	};

	[Update]
	public GameLoopDelegate Update(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("Update");
		next(dt);
		timer.Stop();
	};

	[Render]
	public GameLoopDelegate Render(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("Render");
		next(dt);
		timer.Stop();
	};

	[Last]
	public GameLoopDelegate Last(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("Last");
		next(dt);
		timer.Stop();
	};

	[Destroy]
	public GameLoopDelegate Destroy(GameLoopDelegate next) => dt =>
	{
		var timer = _trace.Start("Destroy");
		next(dt);
		timer.Stop();

		if (_debugConfig.CurrentValue.TraceEnabled) _traceManager.OutputTrace();
	};

#endif
}
