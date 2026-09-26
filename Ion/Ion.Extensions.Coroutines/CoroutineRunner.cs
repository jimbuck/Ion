using System.Collections;

namespace Ion.Extensions.Coroutines;

/// <summary>
/// Default <see cref="ICoroutineRunner"/>. Registered as a singleton by <see cref="BuilderExtensions.AddCoroutines"/>.
/// </summary>
/// <remarks>
/// Coroutines are stepped either by <see cref="CoroutineSystem"/> (added with <see cref="BuilderExtensions.UseCoroutines"/>)
/// or by calling <see cref="Update"/> manually. When both are used, only the first of the two to run in a given
/// frame (by <see cref="GameTime.Frame"/>) steps the coroutines, so a manual call next to the system is harmless.
/// Repeated manual calls within one frame still step every time, as before.
/// Each running coroutine owns an <see cref="EventReaderSet"/> over the application's <see cref="IEvents"/> (its own
/// cursors, so an event resumes each waiting coroutine once and a later wait does not see it again).
/// <para>
/// The current <see cref="Wait"/> of each coroutine is stored inline in its handle (a struct union, see <see cref="Wait"/>),
/// so stepping allocates nothing for <c>IEnumerator&lt;Wait&gt;</c> coroutines. A non-generic <see cref="IEnumerator"/>
/// coroutine works the same way, but the routine itself boxes every struct it yields.
/// </para>
/// </remarks>
public class CoroutineRunner(IEvents events) : ICoroutineRunner, IDisposable
{
	private readonly List<CoroutineHandle> _routines = [];

	// Index of the routine being stepped by Update, or -1 when not stepping. Adjusted when routines are removed mid-step.
	private int _current = -1;

	private long _lastManualFrame = -1;
	private long _lastSystemFrame = -1;

	/// <inheritdoc/>
	public int Count => _routines.Count;

	/// <inheritdoc/>
	public void Start(IEnumerator routine)
	{
		_routines.Add(new CoroutineHandle(routine, new EventReaderSet(events)));
	}

	/// <inheritdoc/>
	/// <remarks>Stopping a routine that is not running (or already finished) is a no-op.</remarks>
	public void Stop(IEnumerator routine)
	{
		var index = _indexOf(routine);
		if (index < 0) return;

		_removeAt(index);
	}

	/// <inheritdoc/>
	public void StopAll()
	{
		foreach (var routine in _routines)
		{
			routine.IsStopped = true;
		}
		_routines.Clear();
		if (_current >= 0) _current = -1;
	}

	/// <summary>
	/// Stops every coroutine.
	/// </summary>
	public void Dispose()
	{
		StopAll();
		GC.SuppressFinalize(this);
	}

	/// <inheritdoc/>
	public bool IsActive(IEnumerator routine) => _indexOf(routine) >= 0;

	/// <inheritdoc/>
	public void Update(GameTime dt)
	{
		// The CoroutineSystem already stepped this frame.
		if (_lastSystemFrame == dt.Frame) return;

		_lastManualFrame = dt.Frame;
		_step(dt);
	}

	/// <summary>
	/// Called by <see cref="CoroutineSystem"/>. Skips the frame if <see cref="Update"/> was already called manually for it.
	/// </summary>
	internal void SystemUpdate(GameTime dt)
	{
		if (_lastManualFrame == dt.Frame) return;

		_lastSystemFrame = dt.Frame;
		_step(dt);
	}

	private void _step(GameTime dt)
	{
		if (_routines.Count == 0) return;

		for (_current = 0; _current < _routines.Count; _current++)
		{
			var handle = _routines[_current];

			if (handle.IsReady(dt))
			{
				var alive = _moveNext(handle, handle.Enumerator);

				// The routine stopped itself (or was stopped) while running.
				if (handle.IsStopped) continue;

				if (!alive) _removeAt(_current);
			}
		}

		_current = -1;
	}

	private int _indexOf(IEnumerator routine)
	{
		for (var i = 0; i < _routines.Count; i++)
		{
			if (ReferenceEquals(_routines[i].Enumerator, routine)) return i;
		}

		return -1;
	}

	private void _removeAt(int index)
	{
		var handle = _routines[index];
		handle.IsStopped = true;
		_routines.RemoveAt(index);

		if (_current >= 0 && index <= _current) _current--;
	}

	private static bool _moveNext(CoroutineHandle handle, IEnumerator routine)
	{
		// A routine that yielded a nested routine resumes only once the nested one (and its own nesting) finishes.
		var current = _currentOf(routine);
		if (current.Kind == WaitKind.Routine && current.Routine is { } nested)
		{
			if (_moveNext(handle, nested)) return true;

			handle.SetWait(default);
		}

		var alive = routine.MoveNext();
		handle.SetWait(alive ? _currentOf(routine) : default);

		return alive;
	}

	// IEnumerator<Wait> is read without boxing; a non-generic routine's Current is already an object (boxed by the routine).
	private static Wait _currentOf(IEnumerator routine) => routine is IEnumerator<Wait> typed ? typed.Current : Wait.FromYield(routine.Current);

	private sealed class CoroutineHandle(IEnumerator enumerator, EventReaderSet events)
	{
		// The current wait, stored inline, and its running state.
		private Wait _wait;
		private float _remaining;
		private bool _eventSeen;

		public IEnumerator Enumerator { get; } = enumerator;
		public EventReaderSet Events { get; } = events;
		public bool IsStopped { get; set; }

		public void SetWait(in Wait wait)
		{
			_wait = wait;
			_remaining = wait.Seconds;
			_eventSeen = false;
		}

		/// <summary>Advances the current wait by one frame and returns whether the routine may resume.</summary>
		public bool IsReady(GameTime dt)
		{
			switch (_wait.Kind)
			{
				case WaitKind.Seconds:
					_remaining -= dt.Delta;
					return _remaining <= 0f;
				case WaitKind.Until:
					return _wait.Predicate!();
				case WaitKind.While:
					return !_wait.Predicate!();
				case WaitKind.Event:
					if (!_eventSeen && _wait.PollEvent(Events)) _eventSeen = true;
					return _eventSeen;
				case WaitKind.Custom:
					var custom = _wait.Custom!;
					custom.Update(dt, Events);
					return custom.IsReady;
				default:
					// None, and Routine: the nested routine is stepped by _moveNext.
					return true;
			}
		}
	}
}
