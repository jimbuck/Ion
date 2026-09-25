using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ion.Extensions.Metrics;

/// <summary>
/// The <see cref="IMetrics"/> of an application: owns the game instruments, the frame log, the <c>Ion</c> meter and trace
/// captures, and listens to every frame of the <see cref="FrameProfiler"/>.
/// </summary>
internal sealed partial class MetricsService : IMetrics, IFrameListener, IDisposable
{
	private readonly FrameProfiler _profiler;
	private readonly IOptionsMonitor<MetricsConfig> _config;
	private readonly ILoopContext _loop;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, MetricsInstrument> _byName = new(StringComparer.Ordinal);
	private readonly List<MetricsInstrument> _instruments = [];
	private readonly List<MetricsHistogram> _histograms = [];
	private readonly List<FrameProfile> _export = [];
	private readonly FrameLogWriter? _frameLog;
	private readonly IonMeter? _meter;
	private readonly MetricsOverlay _overlay;

	private int _captureRemaining;
	private int _captureFrames;
	private uint _captureFirstFrame;
	private string? _capturePath;
	private bool _captureRestore;
	private bool _disposed;

	public MetricsService(FrameProfiler profiler, IOptionsMonitor<MetricsConfig> config, ILoopContext loop, MetricsOverlay overlay, ILogger<MetricsService> logger)
	{
		_profiler = profiler;
		_config = config;
		_loop = loop;
		_overlay = overlay;
		_logger = logger;

		var options = config.CurrentValue;
		if (options.Meter) _meter = new IonMeter();

		if (!string.IsNullOrWhiteSpace(options.FrameLog))
		{
			_frameLog = new FrameLogWriter(options.FrameLog, options.FrameLogFlushFrames);
			LogFrameLog(_logger, _frameLog.Path!);
		}

		profiler.IsActive = options.Profiling;
		profiler.AddListener(this);
	}

	public FrameProfiler Profiler => _profiler;

	public bool IsProfiling
	{
		get => _profiler.IsActive;
		set => _profiler.IsActive = value;
	}

	public FrameStats LastFrame => _profiler.LastFrame;

	public IReadOnlyList<MetricsInstrument> Instruments => _instruments;

	public bool IsCapturing => _captureRemaining > 0;

	public string? LastTracePath { get; private set; }

	/// <summary>The frame log, when <c>Ion:Metrics:FrameLog</c> is set.</summary>
	public FrameLogWriter? FrameLog => _frameLog;

	/// <summary>The <c>Ion</c> meter, when <c>Ion:Metrics:Meter</c> is on.</summary>
	public IonMeter? Meter => _meter;

	public SpanId Span(string name) => MetricsIds.Register(name);

	public MetricsScope Scope(SpanId span) => _profiler.Scope(span);

	public MetricsCounter Counter(string name, string? unit = null, string? description = null) =>
		Register(name, () => new MetricsCounter(name, unit, description));

	public MetricsGauge Gauge(string name, string? unit = null, string? description = null) =>
		Register(name, () => new MetricsGauge(name, unit, description));

	public MetricsHistogram Histogram(string name, string? unit = null, string? description = null) =>
		Register(name, () => new MetricsHistogram(name, unit, description));

	public void Capture(int frames, string? path = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);

		var target = string.IsNullOrWhiteSpace(path) ? _config.CurrentValue.TraceOutput : path;
		if (string.IsNullOrWhiteSpace(target))
		{
			LogNoTraceOutput(_logger);
			return;
		}

		if (!_profiler.CanRecord)
		{
			LogProfilingUnavailable(_logger);
			return;
		}

		frames = Math.Min(frames, _profiler.HistoryFrames);
		if (!IsCapturing) _captureRestore = _profiler.IsActive;

		// A frame in progress started without profiling: the capture starts with the next one.
		_captureFirstFrame = _loop.Stage == GameLoopStage.None ? _loop.Frame : _loop.Frame + 1;
		_captureFrames = frames;
		_captureRemaining = frames;
		_capturePath = target;
		_profiler.IsActive = true;
		LogCaptureStarted(_logger, frames, target);
	}

	public string? WriteTrace(string? path = null, int frames = int.MaxValue)
	{
		var target = string.IsNullOrWhiteSpace(path) ? _config.CurrentValue.TraceOutput : path;
		if (string.IsNullOrWhiteSpace(target)) return null;

		_export.Clear();
		_profiler.CopyFrames(_export, frames);
		var events = MetricsExporter.WriteChromeTrace(target, _export);
		_export.Clear();

		LastTracePath = Path.GetFullPath(target);
		LogTraceWritten(_logger, events, LastTracePath);
		return LastTracePath;
	}

	public void OnFrame(FrameProfile frame)
	{
		if (frame.Kind != FrameKind.Frame) return;

		ref readonly var stats = ref frame.Stats;
		_frameLog?.Write(stats, _instruments);
		_meter?.OnFrame(stats);
		_overlay.OnFrame(stats, _instruments);

		if (_captureRemaining > 0 && frame.Frame >= _captureFirstFrame && --_captureRemaining == 0)
		{
			try
			{
				WriteTrace(_capturePath, _captureFrames);
			}
			finally
			{
				_profiler.IsActive = _captureRestore;
				_capturePath = null;
			}
		}

		var histograms = _histograms;
		for (var i = 0; i < histograms.Count; i++) histograms[i].Reset();
	}

	/// <summary>Writes the trace at shutdown (when profiling is on) and flushes the frame log. Called by the Destroy step.</summary>
	public void Shutdown()
	{
		if (_profiler.IsActive && _profiler.Count > 0 && !string.IsNullOrWhiteSpace(_config.CurrentValue.TraceOutput)) WriteTrace();
		_frameLog?.Flush();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_profiler.RemoveListener(this);
		_frameLog?.Dispose();
		_meter?.Dispose();
	}

	private T Register<T>(string name, Func<T> create) where T : MetricsInstrument
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		lock (_lock)
		{
			if (_byName.TryGetValue(name, out var existing))
			{
				return existing as T ?? throw new InvalidOperationException($"Metric '{name}' is already registered as a {existing.Kind}.");
			}

			var instrument = create();
			_byName.Add(name, instrument);
			_instruments.Add(instrument);
			if (instrument is MetricsHistogram histogram) _histograms.Add(histogram);
			_meter?.Add(instrument);
			return instrument;
		}
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Metrics frame log: {Path}")]
	private static partial void LogFrameLog(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Capturing a trace of the next {Frames} frames to {Path}.")]
	private static partial void LogCaptureStarted(ILogger logger, int frames, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Wrote a Chrome trace ({Events} events) to {Path}; open it in https://ui.perfetto.dev.")]
	private static partial void LogTraceWritten(ILogger logger, int events, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Cannot capture a trace: Ion:Metrics:TraceOutput is empty and no path was given.")]
	private static partial void LogNoTraceOutput(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Cannot capture a trace: profiling is compiled out (IonMetricsProfiling=false) or Ion:Metrics:SpansPerFrame is 0.")]
	private static partial void LogProfilingUnavailable(ILogger logger);
}
