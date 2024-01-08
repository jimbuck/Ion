using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ion.Utils;

public record struct MicroTiming(int Id, double Start, double Stop, int ThreadId)
{
	public double Duration => Stop - Start;
}

public interface IMicroTimerInstance : IDisposable
{
	void Then(string name);
}

public static class MicroTimer
{
	private static int _nextId = 0;
	private static readonly double _toMicroSeconds;

	private static readonly ConcurrentDictionary<int, string> _names = new();
	private static readonly ConcurrentBag<MicroTiming> _timings = new();

	static MicroTimer()
	{
		_toMicroSeconds = 1000000D / Stopwatch.Frequency;
	}

	public static IMicroTimerInstance Start(string name)
	{
		var id = Interlocked.Increment(ref _nextId);
		_names.TryAdd(id, name);
		return new MicroTimerInstance(id, Stopwatch.GetTimestamp() * _toMicroSeconds);
	}

	public static void Clear()
	{
		_timings.Clear();
		_names.Clear();
	}

	public static void Export(string path)
	{
		var traceExport = new TraceExport()
		{
			TraceEvents = _timings.Select(t => _names.TryGetValue(t.Id, out var name) ? new TraceEvent(name, t) : null).Where(t => t != null).ToList() as List<TraceEvent>,
			Metadata = new()
			{
				//{ "clock-offset-since-epoch", $"{_start}" },
			},
		};
		File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(traceExport));
	}

	private static void _stop(int id, double start, int threadId)
	{
		var stop = Stopwatch.GetTimestamp() * _toMicroSeconds;
		_timings.Add(new MicroTiming(id, start, stop, threadId));
	}

	private struct MicroTimerInstance : IMicroTimerInstance
	{
		private int _id;
		private double _start;
		private int _threadId;

		public MicroTimerInstance(int id, double start)
		{
			_id = id;
			_start = start;
			_threadId = Environment.CurrentManagedThreadId;
		}

		public void Then(string name)
		{
			_stop(_id, _start, _threadId);
			var newInstance = (MicroTimerInstance)Start(name);
			_id = newInstance._id;
			_start = newInstance._start;
			_threadId = newInstance._threadId;
		}

		public void Dispose()
		{
			_stop(_id, _start, _threadId);
		}
	}

	private class TraceEvent
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

		public TraceEvent(string name, MicroTiming timing)
		{
			Name = name;
			Timestamp = timing.Start;
			Duration = timing.Duration;
			ThreadId = timing.ThreadId;
		}
	}

	private class TraceExport
	{
		[JsonPropertyName("controllerTraceDataKey")]
		public string ControllerTraceDataKey { get; set; } = "systraceController";

		[JsonPropertyName("metadata")]
		public Dictionary<string, string> Metadata { get; set; } = new();

		[JsonPropertyName("traceEvents")]
		public List<TraceEvent> TraceEvents { get; set; } = new();
	}
}
