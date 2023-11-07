using Microsoft.Extensions.DependencyInjection;

using System.Collections;

namespace Kyber;

public class CoroutineRunner : ICoroutineRunner
{
	private readonly List<CoroutineHandle> _routines = new();
	private readonly IServiceProvider _services;

	/// <summary>
	/// How many coroutines are currently active.
	/// </summary>
	public int Count => _routines.Count;

	public CoroutineRunner(IServiceProvider services)
	{
		_services = services;
	}

	/// <summary>
	/// Run a coroutine.
	/// </summary>
	/// <returns>A handle to the new coroutine.</returns>
	/// <param name="delay">How many seconds to delay before starting.</param>
	/// <param name="routine">The routine to run.</param>
	public void Start(IEnumerator routine)
	{
		_routines.Add(new CoroutineHandle(routine, _services.GetRequiredService<IEventListener>()));
	}

	/// <summary>
	/// Stop the specified routine.
	/// </summary>
	/// <returns>True if the routine was actually stopped.</returns>
	/// <param name="routine">The routine to stop.</param>
	public void Stop(IEnumerator routine)
	{
		var index = _routines.FindIndex(ch => ch.Enumerator == routine);
		_routines[index].EventListener?.Dispose();
		_routines.RemoveAt(index);
	}

	/// <summary>
	/// Stop all running routines.
	/// </summary>
	public void StopAll()
	{
		foreach(var routine in _routines) routine.EventListener?.Dispose();
		_routines.Clear();
	}

	/// <summary>
	/// Check if the routine is currently active.
	/// </summary>
	/// <returns>True if the routine is active.</returns>
	/// <param name="routine">The routine to check.</param>
	public bool IsActive(IEnumerator routine)
	{
		return _routines.Any(ch => ch.Enumerator == routine);
	}

	/// <summary>
	/// Update all running coroutines.
	/// </summary>
	/// <returns>True if any routines were updated.</returns>
	/// <param name="deltaTime">How many seconds have passed sinced the last update.</param>
	public void Update(GameTime dt)
	{
		if (_routines.Count == 0) return;

		for (int i = 0; i < _routines.Count; i++)
		{
			_routines[i].Wait?.Update(dt, _routines[i].EventListener);

			if (_routines[i].Wait is null || _routines[i].Wait!.IsReady)
			{
				if (!_moveNext(_routines[i].Enumerator, i))
				{
					_routines[i].EventListener?.Dispose();
					_routines.RemoveAt(i--);
				}
			}
		}
	}

	private bool _moveNext(IEnumerator routine, int index)
	{
		if (routine.Current is IEnumerator enumerator)
		{
			if (_moveNext(enumerator, index)) return true;

			_routines[index].Wait = new WaitFor(0);
		}

		bool result = routine.MoveNext();

		if (routine.Current is float delay) _routines[index].Wait = new WaitFor(delay);
		else if (routine.Current is IWait wait) _routines[index].Wait = wait;

		return result;
	}

	private class CoroutineHandle
	{
		public IEnumerator Enumerator { get; }
		public IEventListener EventListener { get; }
		public IWait? Wait { get; set; }

		public CoroutineHandle(IEnumerator enumerator, IEventListener eventListener)
		{
			Enumerator = enumerator;
			EventListener = eventListener;
		}
	}
}