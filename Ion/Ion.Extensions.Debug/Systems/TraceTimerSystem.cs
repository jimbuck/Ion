using Microsoft.Extensions.Options;

namespace Ion.Extensions.Debug;

/// <summary>
/// Times every stage (except FixedUpdate) with a <c>GameLoop::{Stage}</c> trace timer: a scope at order
/// <see cref="StageOrder.Trace"/>, the outermost of every stage. Only in Debug builds of the package. At the end of Destroy
/// it writes the trace when <see cref="DebugConfig.TraceEnabled"/> is set.
/// </summary>
public class TraceTimerSystem
{
	private readonly IOptionsMonitor<DebugConfig> _debugConfig;
	private readonly ITraceManager _traceManager;
	private readonly ITraceTimer _trace;

	public TraceTimerSystem(IOptionsMonitor<DebugConfig> debugConfig, ITraceManager traceManager)
	{
		_debugConfig = debugConfig;
		_traceManager = traceManager;
		_trace = traceManager.CreateTimer("GameLoop");
	}

#if DEBUG
	private ITraceTimerInstance? _init, _first, _update, _render, _last, _destroy;

	[Begin(Stage.Init, Order = StageOrder.Trace)] public void BeginInit(GameTime dt) => _init = _trace.Start("Init");
	[End(Stage.Init, Order = StageOrder.Trace)] public void EndInit(GameTime dt) => _init?.Stop();

	[Begin(Stage.First, Order = StageOrder.Trace)] public void BeginFirst(GameTime dt) => _first = _trace.Start("First");
	[End(Stage.First, Order = StageOrder.Trace)] public void EndFirst(GameTime dt) => _first?.Stop();

	[Begin(Stage.Update, Order = StageOrder.Trace)] public void BeginUpdate(GameTime dt) => _update = _trace.Start("Update");
	[End(Stage.Update, Order = StageOrder.Trace)] public void EndUpdate(GameTime dt) => _update?.Stop();

	[Begin(Stage.Render, Order = StageOrder.Trace)] public void BeginRender(GameTime dt) => _render = _trace.Start("Render");
	[End(Stage.Render, Order = StageOrder.Trace)] public void EndRender(GameTime dt) => _render?.Stop();

	[Begin(Stage.Last, Order = StageOrder.Trace)] public void BeginLast(GameTime dt) => _last = _trace.Start("Last");
	[End(Stage.Last, Order = StageOrder.Trace)] public void EndLast(GameTime dt) => _last?.Stop();

	[Begin(Stage.Destroy, Order = StageOrder.Trace)] public void BeginDestroy(GameTime dt) => _destroy = _trace.Start("Destroy");

	[End(Stage.Destroy, Order = StageOrder.Trace)]
	public void EndDestroy(GameTime dt)
	{
		_destroy?.Stop();

		if (_debugConfig.CurrentValue.TraceEnabled) _traceManager.OutputTrace();
	}
#endif
}
