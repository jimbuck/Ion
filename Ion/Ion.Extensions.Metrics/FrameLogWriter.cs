using System.Text.Json;

namespace Ion.Extensions.Metrics;

/// <summary>
/// Writes the frame log: one JSON object per line (JSON Lines) per frame, with the frame's <see cref="FrameStats"/> and
/// every game instrument. Property names are snake_case; game instruments are under <c>counters</c>, <c>gauges</c> and
/// <c>histograms</c>. Writing a line does not allocate once every instrument name has been seen.
/// </summary>
/// <example>
/// <code>{"frame":42,"delta_ms":16.667,"frame_ms":0.412,"idle_ms":0,"work_ms":0.412,"fps":2427.18,"fixed_steps":1,"draw_calls":103,"sprites":103,"triangles":206,"entities":0,"events_emitted":2,"gc_gen0":0,"gc_gen1":0,"gc_gen2":0,"allocated_bytes":0,"spans":0,"dropped_spans":0,"counters":{"balls":3}}</code>
/// </example>
public sealed class FrameLogWriter : IDisposable
{
	private static readonly JsonEncodedText Counters = JsonEncodedText.Encode("counters");
	private static readonly JsonEncodedText Gauges = JsonEncodedText.Encode("gauges");
	private static readonly JsonEncodedText Histograms = JsonEncodedText.Encode("histograms");
	private static readonly JsonEncodedText Count = JsonEncodedText.Encode("count");
	private static readonly JsonEncodedText Sum = JsonEncodedText.Encode("sum");
	private static readonly JsonEncodedText Min = JsonEncodedText.Encode("min");
	private static readonly JsonEncodedText Max = JsonEncodedText.Encode("max");

	private static readonly JsonEncodedText Frame = JsonEncodedText.Encode("frame");
	private static readonly JsonEncodedText DeltaMs = JsonEncodedText.Encode("delta_ms");
	private static readonly JsonEncodedText FrameMs = JsonEncodedText.Encode("frame_ms");
	private static readonly JsonEncodedText IdleMs = JsonEncodedText.Encode("idle_ms");
	private static readonly JsonEncodedText WorkMs = JsonEncodedText.Encode("work_ms");
	private static readonly JsonEncodedText Fps = JsonEncodedText.Encode("fps");
	private static readonly JsonEncodedText FixedSteps = JsonEncodedText.Encode("fixed_steps");
	private static readonly JsonEncodedText DrawCalls = JsonEncodedText.Encode("draw_calls");
	private static readonly JsonEncodedText Sprites = JsonEncodedText.Encode("sprites");
	private static readonly JsonEncodedText Triangles = JsonEncodedText.Encode("triangles");
	private static readonly JsonEncodedText Entities = JsonEncodedText.Encode("entities");
	private static readonly JsonEncodedText EventsEmitted = JsonEncodedText.Encode("events_emitted");
	private static readonly JsonEncodedText Gc0 = JsonEncodedText.Encode("gc_gen0");
	private static readonly JsonEncodedText Gc1 = JsonEncodedText.Encode("gc_gen1");
	private static readonly JsonEncodedText Gc2 = JsonEncodedText.Encode("gc_gen2");
	private static readonly JsonEncodedText AllocatedBytes = JsonEncodedText.Encode("allocated_bytes");
	private static readonly JsonEncodedText Spans = JsonEncodedText.Encode("spans");
	private static readonly JsonEncodedText DroppedSpans = JsonEncodedText.Encode("dropped_spans");

	private static readonly byte[] NewLine = [(byte)'\n'];

	private readonly Stream _stream;
	private readonly Utf8JsonWriter _json;
	private readonly bool _ownsStream;
	private readonly int _flushFrames;
	private JsonEncodedText[] _names = [];
	private int _unflushed;

	/// <summary>Creates (or truncates) the frame log at <paramref name="path"/>, creating its directory.</summary>
	/// <param name="path">The file to write.</param>
	/// <param name="flushFrames">Flush to disk every this many lines (1: every line).</param>
	public FrameLogWriter(string path, int flushFrames = 60)
		: this(Open(path), flushFrames, ownsStream: true)
	{
		Path = System.IO.Path.GetFullPath(path);
	}

	/// <summary>Writes the frame log to <paramref name="stream"/>.</summary>
	public FrameLogWriter(Stream stream, int flushFrames = 60, bool ownsStream = false)
	{
		ArgumentNullException.ThrowIfNull(stream);
		_stream = stream;
		_ownsStream = ownsStream;
		_flushFrames = Math.Max(1, flushFrames);
		_json = new Utf8JsonWriter(stream, new JsonWriterOptions { SkipValidation = true });
	}

