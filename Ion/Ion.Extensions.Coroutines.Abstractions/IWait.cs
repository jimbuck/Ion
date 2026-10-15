namespace Ion;

/// <summary>
/// A custom wait condition, for conditions that <see cref="Wait"/> does not cover. Yield it from a coroutine directly or
/// through <see cref="Wait.For(IWait)"/>.
/// </summary>
/// <remarks>
/// The built-in waits (<see cref="Wait.For(float)"/>, <see cref="Wait.Until"/>, <see cref="Wait.While"/> and
/// <see cref="Wait.For{TEvent}"/>) are stored inline in the coroutine and allocate nothing. A custom wait is kept by
/// reference, so implement it as a class (a struct would be boxed on every yield). The runner calls
/// <see cref="Update"/> once per frame, then reads <see cref="IsReady"/>.
/// </remarks>
public interface IWait
{
	/// <summary>True once the coroutine may resume.</summary>
	bool IsReady { get; }

	/// <summary>Advances the wait by one frame.</summary>
	/// <param name="dt">The frame time.</param>
	/// <param name="events">The coroutine's own event cursors (one reader per event type).</param>
	void Update(GameTime dt, EventReaderSet events);
}
