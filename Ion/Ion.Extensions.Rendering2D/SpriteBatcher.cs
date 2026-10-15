using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>One segment of a frame: the sprites between two render state changes.</summary>
internal struct SpriteSegment
{
	public int Start;
	public int End;
	public int FirstDraw;
	public int DrawCount;
	public SpriteBatchOptions Options;
	public ITexture? Target;
	public Color? Clear;
}

/// <summary>One draw call: <see cref="Count"/> instances from <see cref="First"/>, all with the texture in <see cref="Slot"/>.</summary>
internal struct SpriteDraw
{
	public int First;
	public int Count;
	public int Slot;
}

/// <summary>
/// The CPU half of the sprite batch: records sprites into one instance array per frame, sorts each segment and splits it
/// into draw calls (one per run of equal textures). Allocation-free once its arrays have grown to the frame's size.
/// Nothing here touches the GPU, so it is benchmarked on its own (<c>SpriteBatchBenchmarks</c>).
/// </summary>
internal sealed class SpriteBatcher
{
	private static int _nextStamp;

	private SpriteInstance[] _instances = new SpriteInstance[1024];
	private int[] _slots = new int[1024];
	private SpriteInstance[] _scratch = [];
	private int[] _scratchSlots = [];
	private uint[] _keys = [];
	private uint[] _scratchKeys = [];
	private int[] _order = [];
	private int[] _scratchOrder = [];
	private readonly int[] _radix = new int[256];
	private int[] _counts = new int[16];

	private SpriteTexture[] _textures = new SpriteTexture[16];
	private SpriteSegment[] _segments = new SpriteSegment[8];
	private SpriteDraw[] _draws = new SpriteDraw[64];

	private int _count;
	private int _textureCount;
	private int _segmentCount;
	private int _drawCount;
	private int _stamp = Interlocked.Increment(ref _nextStamp);
	private bool _segmentOpen;

	/// <summary>The sprites recorded this frame (sorted per segment once the segment is closed).</summary>
	public ReadOnlySpan<SpriteInstance> Instances => new(_instances, 0, _count);

	/// <summary>The number of sprites recorded this frame.</summary>
	public int Count => _count;

	/// <summary>The textures used this frame, by slot.</summary>
	public ReadOnlySpan<SpriteTexture> Textures => new(_textures, 0, _textureCount);

	/// <summary>The closed segments of this frame.</summary>
	public ReadOnlySpan<SpriteSegment> Segments => new(_segments, 0, _segmentCount);

	/// <summary>The draw calls of the closed segments.</summary>
	public ReadOnlySpan<SpriteDraw> Draws => new(_draws, 0, _drawCount);

	/// <summary>True between <see cref="OpenSegment"/> and <see cref="CloseSegment"/>.</summary>
	public bool IsSegmentOpen => _segmentOpen;

	/// <summary>Starts a new frame: forgets every sprite, segment and texture slot (keeps the arrays).</summary>
	public void Reset()
	{
		Array.Clear(_textures, 0, _textureCount);
		_count = _textureCount = _segmentCount = _drawCount = 0;
		_segmentOpen = false;
		_stamp = Interlocked.Increment(ref _nextStamp);
	}

	/// <summary>Opens a segment with the given state; the previous one must be closed.</summary>
	public void OpenSegment(in SpriteBatchOptions options, ITexture? target, Color? clear)
	{
		if (_segmentOpen) throw new InvalidOperationException("Close the current segment first.");
		if (_segmentCount == _segments.Length) Array.Resize(ref _segments, _segments.Length * 2);
		_segments[_segmentCount] = new SpriteSegment { Start = _count, End = _count, Options = options, Target = target, Clear = clear };
		_segmentOpen = true;
	}

	/// <summary>
	/// Closes the open segment: sorts its sprites by its sort mode and splits them into draw calls. An empty segment is
	/// dropped unless it clears its target.
	/// </summary>
	public void CloseSegment()
	{
		if (!_segmentOpen) throw new InvalidOperationException("No segment is open.");
		_segmentOpen = false;
		ref var segment = ref _segments[_segmentCount];
		segment.End = _count;
		var length = segment.End - segment.Start;
		if (length == 0 && segment.Clear is null) return;

		if (length > 1)
		{
			switch (segment.Options.SortMode)
			{
				case SpriteSortMode.Texture:
					_sortByTexture(segment.Start, length);
					break;
				case SpriteSortMode.FrontToBack:
					_sortByDepth(segment.Start, length, descending: false);
					break;
				case SpriteSortMode.BackToFront:
					_sortByDepth(segment.Start, length, descending: true);
					break;
			}
		}

		segment.FirstDraw = _drawCount;
		var slots = _slots;
		var i = segment.Start;
		while (i < segment.End)
		{
			var slot = slots[i];
			var runStart = i;
			i++;
			while (i < segment.End && slots[i] == slot) i++;
			if (_drawCount == _draws.Length) Array.Resize(ref _draws, _draws.Length * 2);
			_draws[_drawCount++] = new SpriteDraw { First = runStart, Count = i - runStart, Slot = slot };
		}

		segment.DrawCount = _drawCount - segment.FirstDraw;
		_segmentCount++;
	}

