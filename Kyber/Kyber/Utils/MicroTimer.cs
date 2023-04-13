using System.Diagnostics;

namespace Kyber.Utils;

public static class MicroTimer
{
	private static readonly ConcurrentDictionary<string, Stopwatch> _watches = new();
	private static readonly ConcurrentDictionary<string, float[]> _timings = new();
	private static readonly ConcurrentDictionary<string, int> _indicies = new();

	public static IDisposable Start(string name, int maxRecords = 32)
	{
		if (!_watches.ContainsKey(name))
		{
			_watches.TryAdd(name, new Stopwatch());
			_timings.TryAdd(name, new float[maxRecords]);
			_indicies.TryAdd(name, 0);
		}
		var instance = new MicroTimerInstance(name);

		_watches[name].Restart();
		return instance;
	}

	public static void Timings(ref Dictionary<string, float> target)
	{
		foreach(var key in _timings.Keys)
		{
			target[key] = _timings[key].Average();
		}
	}

	public static void Clear()
	{
		_watches.Clear();
		_timings.Clear();
		_indicies.Clear();
	}

	private static void _stop(string name)
	{
		if (!_watches.TryGetValue(name, out var watch)) return;
		watch.Stop();
		_timings[name][_indicies[name]] = (float)watch.Elapsed.TotalSeconds;
		_indicies[name] = (_indicies[name] + 1) % _timings[name].Length;
	}

	private readonly struct MicroTimerInstance : IDisposable
	{
		private readonly string _name;

		public MicroTimerInstance(string name)
		{
			_name = name;
		}

		public void Dispose()
		{
			_stop(_name);
		}
	}
}
