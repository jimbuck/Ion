using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Debug;

internal class TraceManager : ITraceManager
{
	private readonly IOptionsMonitor<DebugConfig> _debugConfig;
	private readonly ILogger _logger;
	private readonly double _toMicroSeconds;

	private readonly ConcurrentBag<TraceTiming> _timings = new();

	private int _nextId = 0;

	private readonly ITraceTimerInstance _nullTimer = new NullTimerInstance();

	public bool IsEnabled { get; set; }

	public TraceManager(IOptionsMonitor<DebugConfig> debugConfig, ILogger<TraceManager> logger)
	{
		_debugConfig = debugConfig;
		_logger = logger;
		_toMicroSeconds = 1000000D / Stopwatch.Frequency;
		IsEnabled = _debugConfig.CurrentValue.TraceEnabled;
	}

	public void Start() {
		IsEnabled = true;
	}

	public void Stop()
	{
		IsEnabled = false;
	}

	public ITraceTimerInstance StartTraceTimer(string prefix, string name)
	{
#if !DEBUG
		return _nullTimer;
#endif

		if (!IsEnabled) return _nullTimer;

		return new TraceTimerInstance(this, prefix, Interlocked.Increment(ref _nextId), name, Stopwatch.GetTimestamp() * _toMicroSeconds);
	}

	internal void StopTraceTimer(int id, string name, double start, int threadId)
	{
		_timings.Add(new TraceTiming(id, name, start, Stopwatch.GetTimestamp() * _toMicroSeconds, threadId));
	}

	public void Clear()
	{
		_timings.Clear();
	}

	public void OutputTrace()
	{
		var traceOutputPath = _debugConfig.CurrentValue.TraceOutput;

		if (string.IsNullOrWhiteSpace(traceOutputPath)) return;

		File.WriteAllBytes(traceOutputPath, JsonSerializer.SerializeToUtf8Bytes(new TraceExport()
		{
			TraceEvents = _timings.Select(t => new TraceEvent(t)).Where(t => t != null).ToList(),
			Metadata = new()
			{
				//{ "clock-offset-since-epoch", $"{_start}" },
			},
		}, TraceExportJsonContext.Default.TraceExport));
		_logger.LogDebug("Ion Trace output to {traceOutput}", traceOutputPath);
	}
}

internal partial class TraceEvent
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("tid")]
	public long ThreadId { get; set; }

	[JsonPropertyName("ts")]
	public double Timestamp { get; set; }

	[JsonPropertyName(name: "dur")]
	public double Duration { get; set; }

	[JsonPropertyName("ph")]
	public string Phase { get; set; } = "X";

	//[JsonPropertyName("cat")]
	//public string? Categories { get; set; }

	//[JsonPropertyName("args")]
	//public Dictionary<string, object>? Args { get; set; } = new();

	public TraceEvent(TraceTiming timing)
	{
		Name = timing.Name;
		Timestamp = timing.Start;
		Duration = timing.Duration;
		ThreadId = timing.ThreadId;
	}
}

internal partial class TraceExport
{
	[JsonPropertyName("controllerTraceDataKey")]
	public string ControllerTraceDataKey { get; set; } = "systraceController";

	[JsonPropertyName("metadata")]
	public Dictionary<string, string> Metadata { get; set; } = new();

	[JsonPropertyName("traceEvents")]
	public List<TraceEvent> TraceEvents { get; set; } = new();
}

[JsonSerializable(typeof(TraceExport))]
internal partial class TraceExportJsonContext : JsonSerializerContext { }