	/// <summary>
	/// Records a sprite: the <paramref name="width"/> by <paramref name="height"/> quad whose point at
	/// <paramref name="originX"/>, <paramref name="originY"/> (fractions of the size) sits at <paramref name="x"/>,
	/// <paramref name="y"/>, rotated by <paramref name="rotation"/> radians around that point, showing the UV rectangle
	/// <paramref name="uv"/> (see <see cref="SpriteInstance.PackUv"/>).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(SpriteTexture texture, float x, float y, float width, float height, float originX, float originY, float rotation, ulong uv, uint color, float depth)
	{
		var slot = texture.BatchStamp == _stamp ? texture.BatchSlot : _addTexture(texture);
		var index = _count;
		if ((uint)index >= (uint)_instances.Length) _grow();
		// _slots and _instances always have the same length, checked above.
		Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_slots), index) = slot;
		ref var instance = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_instances), index);
		_count = index + 1;

		if (rotation == 0f)
		{
			instance.Position = new Vector2(x - originX * width, y - originY * height);
			instance.AxisX = new Vector2(width, 0f);
			instance.AxisY = new Vector2(0f, height);
		}
		else
		{
			var (sin, cos) = MathF.SinCos(rotation);
			var axisX = new Vector2(cos * width, sin * width);
			var axisY = new Vector2(-sin * height, cos * height);
			instance.Position = new Vector2(x, y) - axisX * originX - axisY * originY;
			instance.AxisX = axisX;
			instance.AxisY = axisY;
		}

		instance.Uv = uv;
		instance.Color = color;
		instance.Depth = depth;
	}

	/// <summary>
	/// The call-free fast path of <see cref="Add(SpriteTexture, float, float, float, float, float, float, float, ulong, uint, float)"/>:
	/// records an unrotated sprite when a segment is open, the texture already has a slot this frame and the instance
	/// array has room. Returns false (recording nothing) otherwise.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryAdd(SpriteTexture texture, float x, float y, float width, float height, float originX, float originY, float rotation, ulong uv, uint color, float depth)
	{
		var index = _count;
		if (!_segmentOpen || texture.BatchStamp != _stamp || rotation != 0f || (uint)index >= (uint)_instances.Length) return false;
		Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_slots), index) = texture.BatchSlot;
		ref var instance = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_instances), index);
		_count = index + 1;
		instance.Position = new Vector2(x - originX * width, y - originY * height);
		instance.AxisX = new Vector2(width, 0f);
		instance.AxisY = new Vector2(0f, height);
		instance.Uv = uv;
		instance.Color = color;
		instance.Depth = depth;
		return true;
	}

	/// <summary>Records a sprite whose corner and edges are already computed (glyphs of a rotated string).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(SpriteTexture texture, Vector2 position, Vector2 axisX, Vector2 axisY, ulong uv, uint color, float depth)
	{
		var slot = texture.BatchStamp == _stamp ? texture.BatchSlot : _addTexture(texture);
		var index = _count;
		if ((uint)index >= (uint)_instances.Length) _grow();
		// _slots and _instances always have the same length, checked above.
		Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_slots), index) = slot;
		ref var instance = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_instances), index);
		_count = index + 1;
		instance.Position = position;
		instance.AxisX = axisX;
		instance.AxisY = axisY;
		instance.Uv = uv;
		instance.Color = color;
		instance.Depth = depth;
	}

	private int _addTexture(SpriteTexture texture)
	{
		if (_textureCount == _textures.Length) Array.Resize(ref _textures, _textures.Length * 2);
		var slot = _textureCount++;
		_textures[slot] = texture;
		texture.BatchStamp = _stamp;
		texture.BatchSlot = slot;
		return slot;
	}

	private void _grow()
	{
		var size = _instances.Length * 2;
		Array.Resize(ref _instances, size);
		Array.Resize(ref _slots, size);
	}

	private void _ensureScratch(int length)
	{
		if (_scratch.Length >= length) return;
		var size = Math.Max(length, _instances.Length);
		_scratch = new SpriteInstance[size];
		_scratchSlots = new int[size];
	}

	// Puts the sorted scratch back in place: swaps the arrays when the segment is the whole frame so far (no copy),
	// copies otherwise.
	private void _commitScratch(int start, int length)
	{
		if (start == 0 && _scratch.Length >= _instances.Length)
		{
			(_instances, _scratch) = (_scratch, _instances);
			(_slots, _scratchSlots) = (_scratchSlots, _slots);
			if (_scratch.Length < _instances.Length) _ensureScratch(_instances.Length);
			return;
		}

		_scratch.AsSpan(0, length).CopyTo(_instances.AsSpan(start, length));
		_scratchSlots.AsSpan(0, length).CopyTo(_slots.AsSpan(start, length));
	}

	// Counting sort by texture slot: stable, O(n + textures).
	private void _sortByTexture(int start, int length)
	{
		if (_counts.Length < _textureCount + 1) _counts = new int[Math.Max(_textureCount + 1, _counts.Length * 2)];
		var counts = _counts.AsSpan(0, _textureCount + 1);
		counts.Clear();
		var slots = _slots.AsSpan(start, length);
		foreach (var slot in slots) counts[slot + 1]++;
		for (var t = 1; t < counts.Length; t++) counts[t] += counts[t - 1];

		_ensureScratch(_instances.Length);
		var instances = _instances.AsSpan(start, length);
		var scratch = _scratch.AsSpan(0, length);
		var scratchSlots = _scratchSlots.AsSpan(0, length);
		for (var i = 0; i < length; i++)
		{
			var slot = slots[i];
			var target = counts[slot]++;
			scratch[target] = instances[i];
			scratchSlots[target] = slot;
		}

		_commitScratch(start, length);
	}

	// Stable sort by depth: an LSD radix sort of order-preserving 32-bit depth keys, 8 bits per pass, skipping passes in
	// which every key has the same digit (a handful of distinct depths sorts in one or two passes).
	private void _sortByDepth(int start, int length, bool descending)
	{
		if (_keys.Length < length)
		{
			var size = Math.Max(length, _instances.Length);
			_keys = new uint[size];
			_scratchKeys = new uint[size];
			_order = new int[size];
			_scratchOrder = new int[size];
		}

		var keys = _keys.AsSpan(0, length);
		var order = _order.AsSpan(0, length);
		var instances = _instances.AsSpan(start, length);
		uint all = 0, any = 0;
		for (var i = 0; i < length; i++)
		{
			var bits = BitConverter.SingleToUInt32Bits(instances[i].Depth);
			bits = (bits & 0x8000_0000u) != 0 ? ~bits : bits | 0x8000_0000u;
			if (descending) bits = ~bits;
			keys[i] = bits;
			order[i] = i;
			if (i == 0) all = bits;
			any |= bits ^ all;
		}

		var keysFrom = _keys;
		var keysTo = _scratchKeys;
		var orderFrom = _order;
		var orderTo = _scratchOrder;
		var radix = _radix;
		for (var shift = 0; shift < 32; shift += 8)
		{
			if (((any >> shift) & 0xFF) == 0) continue;
			Array.Clear(radix);
			for (var i = 0; i < length; i++) radix[(keysFrom[i] >> shift) & 0xFF]++;
			var sum = 0;
			for (var d = 0; d < 256; d++)
			{
				var count = radix[d];
				radix[d] = sum;
				sum += count;
			}

			for (var i = 0; i < length; i++)
			{
				var key = keysFrom[i];
				var target = radix[(key >> shift) & 0xFF]++;
				keysTo[target] = key;
				orderTo[target] = orderFrom[i];
			}

			(keysFrom, keysTo) = (keysTo, keysFrom);
			(orderFrom, orderTo) = (orderTo, orderFrom);
		}

		_ensureScratch(_instances.Length);
		var slots = _slots.AsSpan(start, length);
		var scratch = _scratch.AsSpan(0, length);
		var scratchSlots = _scratchSlots.AsSpan(0, length);
		var sorted = orderFrom.AsSpan(0, length);
		for (var i = 0; i < length; i++)
		{
			var from = sorted[i];
			scratch[i] = instances[from];
			scratchSlots[i] = slots[from];
		}

		_commitScratch(start, length);
	}
}
