﻿using Microsoft.Extensions.DependencyInjection;

using System.Collections;

namespace Ion;

public class CoroutineRunner(IServiceProvider services) : ICoroutineRunner
{
	private readonly List<CoroutineHandle> _routines = [];

	/// <inheritdoc/>
	public int Count => _routines.Count;

	/// <inheritdoc/>
	public void Start(IEnumerator routine)
	{
		_routines.Add(new CoroutineHandle(routine, services.GetRequiredService<IEventListener>()));
	}

	/// <inheritdoc/>
	public void Stop(IEnumerator routine)
	{
		var index = _routines.FindIndex(ch => ch.Enumerator == routine);
		_routines[index].EventListener?.Dispose();
		_routines.RemoveAt(index);
	}

	/// <inheritdoc/>
	public void StopAll()
	{
		foreach(var routine in _routines) routine.EventListener?.Dispose();
		_routines.Clear();
	}

	/// <inheritdoc/>
	public bool IsActive(IEnumerator routine)
	{
		return _routines.Any(ch => ch.Enumerator == routine);
	}

	/// <inheritdoc/>
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

	private class CoroutineHandle(IEnumerator enumerator, IEventListener eventListener)
	{
		public IEnumerator Enumerator { get; } = enumerator;
		public IEventListener EventListener { get; } = eventListener;
		public IWait? Wait { get; set; }
	}
}