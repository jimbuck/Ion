using System.Runtime.InteropServices;

namespace Ion.Extensions.Http;

/// <summary>
/// A bounded, lock-free, multi-producer queue of references (Dmitry Vyukov's bounded queue): server threads enqueue work
/// for the game thread, which dequeues it at a stage boundary. Neither side allocates or blocks; a full queue refuses the
/// item (the caller answers 503).
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class BoundedQueue<T> where T : class
{
	private readonly Cell[] _cells;
	private readonly int _mask;
	private PaddedLong _enqueue;
	private PaddedLong _dequeue;

	/// <summary>Creates a queue holding up to <paramref name="capacity"/> items (rounded up to a power of two).</summary>
	public BoundedQueue(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
		var size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)capacity);
		_cells = new Cell[size];
		for (var i = 0; i < size; i++) _cells[i].Sequence = i;
		_mask = size - 1;
	}

	/// <summary>The capacity.</summary>
	public int Capacity => _cells.Length;

	/// <summary>An estimate of the number of queued items.</summary>
	public int Count => (int)Math.Max(0, Volatile.Read(ref _enqueue.Value) - Volatile.Read(ref _dequeue.Value));

	/// <summary>Adds <paramref name="item"/>; false when the queue is full. Safe from any thread.</summary>
	public bool TryEnqueue(T item)
	{
		ArgumentNullException.ThrowIfNull(item);
		var position = Volatile.Read(ref _enqueue.Value);
		while (true)
		{
			ref var cell = ref _cells[position & _mask];
			var sequence = Volatile.Read(ref cell.Sequence);
			var diff = sequence - position;
			if (diff == 0)
			{
				if (Interlocked.CompareExchange(ref _enqueue.Value, position + 1, position) == position)
				{
					cell.Item = item;
					Volatile.Write(ref cell.Sequence, position + 1);
					return true;
				}
			}
			else if (diff < 0)
			{
				return false;
			}

			position = Volatile.Read(ref _enqueue.Value);
		}
	}

	/// <summary>Removes the oldest item; false when the queue is empty. Safe from any thread (the game thread is the only consumer in Ion).</summary>
	public bool TryDequeue(out T item)
	{
		var position = Volatile.Read(ref _dequeue.Value);
		while (true)
		{
			ref var cell = ref _cells[position & _mask];
			var sequence = Volatile.Read(ref cell.Sequence);
			var diff = sequence - (position + 1);
			if (diff == 0)
			{
				if (Interlocked.CompareExchange(ref _dequeue.Value, position + 1, position) == position)
				{
					item = cell.Item!;
					cell.Item = null;
					Volatile.Write(ref cell.Sequence, position + _cells.Length);
					return true;
				}
			}
			else if (diff < 0)
			{
				item = null!;
				return false;
			}

			position = Volatile.Read(ref _dequeue.Value);
		}
	}

	private struct Cell
	{
		public long Sequence;
		public T? Item;
	}
}

/// <summary>A long alone on its cache line (explicit layout cannot be nested in a generic type).</summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
	[FieldOffset(64)]
	public long Value;
}
