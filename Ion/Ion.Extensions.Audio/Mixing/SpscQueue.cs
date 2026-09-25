using System.Numerics;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Audio;

/// <summary>
/// A bounded, lock-free, single-producer single-consumer ring buffer. One thread may call <see cref="TryEnqueue"/> and
/// one (other) thread <see cref="TryDequeue"/>; neither allocates or blocks.
/// </summary>
internal sealed class SpscQueue<T> where T : struct
{
	private readonly T[] _items;
	private readonly int _mask;

	// Producer and consumer indices on separate cache lines, so the two threads do not false-share.
	private PaddedLong _head; // next index to read, written by the consumer
	private PaddedLong _tail; // next index to write, written by the producer

	public SpscQueue(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		capacity = (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
		_items = new T[capacity];
		_mask = capacity - 1;
	}

	public int Capacity => _items.Length;

	/// <summary>An estimate of the number of queued items (exact when called from either thread while the other is idle).</summary>
	public int Count => (int)(Volatile.Read(ref _tail.Value) - Volatile.Read(ref _head.Value));

	/// <summary>Producer only. Returns false when the queue is full.</summary>
	public bool TryEnqueue(in T item)
	{
		var tail = _tail.Value;
		if (tail - Volatile.Read(ref _head.Value) >= _items.Length) return false;

		_items[(int)(tail & _mask)] = item;
		Volatile.Write(ref _tail.Value, tail + 1);
		return true;
	}

	/// <summary>Consumer only. Returns false when the queue is empty.</summary>
	public bool TryDequeue(out T item)
	{
		var head = _head.Value;
		if (head == Volatile.Read(ref _tail.Value))
		{
			item = default;
			return false;
		}

		ref var slot = ref _items[(int)(head & _mask)];
		item = slot;
		slot = default; // drop references (sounds) held by the slot
		Volatile.Write(ref _head.Value, head + 1);
		return true;
	}
}

/// <summary>A long alone on its cache line (64 bytes of padding on each side).</summary>
[StructLayout(LayoutKind.Explicit, Size = 136)]
internal struct PaddedLong
{
	[FieldOffset(64)] public long Value;
}
