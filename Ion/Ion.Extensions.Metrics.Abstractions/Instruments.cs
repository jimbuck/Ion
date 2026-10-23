using System.Runtime.CompilerServices;

namespace Ion.Extensions.Metrics;

/// <summary>The kind of a <see cref="MetricsInstrument"/>.</summary>
public enum MetricsInstrumentKind
{
	/// <summary>A running total (<see cref="MetricsCounter"/>).</summary>
	Counter,
	/// <summary>A value set by the game (<see cref="MetricsGauge"/>).</summary>
	Gauge,
	/// <summary>Values recorded per frame (<see cref="MetricsHistogram"/>).</summary>
	Histogram,
}

/// <summary>
/// A game metric registered by name once with <see cref="IMetrics"/>; the returned handle is updated directly, with no
/// lookup. Values are reported every frame in the frame log and through the <c>Ion</c> <c>Meter</c>.
/// </summary>
public abstract class MetricsInstrument
{
	private protected MetricsInstrument(string name, string? unit, string? description)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		Name = name;
		Unit = unit;
		Description = description;
	}

	/// <summary>The name, as registered.</summary>
	public string Name { get; }

	/// <summary>The unit, if any.</summary>
	public string? Unit { get; }

	/// <summary>The description, if any.</summary>
	public string? Description { get; }

	/// <summary>The kind of instrument.</summary>
	public abstract MetricsInstrumentKind Kind { get; }

	/// <inheritdoc/>
	public override string ToString() => $"{Kind} {Name}";
}

/// <summary>A running total. Thread safe.</summary>
public sealed class MetricsCounter(string name, string? unit = null, string? description = null) : MetricsInstrument(name, unit, description)
{
	private long _value;

	/// <inheritdoc/>
	public override MetricsInstrumentKind Kind => MetricsInstrumentKind.Counter;

	/// <summary>The total.</summary>
	public long Value => Volatile.Read(ref _value);

	/// <summary>Adds one.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Increment() => Interlocked.Increment(ref _value);

	/// <summary>Adds <paramref name="delta"/> (may be negative).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(long delta) => Interlocked.Add(ref _value, delta);
}

/// <summary>A value set by the game (the last value set is reported).</summary>
public sealed class MetricsGauge(string name, string? unit = null, string? description = null) : MetricsInstrument(name, unit, description)
{
	private double _value;

	/// <inheritdoc/>
	public override MetricsInstrumentKind Kind => MetricsInstrumentKind.Gauge;

	/// <summary>The current value.</summary>
	public double Value => Volatile.Read(ref _value);

	/// <summary>Sets the value.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Set(double value) => Volatile.Write(ref _value, value);
}

/// <summary>
/// Values recorded during a frame, summarized per frame (count, sum, min, max) and reset when the frame ends. Record from
/// the loop thread.
/// </summary>
public sealed class MetricsHistogram(string name, string? unit = null, string? description = null) : MetricsInstrument(name, unit, description)
{
	private long _count;
	private double _sum;
	private double _min = double.PositiveInfinity;
	private double _max = double.NegativeInfinity;

	/// <inheritdoc/>
	public override MetricsInstrumentKind Kind => MetricsInstrumentKind.Histogram;

	/// <summary>Values recorded this frame.</summary>
	public long Count => _count;

	/// <summary>Their sum.</summary>
	public double Sum => _sum;

	/// <summary>The smallest (0 when none).</summary>
	public double Min => _count == 0 ? 0 : _min;

	/// <summary>The largest (0 when none).</summary>
	public double Max => _count == 0 ? 0 : _max;

	/// <summary>The mean (0 when none).</summary>
	public double Mean => _count == 0 ? 0 : _sum / _count;

	/// <summary>Values recorded since the histogram was created.</summary>
	public long TotalCount { get; private set; }

	/// <summary>Called for every recorded value (the <c>Meter</c> bridge); null by default.</summary>
	public Action<double>? Recorded { get; set; }

	/// <summary>Records <paramref name="value"/>.</summary>
	public void Record(double value)
	{
		_count++;
		TotalCount++;
		_sum += value;
		if (value < _min) _min = value;
		if (value > _max) _max = value;
		Recorded?.Invoke(value);
	}

	/// <summary>Starts a new frame (called by the metrics module after the frame is reported).</summary>
	public void Reset()
	{
		_count = 0;
		_sum = 0;
		_min = double.PositiveInfinity;
		_max = double.NegativeInfinity;
	}
}
