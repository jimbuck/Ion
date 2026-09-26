using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// Obsolete adapter: <see cref="IEventEmitter"/> over <see cref="IEvents"/>. Kept for one release.
/// </summary>
[Obsolete(EventAdapterMessages.Emitter)]
public class EventEmitter : IEventEmitter
{
	/// <summary>Creates an emitter over a new <see cref="EventBus"/> that is not tied to a game loop.</summary>
	public EventEmitter() : this(new EventBus()) { }

	/// <summary>Creates an emitter over <paramref name="events"/>.</summary>
	public EventEmitter(IEvents events)
	{
		ArgumentNullException.ThrowIfNull(events);
		Events = events;
	}

	/// <summary>The bus this adapter emits to.</summary>
	public IEvents Events { get; }

	/// <summary>Ends the frame on the underlying <see cref="EventBus"/> (no-op for other <see cref="IEvents"/>).</summary>
	public void Step() => (Events as EventBus)?.Step();

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>() where T : unmanaged => Events.Emit(default(T));

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Emit<T>(T data) where T : unmanaged => Events.Emit(in data);
}

internal static class EventAdapterMessages
{
	public const string Emitter = "EventEmitter is an adapter over IEvents and will be removed in the next release. Inject IEvents and call Emit<T>(in T).";
	public const string Listener = "EventListener is an adapter over IEvents and will be removed in the next release. Inject IEvents and keep an EventReader<T> from Reader<T>(), created once in the constructor, in a field that is not readonly.";
}
