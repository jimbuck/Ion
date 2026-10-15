using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;

namespace Ion.Testing;

/// <summary>
/// The outcome of <see cref="IonTestHost.Run{TGame}"/>: the frames run, the last frame's stats, the game counters, the
/// last rendered image (with headless rendering), and the still running host for inspecting state. Dispose it to run the
/// Destroy stage and release the game.
/// </summary>
public sealed class IonRunResult : IDisposable
{
	internal IonRunResult(IonTestHost host, int frames)
	{
		Host = host;
		Frames = frames;
		LastFrame = host.LastFrame;

		var counters = new Dictionary<string, double>(StringComparer.Ordinal);
		if (host.Services.GetService<IMetrics>() is { } metrics)
		{
			foreach (var instrument in metrics.Instruments)
			{
				switch (instrument)
				{
					case MetricsCounter c: counters[c.Name] = c.Value; break;
					case MetricsGauge g: counters[g.Name] = g.Value; break;
					case MetricsHistogram h: counters[h.Name] = h.TotalCount; break;
				}
			}
		}

		Counters = counters;

		if (host.Services.GetService<IScreenshotSource>() is { } source && frames > 0)
		{
			try
			{
				Image = source.Capture();
			}
			catch (InvalidOperationException)
			{
				Image = null;
			}
		}
	}

	/// <summary>The host, still running: step it further, inject input, resolve services.</summary>
	public IonTestHost Host { get; }

	/// <summary>The number of frames run (fewer than asked when the game exited early).</summary>
	public int Frames { get; }

	/// <summary>Whether the game asked to exit.</summary>
	public bool Exited => Host.IsExitRequested;

	/// <summary>The stats of the last frame: draw calls, sprites, entities, events, allocations.</summary>
	public FrameStats LastFrame { get; }

	/// <summary>Every game counter, gauge (value) and histogram (total count) by name, at the end of the run.</summary>
	public IReadOnlyDictionary<string, double> Counters { get; }

	/// <summary>The last rendered frame, with headless rendering on (<c>host.WithRendering()</c>); null otherwise.</summary>
	public Screenshot? Image { get; }

	/// <summary>A required service of the game (its state).</summary>
	public T Get<T>() where T : notnull => Host.Get<T>();

	/// <summary>
	/// The ECS world (the most recent live one, as the remote protocol picks) as normalized JSON (see
	/// <see cref="WorldSnapshot"/>), or null when the game has no ECS module.
	/// </summary>
	public string? WorldJson(int decimals = JsonSnapshot.DefaultDecimals)
	{
		var worlds = Host.Services.GetService<EcsWorlds>();
		if (worlds is null) return null;
		_ = worlds.Root;
		var registry = Host.Services.GetService<ComponentSerializerRegistry>();
		return WorldSnapshot.ToJson(worlds.Worlds[^1], registry, decimals);
	}

	/// <summary>Runs the Destroy stage and disposes the game.</summary>
	public void Dispose() => Host.Dispose();
}
