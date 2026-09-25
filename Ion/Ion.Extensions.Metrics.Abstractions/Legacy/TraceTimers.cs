using System.ComponentModel;

using Ion.Extensions.Metrics;

namespace Ion.Extensions.Debug;

// The trace timer API of Ion 0.2, kept for one release as adapters over the frame profiler. Every name started with a
// timer is interned the first time (a dictionary lookup per Start), and recording boxes an instance: use MetricsScope.

/// <summary>A started trace timing.</summary>
[Obsolete(LegacyTrace.Message)]
public interface ITraceTimerInstance
{
	/// <summary>Stops this timing and starts a new one named <paramref name="name"/> (same prefix).</summary>
	void Then(string name);

	/// <summary>Stops this timing.</summary>
	void Stop();
}

/// <summary>Starts trace timings named <c>{prefix}::{name}</c>.</summary>
[Obsolete(LegacyTrace.Message)]
public interface ITraceTimer
{
	/// <summary>Starts a timing named <paramref name="name"/>.</summary>
	ITraceTimerInstance Start(string name);
}

/// <summary>A trace timer whose prefix is the name of <typeparamref name="T"/>.</summary>
[Obsolete(LegacyTrace.Message)]
public interface ITraceTimer<T> : ITraceTimer { }

/// <summary>Collects trace timings and writes them out (Chrome trace format).</summary>
[Obsolete(LegacyTrace.Message)]
public interface ITraceManager
{
	/// <summary>Whether timers started now record anything (the profiler's runtime toggle).</summary>
	bool IsEnabled { get; set; }

	/// <summary>Enables tracing.</summary>
	void Start();

	/// <summary>Disables tracing.</summary>
	void Stop();

	/// <summary>Discards every recorded timing.</summary>
	void Clear();

	/// <summary>Writes the recorded timings to the configured trace output.</summary>
	void OutputTrace();

	/// <summary>Creates a timer whose timings are named <c>{prefix}::{name}</c>.</summary>
	ITraceTimer CreateTimer(string prefix);
}

/// <summary>A timing that records nothing.</summary>
[Obsolete(LegacyTrace.Message)]
public struct NullTimerInstance : ITraceTimerInstance
{
	/// <inheritdoc/>
	public readonly void Then(string name) { }

	/// <inheritdoc/>
	public readonly void Stop() { }
}

/// <summary>One recorded timing of the 0.2 trace manager.</summary>
[Obsolete(LegacyTrace.Message)]
public record struct TraceTiming(int Id, string Name, double Start, double Stop, int ThreadId)
{
	/// <summary>The duration.</summary>
	public readonly double Duration => Stop - Start;
}

/// <summary>
/// The <see cref="ITraceTimer"/> adapter: records <c>{prefix}::{name}</c> spans into a <see cref="FrameProfiler"/>.
/// Returns a shared no-op instance (no allocation) when the profiler is not recording.
/// </summary>
[Obsolete(LegacyTrace.Message)]
[EditorBrowsable(EditorBrowsableState.Never)]
public class TraceTimerAdapter : ITraceTimer
{
	private static readonly ITraceTimerInstance _null = new NullTimerInstance();

	private readonly FrameProfiler _profiler;
	private readonly string _prefix;
	private readonly Dictionary<string, SpanId> _ids = new(StringComparer.Ordinal);

	/// <summary>Creates a timer recording into <paramref name="profiler"/> with <paramref name="prefix"/>.</summary>
	public TraceTimerAdapter(FrameProfiler profiler, string prefix)
	{
		ArgumentNullException.ThrowIfNull(profiler);
		ArgumentNullException.ThrowIfNull(prefix);
		_profiler = profiler;
		_prefix = prefix + "::";
	}

	/// <inheritdoc/>
	public ITraceTimerInstance Start(string name)
	{
		if (!FrameProfiler.IsProfilingEnabled || !_profiler.IsActive) return _null;

		var id = Id(name);
		var pool = _pool ??= new Stack<Instance>();
		Instance? instance;
		lock (pool) pool.TryPop(out instance);
		instance ??= new Instance(this);
		instance.Begin(id);
		return instance;
	}

	// Stopped instances are reused, so a timer that is started and stopped every frame allocates only while warming up.
	// Created on first use, so an app published with profiling compiled out keeps none of this.
	private Stack<Instance>? _pool;

	private SpanId Id(string name)
	{
		lock (_ids)
		{
			if (!_ids.TryGetValue(name, out var id)) _ids[name] = id = MetricsIds.Register(_prefix + name);
			return id;
		}
	}

	private sealed class Instance(TraceTimerAdapter timer) : ITraceTimerInstance
	{
		private SpanId _id;
		private long _start;
		private bool _running;

		public void Begin(SpanId id)
		{
			_id = id;
			_start = timer._profiler.Begin(id);
			_running = true;
		}

		public void Then(string name)
		{
			if (_running) timer._profiler.End(_id, _start);
			Begin(timer.Id(name));
		}

		public void Stop()
		{
			if (!_running) return;
			_running = false;
			timer._profiler.End(_id, _start);
			var pool = timer._pool!;
			lock (pool) pool.Push(this);
		}
	}
}

/// <summary>The <see cref="ITraceTimer{T}"/> adapter (prefix: the name of <typeparamref name="T"/>).</summary>
[Obsolete(LegacyTrace.Message)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TraceTimerAdapter<T>(FrameProfiler profiler) : TraceTimerAdapter(profiler, typeof(T).Name), ITraceTimer<T>
{
}
