using System.Collections;

namespace Ion;

/// <summary>
/// What a <see cref="Wait"/> waits for.
/// </summary>
public enum WaitKind : byte
{
	/// <summary>Nothing: the coroutine resumes on the next frame (the same as yielding <c>null</c>).</summary>
	None = 0,
	/// <summary>A number of seconds of game time (<see cref="Wait.Seconds"/>).</summary>
	Seconds,
	/// <summary>Until a predicate returns true (<see cref="Wait.Predicate"/>).</summary>
	Until,
	/// <summary>While a predicate returns true (<see cref="Wait.Predicate"/>).</summary>
	While,
	/// <summary>Until an event of <see cref="Wait.EventType"/> is emitted.</summary>
	Event,
	/// <summary>Until a nested coroutine (<see cref="Wait.Routine"/>) finishes.</summary>
	Routine,
	/// <summary>Until a custom <see cref="IWait"/> (<see cref="Wait.Custom"/>) is ready.</summary>
	Custom,
}

/// <summary>
/// A wait condition yielded by a coroutine: an unboxed union of every built-in wait (a kind, a number of seconds and one
/// reference for the predicate, event type, nested routine or custom wait). The runner copies it into the coroutine's
/// handle and evaluates it there, so waiting allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// Coroutines may be written as <see cref="IEnumerator"/> (yield <c>null</c>, a <see cref="float"/> number of seconds, a
/// <see cref="Wait"/>, an <see cref="IWait"/> or a nested <see cref="IEnumerator"/>) or as <see cref="IEnumerator{T}"/> of
/// <see cref="Wait"/>. Yielding a struct through the non-generic <see cref="IEnumerator.Current"/> boxes it, so for zero
/// allocation per yield write <c>IEnumerator&lt;Wait&gt;</c> coroutines: a float converts implicitly
/// (<c>yield return 0.5f;</c>), <c>yield return Wait.None;</c> resumes next frame, and a nested routine is
/// <c>yield return Wait.For(Inner());</c>.
/// </para>
/// <para>
/// The predicate of <see cref="Until"/> and <see cref="While"/> is a delegate: a lambda that captures variables allocates
/// once where it is created, not per frame. <see cref="For{TEvent}"/> stores a cached per-type reference.
/// </para>
/// </remarks>
public readonly struct Wait
{
	private readonly object? _ref;

	private Wait(WaitKind kind, float seconds, object? reference)
	{
		Kind = kind;
		Seconds = seconds;
		_ref = reference;
	}

	/// <summary>What this wait waits for.</summary>
	public WaitKind Kind { get; }

	/// <summary>The delay in seconds, for <see cref="WaitKind.Seconds"/>.</summary>
	public float Seconds { get; }

	/// <summary>The predicate, for <see cref="WaitKind.Until"/> and <see cref="WaitKind.While"/>.</summary>
	public Func<bool>? Predicate => _ref as Func<bool>;

	/// <summary>The event type, for <see cref="WaitKind.Event"/>.</summary>
	public Type? EventType => (_ref as EventWait)?.EventType;

	/// <summary>The nested coroutine, for <see cref="WaitKind.Routine"/>.</summary>
	public IEnumerator? Routine => _ref as IEnumerator;

	/// <summary>The custom wait, for <see cref="WaitKind.Custom"/>.</summary>
	public IWait? Custom => _ref as IWait;

	/// <summary>Resumes on the next frame.</summary>
	public static Wait None => default;

	/// <summary>Waits <paramref name="delay"/> seconds of game time.</summary>
	public static Wait For(float delay) => new(WaitKind.Seconds, delay, null);

	/// <summary>Waits <paramref name="delay"/> of game time.</summary>
	public static Wait For(TimeSpan delay) => new(WaitKind.Seconds, (float)delay.TotalSeconds, null);

	/// <summary>Waits until an event of type <typeparamref name="TEvent"/> is emitted (seen by the coroutine's listener).</summary>
	public static Wait For<TEvent>() where TEvent : unmanaged => new(WaitKind.Event, 0, EventWait<TEvent>.Instance);

	/// <summary>Runs <paramref name="routine"/> as a nested coroutine and resumes when it finishes.</summary>
	public static Wait For(IEnumerator routine)
	{
		ArgumentNullException.ThrowIfNull(routine);
		return new(WaitKind.Routine, 0, routine);
	}

	/// <summary>Waits until the custom <paramref name="wait"/> is ready.</summary>
	public static Wait For(IWait wait)
	{
		ArgumentNullException.ThrowIfNull(wait);
		return new(WaitKind.Custom, 0, wait);
	}

	/// <summary>Waits until <paramref name="predicate"/> returns true (checked once per frame).</summary>
	public static Wait Until(Func<bool> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);
		return new(WaitKind.Until, 0, predicate);
	}

	/// <summary>Waits while <paramref name="predicate"/> returns true (checked once per frame).</summary>
	public static Wait While(Func<bool> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);
		return new(WaitKind.While, 0, predicate);
	}

	/// <summary>A number of seconds, as <see cref="For(float)"/>.</summary>
	public static implicit operator Wait(float seconds) => For(seconds);

	/// <summary>A duration, as <see cref="For(TimeSpan)"/>.</summary>
	public static implicit operator Wait(TimeSpan delay) => For(delay);

	/// <summary>
	/// Converts what a non-generic coroutine yielded into a <see cref="Wait"/>: <c>null</c> and unknown values are
	/// <see cref="None"/>, a <see cref="float"/> is seconds, and a <see cref="Wait"/>, <see cref="IWait"/> or
	/// <see cref="IEnumerator"/> keeps its meaning. Reading an already boxed value does not allocate.
	/// </summary>
	public static Wait FromYield(object? value) => value switch
	{
		null => default,
		Wait wait => wait,
		float seconds => For(seconds),
		IEnumerator routine => new(WaitKind.Routine, 0, routine),
		IWait custom => new(WaitKind.Custom, 0, custom),
		_ => default,
	};

	/// <summary>
	/// For <see cref="WaitKind.Event"/>: polls <paramref name="listener"/> for the event type and returns true when one
	/// was seen. False for every other kind.
	/// </summary>
	public bool PollEvent(IEventListener listener) => _ref is EventWait e && e.Check(listener);

	/// <inheritdoc/>
	public override string ToString() => Kind switch
	{
		WaitKind.Seconds => $"Wait.For({Seconds}s)",
		WaitKind.Event => $"Wait.For<{EventType?.Name}>()",
		_ => $"Wait.{Kind}",
	};

	// One cached instance per event type, so Wait.For<TEvent>() stores a reference instead of allocating.
	private abstract class EventWait
	{
		public abstract Type EventType { get; }
		public abstract bool Check(IEventListener listener);
	}

	private sealed class EventWait<TEvent> : EventWait where TEvent : unmanaged
	{
		public static readonly EventWait<TEvent> Instance = new();
		public override Type EventType => typeof(TEvent);
		public override bool Check(IEventListener listener) => listener.On<TEvent>();
	}
}
