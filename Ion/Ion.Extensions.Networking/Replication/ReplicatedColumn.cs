using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Arch.Core;

namespace Ion.Extensions.Networking;

/// <summary>
/// The values of one replicated component type in one snapshot frame: a flat array indexed by the entities' dense slots
/// and a presence bit per slot. Its virtual members are the only place the component type is known; the frame, the
/// codec and the world work through them.
/// </summary>
internal abstract class ReplicatedColumn
{
	protected ReplicatedColumn(ReplicatedTypeInfo info) => Info = info;

	public ReplicatedTypeInfo Info { get; }

	public int Ordinal => Info.Ordinal;

	public abstract int Capacity { get; }

	public abstract void EnsureCapacity(int slots);

	public abstract bool Has(int slot);

	public abstract void Remove(int slot);

	/// <summary>Clears the presence of the first <paramref name="slotCount"/> slots.</summary>
	public abstract void Clear(int slotCount);

	/// <summary>Copies values and presence of the first <paramref name="slotCount"/> slots from <paramref name="other"/>.</summary>
	public abstract void CopyFrom(ReplicatedColumn other, int slotCount);

	/// <summary>Whether both columns have the slot with bitwise equal values.</summary>
	public abstract bool SameAs(ReplicatedColumn other, int slot);

	public abstract void WriteFull(int slot, ref NetWriter writer);

	/// <summary>Writes the delta of the slot against <paramref name="baseline"/>'s value; false (nothing written) if unchanged.</summary>
	public abstract bool WriteDelta(ReplicatedColumn baseline, int slot, ref NetWriter writer);

	public abstract void ReadFull(int slot, ref NetReader reader);

	/// <summary>Reads a delta against the slot's current value (the baseline's copy).</summary>
	public abstract void ReadDelta(int slot, ref NetReader reader);

	/// <summary>
	/// Whether the 64 slots of <paramref name="block"/> have the same presence and the same bytes as in
	/// <paramref name="other"/> (a fast, vectorized check that lets the encoder and the client skip unchanged blocks; a
	/// false answer only means the slots are compared one by one).
	/// </summary>
	public abstract bool BlockEqual(ReplicatedColumn other, int block);

	/// <summary>
	/// Sets in <paramref name="bits"/> every slot below <paramref name="slotCount"/> whose presence or bytes differ from
	/// <paramref name="other"/> (a non-virtual, block-vectorized pass, so the encoder and the client only look at changed
	/// slots; bytes that differ with equal serialized members only cost a comparison later).
	/// </summary>
	public abstract void MarkChanged(ReplicatedColumn other, ulong[] bits, int slotCount);

	/// <summary>Captures the component of every networked entity of <paramref name="world"/> whose id is <paramref name="ids"/>[slot] into its slot.</summary>
	public abstract void Capture(World world, uint[] ids);

	/// <summary>Collects the entities with the component but without a <see cref="NetworkId"/> (nor <see cref="NetworkLocal"/>).</summary>
	public abstract void CollectUnidentified(World world, List<Entity> into);

	/// <summary>Writes the slot's value to <paramref name="entity"/> (adding the component if missing).</summary>
	public abstract void ApplyTo(World world, Entity entity, int slot);

	public abstract void RemoveFrom(World world, Entity entity);

	/// <summary>Writes the value of the slot blended between <paramref name="a"/> and <paramref name="b"/> to <paramref name="entity"/>.</summary>
	public abstract void InterpolateTo(World world, Entity entity, ReplicatedColumn a, ReplicatedColumn b, int slot, float t);

	/// <summary>Captures <paramref name="entity"/>'s component (if it has one) into <paramref name="slot"/>.</summary>
	public abstract void CaptureEntity(World world, Entity entity, int slot);

	/// <summary>Reads one full value from <paramref name="reader"/> and writes it to <paramref name="entity"/> (owner updates).</summary>
	public abstract void ReadInto(World world, Entity entity, ref NetReader reader);

	/// <summary>Writes the full value of <paramref name="entity"/>'s component; false if it has none.</summary>
	public abstract bool WriteEntity(World world, Entity entity, ref NetWriter writer);
}

