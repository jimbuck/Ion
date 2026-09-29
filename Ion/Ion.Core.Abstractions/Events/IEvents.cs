namespace Ion;

/// <summary>
/// The frame event bus: unboxed, per-type channels with a read cursor per reader.
/// </summary>
/// <remarks>
/// <para>
/// An event emitted in frame N is visible to readers in frame N (after it is emitted) and in frame N+1, and each
/// <see cref="EventReader{T}"/> sees each event once. Readers that read during a FixedUpdate step also see older events
/// that no fixed step has had a chance to see yet (see <see cref="EventBus.MaxBacklogFrames"/>), so fixed-step consumers
/// never miss an event when a frame runs no fixed step.
/// </para>
/// <para>
/// Create readers once, in a constructor or a field initializer, and keep them in a field that is not
/// <see langword="readonly"/>: a reader created inside a stage method starts from the oldest visible event every time
/// (ION103), and a reader in a <see langword="readonly"/> field is copied on every call, so its cursor never advances
/// (ION106).
/// </para>
/// <para>
/// With the Ion source generator, the application's <c>IonApplication.CreateBuilder</c> call installs a generated bus with
/// one typed channel field per event type used in the game; types it cannot see get a channel created at run time.
/// </para>
/// </remarks>
public interface IEvents
{
	/// <summary>Appends <paramref name="e"/> to the channel of <typeparamref name="T"/>. Does not allocate in steady state.</summary>
	[EmitsEvent]
	void Emit<T>(in T e) where T : unmanaged;

	/// <summary>
	/// Creates a reader of <typeparamref name="T"/> that starts at the oldest visible event. Call it once (constructor or
	/// field initializer) and keep the reader in a mutable field.
	/// </summary>
	[ReadsEvent]
	EventReader<T> Reader<T>() where T : unmanaged;
}

/// <summary>Convenience members of <see cref="IEvents"/>.</summary>
public static class EventsExtensions
{
	/// <summary>Emits the default value of <typeparamref name="T"/> (for events without data, such as <see cref="ExitGameEvent"/>).</summary>
	[EmitsEvent]
	public static void Emit<T>(this IEvents events) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(events);
		events.Emit(default(T));
	}
}

/// <summary>
/// Marks a method that emits an event, so the Ion source generator counts its call sites as emits: of the method's type
/// argument when no type is given, otherwise of <see cref="EventType"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class EmitsEventAttribute : Attribute
{
	/// <summary>The method emits its (first) type argument.</summary>
	public EmitsEventAttribute() { }

	/// <summary>The method emits <paramref name="eventType"/>.</summary>
	public EmitsEventAttribute(Type eventType) => EventType = eventType;

	/// <summary>The event type, or <see langword="null"/> for the method's type argument.</summary>
	public Type? EventType { get; }
}

/// <summary>
/// Marks a method that reads (or creates a reader of) an event, so the Ion source generator counts its call sites as
/// reads: of the method's type argument when no type is given, otherwise of <see cref="EventType"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ReadsEventAttribute : Attribute
{
	/// <summary>The method reads its (first) type argument.</summary>
	public ReadsEventAttribute() { }

	/// <summary>The method reads <paramref name="eventType"/>.</summary>
	public ReadsEventAttribute(Type eventType) => EventType = eventType;

	/// <summary>The event type, or <see langword="null"/> for the method's type argument.</summary>
	public Type? EventType { get; }
}

/// <summary>How an assembly uses an event type (see <see cref="EventUsageAttribute"/>).</summary>
[Flags]
public enum EventUsage
{
	/// <summary>Not used.</summary>
	None = 0,
	/// <summary>Emitted somewhere in the assembly.</summary>
	Emitted = 1,
	/// <summary>Read (or a reader is created) somewhere in the assembly.</summary>
	Read = 2,
	/// <summary>Emitted from a per-frame stage method (FixedUpdate or Update): the channel starts larger.</summary>
	EmittedPerFrame = 4,
	/// <summary>Emitted in a loop inside a per-frame stage method: the channel starts larger still.</summary>
	EmittedInLoop = 8,
}

/// <summary>
/// Written by the Ion source generator into assemblies that emit or read events: how the assembly uses
/// <see cref="EventType"/>, so an application compiled against it includes the type in its generated event bus and does
/// not report it as never read (ION101) or never emitted (ION102).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class EventUsageAttribute(Type eventType, EventUsage usage) : Attribute
{
	/// <summary>The event type.</summary>
	public Type EventType { get; } = eventType;

	/// <summary>How the assembly uses it.</summary>
	public EventUsage Usage { get; } = usage;
}
