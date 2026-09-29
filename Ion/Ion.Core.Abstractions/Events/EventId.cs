namespace Ion;

/// <summary>
/// The integer id of the event type <typeparamref name="T"/>, which traces, logs and tools use to name events (stable
/// within a build, unlike <c>typeof(T).GetHashCode()</c>).
/// </summary>
/// <remarks>
/// The Ion source generator assigns compile-time ids (1, 2, 3, ... in the order of the type names) to every event type the
/// application uses and registers them before the application's builder is created. Types it did not see get an id from
/// <see cref="EventIds.FirstRuntimeId"/> upwards on first use.
/// </remarks>
public static class EventId<T> where T : unmanaged
{
	private static int _value;

	/// <summary>The id of <typeparamref name="T"/>.</summary>
	public static int Value
	{
		get
		{
			var value = Volatile.Read(ref _value);
			return value != 0 ? value : EventIds.AssignRuntimeId(ref _value, typeof(T));
		}
	}

	/// <summary>The display name of <typeparamref name="T"/>.</summary>
	public static string Name => EventIds.NameOf(typeof(T));

	/// <summary>Whether <typeparamref name="T"/> has an id assigned at compile time.</summary>
	public static bool IsGenerated => Value < EventIds.FirstRuntimeId;

	/// <summary>
	/// Assigns the compile-time id <paramref name="id"/> to <typeparamref name="T"/>. Called by generated code. Returns
	/// false (and keeps the current id) when <typeparamref name="T"/> already has an id or <paramref name="id"/> is taken by
	/// another type.
	/// </summary>
	public static bool TryAssign(int id) => EventIds.TryAssign(ref _value, typeof(T), id);
}

/// <summary>The registry behind <see cref="EventId{T}"/>: which type has which id.</summary>
public static class EventIds
{
	/// <summary>The first id given to event types that the generator did not see.</summary>
	public const int FirstRuntimeId = 1 << 16;

	private static readonly Lock Gate = new();
	private static readonly Dictionary<int, Type> Types = [];
	private static int _nextRuntimeId = FirstRuntimeId;

	/// <summary>The event type with id <paramref name="id"/>, if any.</summary>
	public static Type? TypeOf(int id)
	{
		lock (Gate) return Types.GetValueOrDefault(id);
	}

	/// <summary>A short display name for an event type (its name, with generic arguments).</summary>
	public static string NameOf(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		if (!type.IsGenericType) return type.Name;
		var name = type.Name;
		var tick = name.IndexOf('`', StringComparison.Ordinal);
		if (tick >= 0) name = name[..tick];
		return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(NameOf))}>";
	}

	internal static int AssignRuntimeId(ref int slot, Type type)
	{
		lock (Gate)
		{
			if (slot != 0) return slot;
			while (Types.ContainsKey(_nextRuntimeId)) _nextRuntimeId++;
			var id = _nextRuntimeId++;
			Types[id] = type;
			Volatile.Write(ref slot, id);
			return id;
		}
	}

	internal static bool TryAssign(ref int slot, Type type, int id)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

		lock (Gate)
		{
			if (slot == id) return true;
			if (slot != 0 || Types.ContainsKey(id)) return false;
			Types[id] = type;
			Volatile.Write(ref slot, id);
			return true;
		}
	}
}
