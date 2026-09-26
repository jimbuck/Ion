namespace Ion.Extensions.Networking;

/// <summary>
/// The replicated state of every networked entity at one tick: per slot the entity's <see cref="NetworkId"/> (0 when
/// the slot is empty) and owner, and one <see cref="ReplicatedColumn"/> per replicated type. On a client it also tracks
/// the parts of the snapshot received so far.
/// </summary>
internal sealed class SnapshotFrame
{
	public SnapshotFrame(NetworkTypeTable table, int capacity)
	{
		var factory = new ColumnFactory(capacity);
		Columns = new ReplicatedColumn[table.Components.Count];
		for (var i = 0; i < Columns.Length; i++) Columns[i] = table.Components[i].Accept(factory);
		Ids = new uint[Math.Max(64, capacity)];
		Owners = new byte[Ids.Length];
	}

	/// <summary>The tick, or 0 when the frame holds nothing.</summary>
	public uint Tick;

	/// <summary>Whether every part arrived (a captured frame is always complete).</summary>
	public bool Complete;

	/// <summary>One past the highest slot that may be in use.</summary>
	public int SlotCount;

	public uint[] Ids;

	public byte[] Owners;

	public readonly ReplicatedColumn[] Columns;

	// Client assembly: the baseline tick the parts are relative to, the parts received and their count (-1 until the last arrives).
	public uint Baseline;
	public readonly ulong[] Parts = new ulong[Protocol.MaxParts / 64];
	public int PartCount = -1;
	public int Bytes;

	public void EnsureCapacity(int slots)
	{
		if (slots > Ids.Length)
		{
			var size = Ids.Length;
			while (size < slots) size *= 2;
			Array.Resize(ref Ids, size);
			Array.Resize(ref Owners, size);
		}

		foreach (var column in Columns) column.EnsureCapacity(slots);
	}

	/// <summary>Empties the frame for <paramref name="tick"/>.</summary>
	public void Reset(uint tick)
	{
		Array.Clear(Ids, 0, Math.Min(SlotCount, Ids.Length));
		foreach (var column in Columns) column.Clear(SlotCount);
		SlotCount = 0;
		Tick = tick;
		Complete = false;
		ResetParts();
	}

	/// <summary>Makes this frame a copy of <paramref name="source"/>'s state, for <paramref name="tick"/>.</summary>
	public void CopyFrom(SnapshotFrame source, uint tick)
	{
		var clear = Math.Max(SlotCount, source.SlotCount);
		EnsureCapacity(clear);
		Array.Clear(Ids, 0, Math.Min(clear, Ids.Length));
		foreach (var column in Columns) column.Clear(clear);
		Array.Copy(source.Ids, Ids, source.SlotCount);
		Array.Copy(source.Owners, Owners, source.SlotCount);
		for (var i = 0; i < Columns.Length; i++) Columns[i].CopyFrom(source.Columns[i], source.SlotCount);
		SlotCount = source.SlotCount;
		Tick = tick;
		Complete = false;
		ResetParts();
	}

	public void ResetParts()
	{
		Array.Clear(Parts);
		PartCount = -1;
		Baseline = 0;
		Bytes = 0;
	}

	public bool IsAlive(int slot) => slot < SlotCount && Ids[slot] != 0;

	/// <summary>Empties <paramref name="slot"/> (a despawn).</summary>
	public void Despawn(int slot)
	{
		if (slot >= Ids.Length) return;
		Ids[slot] = 0;
		Owners[slot] = 0;
		foreach (var column in Columns) column.Remove(slot);
	}

	/// <summary>
	/// Fills <paramref name="bits"/> (cleared first, grown if needed) with the slots below <paramref name="slotCount"/>
	/// whose id, owner or any replicated component differs between this frame and <paramref name="other"/>.
	/// </summary>
	public void Changed(SnapshotFrame other, ref ulong[] bits, int slotCount)
	{
		var words = (slotCount + 63) >> 6;
		if (bits.Length < words) bits = new ulong[Math.Max(words, bits.Length * 2)];
		Array.Clear(bits, 0, words);
		for (var w = 0; w < words; w++)
		{
			if (BlockIdsEqual(other, w)) continue;
			var start = w << 6;
			for (var slot = start; slot < start + 64 && slot < slotCount; slot++)
			{
				var a = slot < Ids.Length ? Ids[slot] : 0;
				var b = slot < other.Ids.Length ? other.Ids[slot] : 0;
				var oa = slot < Owners.Length ? Owners[slot] : 0;
				var ob = slot < other.Owners.Length ? other.Owners[slot] : 0;
				if (a != b || oa != ob) bits[w] |= 1UL << slot;
			}
		}

		for (var c = 0; c < Columns.Length; c++) Columns[c].MarkChanged(other.Columns[c], bits, slotCount);
	}

	/// <summary>Whether the 64 slots of <paramref name="block"/> hold the same ids and owners in both frames.</summary>
	public bool BlockIdsEqual(SnapshotFrame other, int block) => BlockEqual(Ids, other.Ids, block) && BlockEqual(Owners, other.Owners, block);

	/// <summary>Whether the 64 entries of <paramref name="block"/> are equal in both arrays (false when either is shorter).</summary>
	public static bool BlockEqual<T>(T[] a, T[] b, int block) where T : unmanaged, IEquatable<T>
	{
		var start = block << 6;
		if (start + 64 > a.Length || start + 64 > b.Length) return false;
		return a.AsSpan(start, 64).SequenceEqual(b.AsSpan(start, 64));
	}

	/// <summary>Marks part <paramref name="index"/> received; true when it was new.</summary>
	public bool MarkPart(int index)
	{
		if ((uint)index >= Protocol.MaxParts) return false;
		ref var word = ref Parts[index >> 6];
		var bit = 1UL << index;
		if ((word & bit) != 0) return false;
		word |= bit;
		return true;
	}

	/// <summary>Whether every part up to the last one arrived.</summary>
	public bool AllParts()
	{
		if (PartCount < 0) return false;
		for (var i = 0; i < PartCount; i++)
		{
			if ((Parts[i >> 6] & (1UL << i)) == 0) return false;
		}

		return true;
	}
}

/// <summary>A ring of <see cref="SnapshotFrame"/>s indexed by tick modulo the history length.</summary>
internal sealed class SnapshotRing
{
	private readonly SnapshotFrame[] _frames;

	public SnapshotRing(NetworkTypeTable table, int history, int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(history, 2);
		_frames = new SnapshotFrame[history];
		for (var i = 0; i < history; i++) _frames[i] = new SnapshotFrame(table, capacity);
	}

	public int History => _frames.Length;

	/// <summary>The frame slot of <paramref name="tick"/> (whatever tick it holds).</summary>
	public SnapshotFrame Slot(uint tick) => _frames[tick % (uint)_frames.Length];

	/// <summary>The complete frame of <paramref name="tick"/>, or null.</summary>
	public SnapshotFrame? Get(uint tick)
	{
		if (tick == 0) return null;
		var frame = Slot(tick);
		return frame.Tick == tick && frame.Complete ? frame : null;
	}
}
