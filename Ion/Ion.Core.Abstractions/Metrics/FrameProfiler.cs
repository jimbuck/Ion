using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ion;

/// <summary>
/// One recorded span: an interned name, the recording thread and two <see cref="Stopwatch.GetTimestamp"/> values.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SpanRecord(SpanId Id, int ThreadId, long Start, long End)
{
	/// <summary>The duration in <see cref="Stopwatch"/> ticks.</summary>
	public long Duration => End - Start;
}

/// <summary>What a <see cref="FrameProfile"/> covers.</summary>
public enum FrameKind
{
	/// <summary>A frame of the loop.</summary>
	Frame,
	/// <summary>The Init stage (before the first frame).</summary>
	Init,
	/// <summary>The Destroy stage (after the last frame).</summary>
	Destroy,
}

/// <summary>
/// One slot of the <see cref="FrameProfiler"/> ring: a frame's boundaries, its <see cref="FrameStats"/> and the spans
/// recorded during it. Slots are preallocated and reused; a profile handed to an <see cref="IFrameListener"/> or copied
/// with <see cref="FrameProfiler.CopyFrames"/> is valid until the ring wraps around to it again.
/// </summary>
public sealed class FrameProfile
{
	internal SpanRecord[] Buffer = [];
	internal int Count;
	internal int Dropped;

	/// <summary>What this profile covers.</summary>
	public FrameKind Kind { get; internal set; }

	/// <summary>The frame number.</summary>
	public uint Frame { get; internal set; }

	/// <summary>The frame's start timestamp (<see cref="Stopwatch.GetTimestamp"/>).</summary>
	public long Start { get; internal set; }

	/// <summary>The frame's end timestamp (<see cref="Stopwatch.GetTimestamp"/>).</summary>
	public long End { get; internal set; }

	/// <summary>The managed thread id of the loop thread that ran the frame.</summary>
	public int ThreadId { get; internal set; }

	/// <summary>The frame's counters.</summary>
	public FrameStats Stats;

	/// <summary>The spans recorded during the frame, in the order they ended.</summary>
	public ReadOnlySpan<SpanRecord> Spans => Buffer.AsSpan(0, Math.Min(Volatile.Read(ref Count), Buffer.Length));

	/// <summary>Spans that did not fit in the buffer.</summary>
	public int DroppedSpans => Math.Max(0, Volatile.Read(ref Count) - Buffer.Length) + Dropped;

	internal void Reset(FrameKind kind, uint frame, long start)
	{
		Kind = kind;
		Frame = frame;
		Start = start;
		End = start;
		ThreadId = Environment.CurrentManagedThreadId;
		Stats = default;
		Dropped = 0;
		Volatile.Write(ref Count, 0);
	}
}

/// <summary>
/// The frame profiler: a preallocated ring of <see cref="FrameProfile"/>s, <c>HistoryFrames</c> deep, each with room for
/// <c>SpansPerFrame</c> spans. The game loop opens and closes a profile every frame and writes its
/// <see cref="FrameStats"/>; the generated schedule, the runtime schedule and engine systems record spans into it
/// (<see cref="Begin"/>/<see cref="End"/>, or <see cref="Scope"/>). Recording never allocates and never touches a string.
/// </summary>
/// <remarks>
/// <para>
/// Span recording has two switches. <see cref="IsProfilingEnabled"/> is a feature switch (<c>Ion.Metrics.Profiling</c>,
/// set with the <c>IonMetricsProfiling</c> MSBuild property): when a NativeAOT or trimmed app is published with it
/// <c>false</c> every recording site is removed by the compiler. When it is on (the default), <see cref="IsActive"/> is
/// the runtime toggle, off until metrics turn it on (<c>Ion:Metrics:Profiling</c>, <c>IMetrics.IsProfiling</c> or a
/// capture). Frame stats are collected whenever the profiler has a history (<see cref="IsEnabled"/>), whatever the switches.
/// </para>
/// <para>
/// Spans may be recorded from any thread; they land in the frame that is current on the loop thread.
/// </para>
/// </remarks>
public sealed class FrameProfiler : IStepProfiler
{
	/// <summary>The name of the feature switch (<see cref="AppContext"/> switch and <c>RuntimeHostConfigurationOption</c>).</summary>
	public const string ProfilingSwitchName = "Ion.Metrics.Profiling";

	/// <summary>The default number of frames kept.</summary>
	public const int DefaultHistoryFrames = 300;

	/// <summary>The default number of spans each frame can hold.</summary>
	public const int DefaultSpansPerFrame = 512;

	private readonly FrameProfile[] _ring;
	private FrameProfile _current;
	private long _completed;
	private long _historyStart;
	private bool _active;
	private bool _buffersAllocated;
	private FrameStats _last;
	private IFrameListener[] _listeners = [];

	private int _gc0, _gc1, _gc2;
	private long _allocatedBytes;

