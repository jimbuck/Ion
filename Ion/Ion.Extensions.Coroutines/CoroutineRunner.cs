using Microsoft.Extensions.DependencyInjection;

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
/// </remarks>
public class CoroutineRunner(IServiceProvider services) : ICoroutineRunner
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
		_routines.Add(new CoroutineHandle(routine, services.GetRequiredService<IEventListener>()));
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
			routine.EventListener?.Dispose();
		}
		_routines.Clear();
		if (_current >= 0) _current = -1;
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

			handle.Wait?.Update(dt, handle.EventListener);

			if (handle.Wait is null || handle.Wait.IsReady)
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
		handle.EventListener?.Dispose();
		_routines.RemoveAt(index);

		if (_current >= 0 && index <= _current) _current--;
	}

	private static bool _moveNext(CoroutineHandle handle, IEnumerator routine)
	{
		if (routine.Current is IEnumerator enumerator)
		{
			if (_moveNext(handle, enumerator)) return true;

			handle.Wait = new WaitFor(0);
		}

		bool result = routine.MoveNext();

		if (routine.Current is float delay) handle.Wait = new WaitFor(delay);
		else if (routine.Current is IWait wait) handle.Wait = wait;

		return result;
	}

	private sealed class CoroutineHandle(IEnumerator enumerator, IEventListener eventListener)
	{
		public IEnumerator Enumerator { get; } = enumerator;
		public IEventListener EventListener { get; } = eventListener;
		public IWait? Wait { get; set; }
		public bool IsStopped { get; set; }
	}
}
