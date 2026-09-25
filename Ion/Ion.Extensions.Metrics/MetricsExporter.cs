using System.Diagnostics;
using System.Text.Json;

namespace Ion.Extensions.Metrics;

/// <summary>
/// Writes profiled frames in the Chrome trace event format (JSON object form), which Perfetto (<c>ui.perfetto.dev</c>)
/// and <c>chrome://tracing</c> open. Span names are resolved from their ids here, at export time.
/// </summary>
/// <remarks>
/// Every frame becomes a complete event (<c>"ph":"X"</c>, category <c>frame</c>) on the loop thread with its
/// <see cref="FrameStats"/> as arguments, plus a counter event (<c>"ph":"C"</c>, name <c>stats</c>) so the counters show
/// as tracks; every span becomes a complete event (category <c>span</c>) on the thread that recorded it. Timestamps are in
/// microseconds from the start of the first frame written.
/// </remarks>
public static class MetricsExporter
{
	private static readonly JsonEncodedText TraceEvents = JsonEncodedText.Encode("traceEvents");
	private static readonly JsonEncodedText Name = JsonEncodedText.Encode("name");
	private static readonly JsonEncodedText Cat = JsonEncodedText.Encode("cat");
	private static readonly JsonEncodedText Ph = JsonEncodedText.Encode("ph");
	private static readonly JsonEncodedText Ts = JsonEncodedText.Encode("ts");
	private static readonly JsonEncodedText Dur = JsonEncodedText.Encode("dur");
	private static readonly JsonEncodedText Pid = JsonEncodedText.Encode("pid");
	private static readonly JsonEncodedText Tid = JsonEncodedText.Encode("tid");
	private static readonly JsonEncodedText Args = JsonEncodedText.Encode("args");

	/// <summary>
	/// Writes <paramref name="frames"/> (oldest first, for example from <see cref="FrameProfiler.CopyFrames"/>) to
	/// <paramref name="path"/>, creating its directory. Returns the number of trace events written.
	/// </summary>
	public static int WriteChromeTrace(string path, IReadOnlyList<FrameProfile> frames)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentNullException.ThrowIfNull(frames);

		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		return WriteChromeTrace(stream, frames);
	}

	/// <summary>Writes <paramref name="frames"/> to <paramref name="stream"/>. Returns the number of trace events written.</summary>
	public static int WriteChromeTrace(Stream stream, IReadOnlyList<FrameProfile> frames)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(frames);

		using var json = new Utf8JsonWriter(stream);
		var origin = frames.Count > 0 ? frames[0].Start : 0;
		var events = 0;

		json.WriteStartObject();
		json.WriteString("displayTimeUnit", "ms");
		json.WriteStartObject("otherData");
		json.WriteString("generator", "Ion.Extensions.Metrics");
		json.WriteNumber("frames", frames.Count);
		json.WriteEndObject();
		json.WritePropertyName(TraceEvents);
		json.WriteStartArray();

		// Metadata: the process and every thread that recorded something.
		Metadata(json, "process_name", 0, "Ion");
		events++;

		var threads = new HashSet<int>();
		var loopThreads = new HashSet<int>();
		foreach (var frame in frames)
		{
			loopThreads.Add(frame.ThreadId);
			threads.Add(frame.ThreadId);
			foreach (ref readonly var span in frame.Spans) threads.Add(span.ThreadId);
		}

		foreach (var thread in threads.Order())
		{
			Metadata(json, "thread_name", thread, loopThreads.Contains(thread) ? $"Game loop ({thread})" : $"Thread {thread}");
			events++;
		}

		foreach (var frame in frames)
		{
			var name = frame.Kind switch
			{
				FrameKind.Init => "Init",
				FrameKind.Destroy => "Destroy",
				_ => $"Frame {frame.Frame}",
			};

			json.WriteStartObject();
			json.WriteString(Name, name);
			json.WriteString(Cat, "frame");
			json.WriteString(Ph, "X");
			json.WriteNumber(Ts, Micros(frame.Start - origin));
			json.WriteNumber(Dur, Micros(frame.End - frame.Start));
			json.WriteNumber(Pid, 1);
			json.WriteNumber(Tid, frame.ThreadId);
			json.WritePropertyName(Args);
			json.WriteStartObject();
			FrameLogWriter.WriteStats(json, frame.Stats);
			json.WriteEndObject();
			json.WriteEndObject();
			events++;

			if (frame.Kind == FrameKind.Frame)
			{
				ref readonly var stats = ref frame.Stats;
				json.WriteStartObject();
				json.WriteString(Name, "stats");
				json.WriteString(Ph, "C");
				json.WriteNumber(Ts, Micros(frame.Start - origin));
				json.WriteNumber(Pid, 1);
				json.WritePropertyName(Args);
				json.WriteStartObject();
				json.WriteNumber("frame_ms", Math.Round(stats.FrameMs, 3));
				json.WriteNumber("draw_calls", stats.DrawCalls);
				json.WriteNumber("sprites", stats.Sprites);
				json.WriteNumber("events_emitted", stats.EventsEmitted);
				json.WriteNumber("allocated_bytes", stats.AllocatedBytes);
				json.WriteEndObject();
				json.WriteEndObject();
				events++;
			}

			foreach (ref readonly var span in frame.Spans)
			{
				json.WriteStartObject();
				json.WriteString(Name, span.Id.Name);
				json.WriteString(Cat, "span");
				json.WriteString(Ph, "X");
				json.WriteNumber(Ts, Micros(span.Start - origin));
				json.WriteNumber(Dur, Micros(span.End - span.Start));
				json.WriteNumber(Pid, 1);
				json.WriteNumber(Tid, span.ThreadId);
				json.WriteEndObject();
				events++;
			}
		}

		json.WriteEndArray();
		json.WriteEndObject();
		json.Flush();
		return events;
	}

	private static void Metadata(Utf8JsonWriter json, string name, int thread, string value)
	{
		json.WriteStartObject();
		json.WriteString(Name, name);
		json.WriteString(Ph, "M");
		json.WriteNumber(Pid, 1);
		json.WriteNumber(Tid, thread);
		json.WritePropertyName(Args);
		json.WriteStartObject();
		json.WriteString(Name, value);
		json.WriteEndObject();
		json.WriteEndObject();
	}

	private static double Micros(long ticks) => Math.Round(ticks * 1_000_000.0 / Stopwatch.Frequency, 3);
}