	/// <summary>
	/// Whether span recording is compiled in: the <c>Ion.Metrics.Profiling</c> feature switch (default true). A
	/// <c>static readonly</c> value, so the JIT folds it, and a feature switch, so ILC removes every guarded recording site
	/// from an app published with <c>&lt;IonMetricsProfiling&gt;false&lt;/IonMetricsProfiling&gt;</c>.
	/// </summary>
	[FeatureSwitchDefinition(ProfilingSwitchName)]
	public static bool IsProfilingEnabled { get; } = !AppContext.TryGetSwitch(ProfilingSwitchName, out var enabled) || enabled;

	/// <summary>
	/// A profiler without history: it never records and collects no stats. The engine uses it until metrics are installed.
	/// </summary>
	public static FrameProfiler Disabled { get; } = new(0, 0);

	/// <summary>Creates a profiler.</summary>
	/// <param name="historyFrames">The number of completed frames kept (0 disables the profiler entirely).</param>
	/// <param name="spansPerFrame">The number of spans each frame can hold (0 disables span recording).</param>
	public FrameProfiler(int historyFrames = DefaultHistoryFrames, int spansPerFrame = DefaultSpansPerFrame)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(historyFrames);
		ArgumentOutOfRangeException.ThrowIfNegative(spansPerFrame);

		HistoryFrames = historyFrames;
		SpansPerFrame = historyFrames == 0 ? 0 : spansPerFrame;

