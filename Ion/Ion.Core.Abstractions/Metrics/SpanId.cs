using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// An interned span name: a small integer registered once with <see cref="MetricsIds.Register"/> (at construction, or
/// by generated code) and resolved back to its name only when a trace is exported. Recording a span never touches a string.
/// </summary>
/// <param name="Value">The id; 0 is <see cref="None"/>.</param>
public readonly record struct SpanId(int Value)
{
	/// <summary>No span.</summary>
	public static SpanId None => default;

	/// <summary>Whether this is a registered id.</summary>
	public bool IsValid => Value > 0;

	/// <summary>The registered name (see <see cref="MetricsIds.NameOf"/>).</summary>
	public string Name => MetricsIds.NameOf(this);

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>
/// The process-wide registry of span names (<see cref="SpanId"/>). Registering the same name twice returns the same id.
/// Registration takes a lock and may allocate; do it once (in a constructor or a static field), never per frame.
/// </summary>
public static class MetricsIds
{
	private static readonly Lock _lock = new();
	private static readonly Dictionary<string, SpanId> _byName = new(StringComparer.Ordinal);
	private static string[] _names = new string[64];
	private static int _count = 1; // 0 is SpanId.None

	/// <summary>The number of registered names, plus one for <see cref="SpanId.None"/>.</summary>
	public static int Count => Volatile.Read(ref _count);

	/// <summary>Registers <paramref name="name"/> (or returns its existing id).</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
	public static SpanId Register(string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		lock (_lock)
		{
			if (_byName.TryGetValue(name, out var existing)) return existing;

			var id = new SpanId(_count);
			if (_count == _names.Length)
			{
				var names = new string[_names.Length * 2];
				Array.Copy(_names, names, _names.Length);
				Volatile.Write(ref _names, names);
			}

			_names[_count] = name;
			_byName.Add(name, id);
			Volatile.Write(ref _count, _count + 1);
			return id;
		}
	}

	/// <summary>The id of <paramref name="name"/>, if it was registered.</summary>
	public static bool TryGet(string name, out SpanId id)
	{
		lock (_lock) return _byName.TryGetValue(name, out id);
	}

	/// <summary>The name of <paramref name="id"/>; <c>"?"</c> for an id that was never registered.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string NameOf(SpanId id)
	{
		var value = id.Value;
		if (value <= 0 || value >= Count) return "?";
		var names = Volatile.Read(ref _names);
		return (uint)value < (uint)names.Length ? names[value] ?? "?" : "?";
	}
}

/// <summary>
/// The span ids of the engine itself: the frame, every stage, the idle time spent pacing, and the engine systems.
/// </summary>
public static class SpanIds
{
	/// <summary>A whole frame (exported from the frame boundaries, not recorded as a span).</summary>
	public static readonly SpanId Frame = MetricsIds.Register("Frame");

	/// <summary>The Init stage.</summary>
	public static readonly SpanId Init = MetricsIds.Register("Init");

	/// <summary>The First stage.</summary>
	public static readonly SpanId First = MetricsIds.Register("First");

	/// <summary>One FixedUpdate step.</summary>
	public static readonly SpanId FixedUpdate = MetricsIds.Register("FixedUpdate");

	/// <summary>The Update stage.</summary>
	public static readonly SpanId Update = MetricsIds.Register("Update");

	/// <summary>The Render stage.</summary>
	public static readonly SpanId Render = MetricsIds.Register("Render");

	/// <summary>The Last stage.</summary>
	public static readonly SpanId Last = MetricsIds.Register("Last");

	/// <summary>The Destroy stage.</summary>
	public static readonly SpanId Destroy = MetricsIds.Register("Destroy");

	/// <summary>Time the loop slept to honour <c>MaxFPS</c>.</summary>
	public static readonly SpanId Idle = MetricsIds.Register("Idle");

	/// <summary>The event system stepping the frame buffers.</summary>
	public static readonly SpanId EventsStep = MetricsIds.Register("EventSystem.Step");

	/// <summary>The span of <paramref name="stage"/>.</summary>
	public static SpanId Of(Stage stage) => stage switch
	{
		Stage.Init => Init,
		Stage.First => First,
		Stage.FixedUpdate => FixedUpdate,
		Stage.Update => Update,
		Stage.Render => Render,
		Stage.Last => Last,
		Stage.Destroy => Destroy,
		_ => SpanId.None,
	};
}