/// <summary>The column of <typeparamref name="T"/>.</summary>
internal sealed class ReplicatedColumn<T> : ReplicatedColumn where T : unmanaged
{
	private static readonly QueryDescription Identified = new QueryDescription().WithAll<NetworkId, T>();
	private static readonly QueryDescription Unidentified = new QueryDescription().WithAll<T>().WithNone<NetworkId, NetworkLocal>();

	private readonly NetSerializer<T> _serializer;
	private T[] _values;
	private ulong[] _present;

	public ReplicatedColumn(ReplicatedTypeInfo<T> info, int capacity) : base(info)
	{
		_serializer = info.Serializer;
		_values = new T[Math.Max(64, capacity)];
		_present = new ulong[_values.Length / 64];
	}

	public NetSerializer<T> Serializer => _serializer;

	public override int Capacity => _values.Length;

	public T[] Values => _values;

	public override void EnsureCapacity(int slots)
	{
		if (slots <= _values.Length) return;
		var size = _values.Length;
		while (size < slots) size *= 2;
		Array.Resize(ref _values, size);
		Array.Resize(ref _present, size / 64);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override bool Has(int slot) => (uint)slot < (uint)_values.Length && (_present[slot >> 6] & (1UL << slot)) != 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T At(int slot) => ref _values[slot];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Set(int slot, in T value)
	{
		_values[slot] = value;
		_present[slot >> 6] |= 1UL << slot;
	}

	public override void Remove(int slot)
	{
		if ((uint)slot < (uint)_values.Length) _present[slot >> 6] &= ~(1UL << slot);
	}

	public override void Clear(int slotCount)
	{
		var words = Math.Min((slotCount + 63) >> 6, _present.Length);
		Array.Clear(_present, 0, words);
	}

	public override void CopyFrom(ReplicatedColumn other, int slotCount)
	{
		var source = (ReplicatedColumn<T>)other;
		EnsureCapacity(slotCount);
		Array.Copy(source._values, _values, Math.Min(slotCount, source._values.Length));
		var words = Math.Min((slotCount + 63) >> 6, source._present.Length);
		Array.Copy(source._present, _present, words);
	}

	public override bool SameAs(ReplicatedColumn other, int slot)
	{
		var source = (ReplicatedColumn<T>)other;
		return Has(slot) && source.Has(slot) && _serializer.Equal(_values[slot], source._values[slot]);
	}

	public override void WriteFull(int slot, ref NetWriter writer) => _serializer.Write(ref writer, _values[slot]);

	public override bool WriteDelta(ReplicatedColumn baseline, int slot, ref NetWriter writer) =>
		_serializer.WriteDelta(ref writer, ((ReplicatedColumn<T>)baseline)._values[slot], _values[slot]);

	public override void ReadFull(int slot, ref NetReader reader)
	{
		var value = _serializer.Read(ref reader);
		if (!reader.Failed) Set(slot, value);
	}

	public override void ReadDelta(int slot, ref NetReader reader)
	{
		var value = _serializer.ReadDelta(ref reader, _values[slot]);
		if (!reader.Failed) Set(slot, value);
	}

	public override bool BlockEqual(ReplicatedColumn other, int block)
	{
		var source = (ReplicatedColumn<T>)other;
		var start = block << 6;
		if (start + 64 > _values.Length || start + 64 > source._values.Length) return false;
		if (_present[block] != source._present[block]) return false;
		return MemoryMarshal.AsBytes(_values.AsSpan(start, 64)).SequenceEqual(MemoryMarshal.AsBytes(source._values.AsSpan(start, 64)));
	}

	public override void MarkChanged(ReplicatedColumn other, ulong[] bits, int slotCount)
	{
		var source = (ReplicatedColumn<T>)other;
		var words = Math.Min((slotCount + 63) >> 6, bits.Length);
		for (var w = 0; w < words; w++)
		{
			var mine = w < _present.Length ? _present[w] : 0;
			var theirs = w < source._present.Length ? source._present[w] : 0;
			var changed = mine ^ theirs;
			var both = mine & theirs;
			if (both != 0)
			{
				var start = w << 6;
				if (start + 64 <= _values.Length && start + 64 <= source._values.Length
					&& MemoryMarshal.AsBytes(_values.AsSpan(start, 64)).SequenceEqual(MemoryMarshal.AsBytes(source._values.AsSpan(start, 64))))
				{
					both = 0;
				}

				while (both != 0)
				{
					var bit = BitOperations.TrailingZeroCount(both);
					both &= both - 1;
					var slot = start + bit;
					if (!MemoryMarshal.AsBytes(_values.AsSpan(slot, 1)).SequenceEqual(MemoryMarshal.AsBytes(source._values.AsSpan(slot, 1)))) changed |= 1UL << bit;
				}
			}

			bits[w] |= changed;
		}
	}

	public override void Capture(World world, uint[] frameIds)
	{
		foreach (ref var chunk in world.Query(in Identified))
		{
			var ids = chunk.GetSpan<NetworkId>();
			var values = chunk.GetSpan<T>();
			var count = chunk.Count;
			for (var i = 0; i < count; i++)
			{
				var slot = ids[i].Slot;

				// A stale id (its slot was freed and reused) is not captured.
				if ((uint)slot >= (uint)frameIds.Length || frameIds[slot] != ids[i].Id) continue;
				if (slot >= _values.Length) EnsureCapacity(slot + 1);
				_values[slot] = values[i];
				_present[slot >> 6] |= 1UL << slot;
			}
		}
	}

	public override void CollectUnidentified(World world, List<Entity> into)
	{
		if (world.CountEntities(in Unidentified) == 0) return;
		foreach (ref var chunk in world.Query(in Unidentified))
		{
			ref var entities = ref chunk.Entity(0);
			var count = chunk.Count;
			for (var i = 0; i < count; i++) into.Add(Unsafe.Add(ref entities, i));
		}
	}

	public override void ApplyTo(World world, Entity entity, int slot)
	{
		ref readonly var value = ref _values[slot];
		ref var current = ref world.TryGetRef<T>(entity, out var exists);
		if (exists) current = value;
		else world.Add(entity, value);
	}

	public override void RemoveFrom(World world, Entity entity)
	{
		if (world.Has<T>(entity)) world.Remove<T>(entity);
	}

	public override void InterpolateTo(World world, Entity entity, ReplicatedColumn a, ReplicatedColumn b, int slot, float t)
	{
		ref var current = ref world.TryGetRef<T>(entity, out var exists);
		if (!exists) return;
		var from = (ReplicatedColumn<T>)a;
		var to = (ReplicatedColumn<T>)b;
		current = _serializer.Interpolate(from._values[slot], to._values[slot], t);
	}

	public override void CaptureEntity(World world, Entity entity, int slot)
	{
		ref var current = ref world.TryGetRef<T>(entity, out var exists);
		if (!exists)
		{
			Remove(slot);
			return;
		}

		EnsureCapacity(slot + 1);
		Set(slot, current);
	}

	public override void ReadInto(World world, Entity entity, ref NetReader reader)
	{
		var value = _serializer.Read(ref reader);
		if (reader.Failed) return;
		ref var current = ref world.TryGetRef<T>(entity, out var exists);
		if (exists) current = value;
		else world.Add(entity, value);
	}

	public override bool WriteEntity(World world, Entity entity, ref NetWriter writer)
	{
		ref var current = ref world.TryGetRef<T>(entity, out var exists);
		if (!exists) return false;
		_serializer.Write(ref writer, current);
		return true;
	}
}

/// <summary>Creates the column of a replicated type (see <see cref="IReplicatedTypeVisitor{TResult}"/>).</summary>
internal sealed class ColumnFactory(int capacity) : IReplicatedTypeVisitor<ReplicatedColumn>
{
	public ReplicatedColumn Visit<T>(ReplicatedTypeInfo<T> info) where T : unmanaged => new ReplicatedColumn<T>(info, capacity);
}

/// <summary>Bit helpers for slot sets.</summary>
internal static class SlotBits
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Get(ulong[] bits, int slot) => (uint)(slot >> 6) < (uint)bits.Length && (bits[slot >> 6] & (1UL << slot)) != 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Set(ref ulong[] bits, int slot)
	{
		if ((slot >> 6) >= bits.Length) Array.Resize(ref bits, Math.Max(bits.Length * 2, (slot >> 6) + 1));
		bits[slot >> 6] |= 1UL << slot;
	}

	public static int PopCount(ulong[] bits)
	{
		var count = 0;
		foreach (var word in bits) count += BitOperations.PopCount(word);
		return count;
	}
}