	/// <summary>The full path of the log, when writing to a file.</summary>
	public string? Path { get; }

	/// <summary>The number of lines written.</summary>
	public long Lines { get; private set; }

	/// <summary>Writes the line of one frame.</summary>
	public void Write(in FrameStats stats, IReadOnlyList<MetricsInstrument> instruments)
	{
		var json = _json;
		json.WriteStartObject();
		WriteStats(json, stats);

		if (instruments.Count > 0)
		{
			if (_names.Length < instruments.Count) EncodeNames(instruments);
			WriteGroup(json, Counters, MetricsInstrumentKind.Counter, instruments);
			WriteGroup(json, Gauges, MetricsInstrumentKind.Gauge, instruments);
			WriteGroup(json, Histograms, MetricsInstrumentKind.Histogram, instruments);
		}

		json.WriteEndObject();
		json.Flush();
		json.Reset();
		_stream.Write(NewLine);
		Lines++;

		if (++_unflushed >= _flushFrames) Flush();
	}

	/// <summary>Flushes the log to disk.</summary>
	public void Flush()
	{
		_unflushed = 0;
		_stream.Flush();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_json.Dispose();
		_stream.Flush();
		if (_ownsStream) _stream.Dispose();
	}

	/// <summary>Writes the properties of <paramref name="stats"/> (snake_case) into the current object.</summary>
	public static void WriteStats(Utf8JsonWriter json, in FrameStats stats)
	{
		json.WriteNumber(Frame, stats.Frame);
		json.WriteNumber(DeltaMs, Math.Round(stats.DeltaMs, 3));
		json.WriteNumber(FrameMs, Math.Round(stats.FrameMs, 3));
		json.WriteNumber(IdleMs, Math.Round(stats.IdleMs, 3));
		json.WriteNumber(WorkMs, Math.Round(stats.WorkMs, 3));
		json.WriteNumber(Fps, Math.Round(stats.Fps, 2));
		json.WriteNumber(FixedSteps, stats.FixedSteps);
		json.WriteNumber(DrawCalls, stats.DrawCalls);
		json.WriteNumber(Sprites, stats.Sprites);
		json.WriteNumber(Triangles, stats.Triangles);
		json.WriteNumber(Entities, stats.Entities);
		json.WriteNumber(EventsEmitted, stats.EventsEmitted);
		json.WriteNumber(Gc0, stats.Gc0);
		json.WriteNumber(Gc1, stats.Gc1);
		json.WriteNumber(Gc2, stats.Gc2);
		json.WriteNumber(AllocatedBytes, stats.AllocatedBytes);
		json.WriteNumber(Spans, stats.Spans);
		json.WriteNumber(DroppedSpans, stats.DroppedSpans);
	}

	private void WriteGroup(Utf8JsonWriter json, JsonEncodedText group, MetricsInstrumentKind kind, IReadOnlyList<MetricsInstrument> instruments)
	{
		var open = false;
		for (var i = 0; i < instruments.Count; i++)
		{
			var instrument = instruments[i];
			if (instrument.Kind != kind) continue;

			if (!open)
			{
				json.WritePropertyName(group);
				json.WriteStartObject();
				open = true;
			}

			switch (instrument)
			{
				case MetricsCounter counter:
					json.WriteNumber(_names[i], counter.Value);
					break;
				case MetricsGauge gauge:
					if (double.IsFinite(gauge.Value)) json.WriteNumber(_names[i], gauge.Value);
					else json.WriteNull(_names[i]);
					break;
				case MetricsHistogram histogram:
					json.WritePropertyName(_names[i]);
					json.WriteStartObject();
					json.WriteNumber(Count, histogram.Count);
					json.WriteNumber(Sum, histogram.Sum);
					json.WriteNumber(Min, histogram.Min);
					json.WriteNumber(Max, histogram.Max);
					json.WriteEndObject();
					break;
			}
		}

		if (open) json.WriteEndObject();
	}

	private void EncodeNames(IReadOnlyList<MetricsInstrument> instruments)
	{
		var names = new JsonEncodedText[instruments.Count];
		Array.Copy(_names, names, _names.Length);
		for (var i = _names.Length; i < names.Length; i++) names[i] = JsonEncodedText.Encode(instruments[i].Name);
		_names = names;
	}

	private static FileStream Open(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
	}
}