		// One more slot than the history: the frame in progress never overwrites a completed one that is still kept.
		_ring = new FrameProfile[historyFrames + 1];
		for (var i = 0; i < _ring.Length; i++) _ring[i] = new FrameProfile();
		_current = _ring[0];
	}

	/// <summary>The number of completed frames kept.</summary>
	public int HistoryFrames { get; }

	/// <summary>The number of spans each frame can hold.</summary>
	public int SpansPerFrame { get; }

	/// <summary>Whether this profiler keeps frames and collects stats (it has a history).</summary>
	public bool IsEnabled => HistoryFrames > 0;

	/// <inheritdoc/>
	public bool CanRecord => IsProfilingEnabled && SpansPerFrame > 0;

	/// <summary>
	/// The runtime toggle for span recording. Turning it on the first time allocates the span buffers
	/// (<c>HistoryFrames x SpansPerFrame</c> records of 24 bytes); it stays false when <see cref="CanRecord"/> is false.
	/// </summary>
	public bool IsActive
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => IsProfilingEnabled && _active;
		set
		{
			if (value && !CanRecord) value = false;
			if (value && !_buffersAllocated) AllocateBuffers();
			_active = value;
		}
	}

	/// <summary>A live receiver of every span (a Tracy bridge); null by default.</summary>
	public ISpanSink? SpanSink { get; set; }

	/// <summary>The number of frames completed (of <see cref="FrameKind.Frame"/> or not).</summary>
	public long FramesCompleted => _completed;

	/// <summary>The stats of the last completed frame of the loop (default before the first).</summary>
	public FrameStats LastFrame => _last;

	/// <summary>The number of completed profiles currently kept (at most <see cref="HistoryFrames"/>).</summary>
	public int Count => (int)Math.Min(_completed - _historyStart, HistoryFrames);

	/// <summary>The profile being recorded.</summary>
	public FrameProfile Current => _current;

	/// <summary>
	/// A completed profile: 0 is the most recent, <see cref="Count"/> - 1 the oldest kept.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="age"/> is negative or not below <see cref="Count"/>.</exception>
	public FrameProfile GetFrame(int age)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(age);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(age, Count);
		return _ring[(int)((_completed - 1 - age) % _ring.Length)];
	}

	/// <summary>
	/// Adds the last <paramref name="maxFrames"/> completed profiles (at most <see cref="Count"/>) to
	/// <paramref name="destination"/>, oldest first. Returns the number added.
	/// </summary>
	public int CopyFrames(ICollection<FrameProfile> destination, int maxFrames = int.MaxValue)
	{
		ArgumentNullException.ThrowIfNull(destination);
		var count = Math.Min(Math.Max(maxFrames, 0), Count);
		for (var age = count - 1; age >= 0; age--) destination.Add(GetFrame(age));
		return count;
	}

	/// <summary>Forgets every kept profile (the ring keeps its memory).</summary>
	public void Clear() => _historyStart = _completed;

	/// <summary>Adds a listener called at the end of every frame.</summary>
	public void AddListener(IFrameListener listener)
	{
		ArgumentNullException.ThrowIfNull(listener);
		lock (_ring) _listeners = [.. _listeners, listener];
	}

	/// <summary>Removes a listener added with <see cref="AddListener"/>.</summary>
	public void RemoveListener(IFrameListener listener)
	{
		lock (_ring) _listeners = [.. _listeners.Where(l => !ReferenceEquals(l, listener))];
	}

	/// <summary>
	/// Starts a span: returns its start timestamp, or 0 when not recording. Guard the call with
	/// <see cref="IsProfilingEnabled"/> so that it is removed when the feature switch is off.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public long Begin(SpanId span)
	{
		if (!_active) return 0;
		return BeginCore(span);
	}

	/// <summary>Ends a span started with <see cref="Begin"/>; nothing when <paramref name="start"/> is 0.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void End(SpanId span, long start)
	{
		if (start != 0) EndCore(span, start);
	}

	/// <summary>
	/// A span for the enclosing <c>using</c> block: <c>using var _ = profiler.Scope(id);</c>. A no-op that does not even
	/// read the clock when not recording; never allocates.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public MetricsScope Scope(SpanId span)
	{
		if (IsProfilingEnabled && _active) return new MetricsScope(this, span, BeginCore(span));
		return default;
	}

	/// <summary>Records a span with explicit timestamps (<see cref="Stopwatch.GetTimestamp"/>) into the current frame.</summary>
	public void Record(SpanId span, long start, long end)
	{
		var profile = _current;
		var index = Interlocked.Increment(ref profile.Count) - 1;
		var buffer = profile.Buffer;
		if ((uint)index < (uint)buffer.Length) buffer[index] = new SpanRecord(span, Environment.CurrentManagedThreadId, start, end);
	}

	/// <summary>
	/// Opens the profile of a frame (the loop calls this; tests and custom loops may too). Nothing when
	/// <see cref="IsEnabled"/> is false.
	/// </summary>
	public void BeginFrame(uint frame, FrameKind kind = FrameKind.Frame)
	{
		if (!IsEnabled) return;
		var profile = _ring[(int)(_completed % _ring.Length)];
		profile.Reset(kind, frame, Stopwatch.GetTimestamp());
		_current = profile;
	}

	/// <summary>
	/// Closes the current profile: completes <paramref name="stats"/> with the wall-clock frame time, the GC and allocation
	/// deltas and the span counts, keeps it, and calls every listener. Nothing when <see cref="IsEnabled"/> is false.
	/// </summary>
	public void EndFrame(ref FrameStats stats)
	{
		if (!IsEnabled) return;

		var profile = _current;
		var end = Stopwatch.GetTimestamp();
		profile.End = end;

		int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
		// The loop thread's own allocations: exact, monotonic across collections and about 7 ns. The process-wide total is
		// either imprecise (it counts the unused part of every allocation context, which a collection drops, so its delta
		// can be off by tens of kilobytes or negative) or about 600 ns (precise), too much to pay every frame.
		var allocated = GC.GetAllocatedBytesForCurrentThread();
		if (_completed > 0)
		{
			stats.Gc0 = gc0 - _gc0;
			stats.Gc1 = gc1 - _gc1;
			stats.Gc2 = gc2 - _gc2;
			stats.AllocatedBytes = allocated - _allocatedBytes;
		}

		(_gc0, _gc1, _gc2, _allocatedBytes) = (gc0, gc1, gc2, allocated);

		stats.Frame = profile.Frame;
		stats.FrameMs = Stopwatch.GetElapsedTime(profile.Start, end).TotalMilliseconds;
		stats.Spans = profile.Spans.Length;
		stats.DroppedSpans = profile.DroppedSpans;
		profile.Stats = stats;

		_completed++;
		if (profile.Kind == FrameKind.Frame) _last = stats;

		if (IsProfilingEnabled && _active) SpanSink?.FrameMark();

		var listeners = _listeners;
		for (var i = 0; i < listeners.Length; i++) listeners[i].OnFrame(profile);
	}

	/// <summary>Closes the current profile with empty stats (see <see cref="EndFrame(ref FrameStats)"/>).</summary>
	public void EndFrame()
	{
		var stats = default(FrameStats);
		EndFrame(ref stats);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private long BeginCore(SpanId span)
	{
		SpanSink?.Begin(span);
		return Stopwatch.GetTimestamp();
	}

	private void EndCore(SpanId span, long start)
	{
		var end = Stopwatch.GetTimestamp();
		var profile = _current;
		var index = Interlocked.Increment(ref profile.Count) - 1;
		var buffer = profile.Buffer;
		if ((uint)index < (uint)buffer.Length) buffer[index] = new SpanRecord(span, Environment.CurrentManagedThreadId, start, end);
		SpanSink?.End(span);
	}

	private void AllocateBuffers()
	{
		foreach (var profile in _ring)
		{
			if (profile.Buffer.Length != SpansPerFrame) profile.Buffer = new SpanRecord[SpansPerFrame];
		}

		_buffersAllocated = true;
	}
}

/// <summary>
/// A span for a <c>using</c> block (<c>using var _ = profiler.Scope(id);</c>), from <see cref="FrameProfiler.Scope"/>.
/// A <c>ref struct</c>: it cannot be boxed, so it never allocates. The default value does nothing.
/// </summary>
public readonly ref struct MetricsScope
{
	private readonly FrameProfiler? _profiler;
	private readonly SpanId _span;
	private readonly long _start;

	internal MetricsScope(FrameProfiler profiler, SpanId span, long start)
	{
		_profiler = profiler;
		_span = span;
		_start = start;
	}

	/// <summary>Whether this scope records a span.</summary>
	public bool IsRecording => _profiler is not null;

	/// <summary>Ends the span.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		_profiler?.End(_span, _start);
	}
}
