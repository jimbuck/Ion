using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// The runtime <see cref="IEvents"/>: one <see cref="EventChannel{T}"/> per event type, created on first use and kept in a
/// dictionary keyed by type. Registered as a singleton (and as <see cref="IEvents"/>) by <c>IonApplication.CreateBuilder</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Ion source generator derives a closed bus from this class for the application (one typed channel field per event
/// type it sees, registered with <see cref="Register{T}"/>), and routes the application's own <c>Emit</c> and
/// <c>Reader</c> calls straight to those fields. Types it cannot see (a plugin's events, generic helpers) still get a
/// channel here at run time, so both paths coexist on one bus.
/// </para>
/// <para>
/// The game loop calls <see cref="BeginFixedStep"/> before every fixed step and the event system calls <see cref="Step"/>
/// at the very end of every frame. An event emitted in frame N is visible in frames N and N+1. When a loop is running, an
/// event that leaves that window before any fixed step has started since it was emitted is kept for readers in FixedUpdate
/// steps until a fixed step has run (at most <see cref="MaxBacklogFrames"/> frames).
/// </para>
/// </remarks>
public class EventBus : IEvents
{
	/// <summary>
	/// The number of frames an event is kept for FixedUpdate readers when no fixed step runs, which bounds the backlog
	/// when the loop runs frames without advancing time.
	/// </summary>
	public const int MaxBacklogFrames = 1024;

	/// <summary>The initial capacity of a channel created at run time.</summary>
	public const int DefaultCapacity = 16;

	private readonly ILoopContext? _loop;
	private readonly Dictionary<Type, EventChannel> _byType = [];
	private readonly List<EventChannel> _channels = [];

	// Cache of _byType indexed by EventTypeIndex<T> (a process-wide number per type), so the runtime path does not hash.
	private EventChannel?[] _slots = new EventChannel?[32];

	/// <summary>Creates a bus that is not tied to a game loop: events are visible for exactly two frames.</summary>
	public EventBus() : this(null) { }

	/// <summary>Creates a bus that keeps events for FixedUpdate readers until a fixed step has run.</summary>
	/// <param name="loop">The game loop context, or <see langword="null"/> for the plain two-frame window.</param>
	public EventBus(ILoopContext? loop)
	{
		_loop = loop;
	}

	/// <summary>The loop context this bus uses, if any.</summary>
	public ILoopContext? Loop => _loop;

	/// <summary>Every channel of this bus, in the order they were created.</summary>
	public IReadOnlyList<EventChannel> Channels => _channels;

	/// <summary>Called when a channel grows past its capacity (a hint that its initial capacity is too small).</summary>
	public event Action<EventChannel>? ChannelGrew;

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[EmitsEvent]
	public void Emit<T>(in T e) where T : unmanaged => Channel<T>().Emit(in e);

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[ReadsEvent]
	public EventReader<T> Reader<T>() where T : unmanaged => Channel<T>().Reader();

	/// <summary>The channel of <typeparamref name="T"/>, created on first use.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public EventChannel<T> Channel<T>() where T : unmanaged
	{
		var index = EventTypeIndex<T>.Value;
		var slots = _slots;
		if ((uint)index < (uint)slots.Length && slots[index] is { } channel) return Unsafe.As<EventChannel<T>>(channel);
		return CreateChannel<T>(DefaultCapacity);
	}

	/// <summary>The channel of <paramref name="eventType"/>, if one was created.</summary>
	public EventChannel? Find(Type eventType) => _byType.GetValueOrDefault(eventType);

	/// <summary>
	/// Creates (or returns) the channel of <typeparamref name="T"/> with an initial <paramref name="capacity"/>. Used by
	/// generated buses to create their typed channels up front.
	/// </summary>
	protected EventChannel<T> Register<T>(int capacity) where T : unmanaged
	{
		if (_byType.TryGetValue(typeof(T), out var existing)) return (EventChannel<T>)existing;
		return CreateChannel<T>(capacity);
	}

	/// <summary>
	/// Starts a fixed step: events emitted before now have been visible to a fixed step. Called by the game loop before
	/// every FixedUpdate step. Until <see cref="EndFixedSteps"/> (or <see cref="Step"/>), readers also see the fixed-step backlog.
	/// </summary>
	public void BeginFixedStep()
	{
		var channels = _channels;
		for (var i = 0; i < channels.Count; i++) channels[i].MarkFixedStep();
		InFixedStep = true;
	}

	/// <summary>Ends the frame's fixed steps: readers see the two-frame window again. Called by the game loop after them.</summary>
	public void EndFixedSteps() => InFixedStep = false;

	/// <summary>
	/// Whether the loop is running fixed steps (between <see cref="BeginFixedStep"/> and <see cref="EndFixedSteps"/>), when
	/// readers also see older events that no fixed step has seen yet.
	/// </summary>
	public bool InFixedStep { get; private set; }

	/// <summary>
	/// Ends the frame: the current frame's events become the previous frame's and older ones are dropped (or kept for
	/// FixedUpdate readers, see remarks). Called by the event system at the end of the Last stage.
	/// </summary>
	public void Step()
	{
		InFixedStep = false;
		var keepBacklog = _loop is not null && _loop.Stage != GameLoopStage.None;
		var channels = _channels;
		for (var i = 0; i < channels.Count; i++) channels[i].Step(keepBacklog);
	}

	internal void OnChannelGrew(EventChannel channel) => ChannelGrew?.Invoke(channel);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private EventChannel<T> CreateChannel<T>(int capacity) where T : unmanaged
	{
		if (!_byType.TryGetValue(typeof(T), out var channel))
		{
			channel = new EventChannel<T>(this, EventId<T>.Value, capacity);
			_byType.Add(typeof(T), channel);
			_channels.Add(channel);
		}

		var index = EventTypeIndex<T>.Value;
		if (index >= _slots.Length) Array.Resize(ref _slots, Math.Max(index + 1, _slots.Length * 2));
		_slots[index] = channel;
		return (EventChannel<T>)channel;
	}
}

/// <summary>A process-wide dense index per event type, for the runtime bus's channel cache.</summary>
internal static class EventTypeIndex
{
	private static int _next = -1;

	public static int Next() => Interlocked.Increment(ref _next);
}

/// <inheritdoc cref="EventTypeIndex"/>
internal static class EventTypeIndex<T> where T : unmanaged
{
	public static readonly int Value = EventTypeIndex.Next();
}
