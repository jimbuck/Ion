using System.Collections;

namespace Ion.Testing;

internal interface IEventCollector : IDisposable
{
	void Poll(uint frame);
}

/// <summary>
/// The events of type <typeparamref name="T"/> recorded by <see cref="IonTestHost.Collect{T}"/>, oldest first, with the
/// frame each was recorded in.
/// </summary>
public sealed class EventCollector<T> : IReadOnlyList<T>, IEventCollector where T : unmanaged
{
	private EventReader<T> _reader;
	private bool _disposed;
	private readonly List<T> _events = [];
	private readonly List<uint> _frames = [];

	internal EventCollector(EventReader<T> reader)
	{
		_reader = reader;
	}

	/// <summary>The number of events recorded.</summary>
	public int Count => _events.Count;

	/// <summary>The <paramref name="index"/>th recorded event.</summary>
	public T this[int index] => _events[index];

	/// <summary>The frame (<see cref="GameTime.Frame"/>) in which each event was recorded, parallel to this list.</summary>
	public IReadOnlyList<uint> Frames => _frames;

	/// <summary>The most recent event, if any.</summary>
	public T? Last => _events.Count > 0 ? _events[^1] : null;

	/// <summary>Forgets the recorded events.</summary>
	public void Clear()
	{
		_events.Clear();
		_frames.Clear();
	}

	/// <inheritdoc/>
	public IEnumerator<T> GetEnumerator() => _events.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	void IEventCollector.Poll(uint frame)
	{
		if (_disposed) return;

		foreach (ref readonly var e in _reader.Read())
		{
			_events.Add(e);
			_frames.Add(frame);
		}
	}

	/// <summary>Stops recording.</summary>
	public void Dispose() => _disposed = true;
}
