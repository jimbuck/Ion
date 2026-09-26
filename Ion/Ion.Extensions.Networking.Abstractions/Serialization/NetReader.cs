using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ion.Extensions.Networking;

/// <summary>
/// Reads little-endian values from a span. Never throws and never allocates: a read past the end sets
/// <see cref="Failed"/> and returns zero (and every later read does too), so a malformed packet is detected with one check
/// and cannot throw out of the receive path. Used by the generated serializers.
/// </summary>
public ref struct NetReader
{
	private readonly ReadOnlySpan<byte> _buffer;
	private int _position;
	private bool _failed;

	/// <summary>Reads <paramref name="buffer"/> from its start.</summary>
	public NetReader(ReadOnlySpan<byte> buffer)
	{
		_buffer = buffer;
		_position = 0;
		_failed = false;
	}

	/// <summary>The number of bytes read.</summary>
	public readonly int Position => _position;

	/// <summary>The bytes left.</summary>
	public readonly int Remaining => _buffer.Length - _position;

	/// <summary>Whether everything was read.</summary>
	public readonly bool End => _position >= _buffer.Length;

	/// <summary>Whether a read went past the end or a value was invalid.</summary>
	public readonly bool Failed => _failed;

	/// <summary>Marks the data as invalid (a decoder found a value out of range).</summary>
	public void Fail() => _failed = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ReadOnlySpan<byte> Take(int size)
	{
		if (_failed || size > _buffer.Length - _position)
		{
			_failed = true;
			return default;
		}

		var span = _buffer.Slice(_position, size);
		_position += size;
		return span;
	}

	/// <summary>Skips <paramref name="count"/> bytes.</summary>
	public void Skip(int count) => Take(count);

	/// <summary>Reads a byte.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte ReadByte()
	{
		if (_failed || _position >= _buffer.Length)
		{
			_failed = true;
			return 0;
		}

		return _buffer[_position++];
	}

	/// <summary>Reads a bool (any non-zero byte is true).</summary>
	public bool ReadBool() => ReadByte() != 0;

	/// <summary>Reads a signed byte.</summary>
	public sbyte ReadSByte() => (sbyte)ReadByte();

	/// <summary>Reads a 16-bit integer.</summary>
	public short ReadInt16()
	{
		var span = Take(2);
		return span.IsEmpty ? (short)0 : BinaryPrimitives.ReadInt16LittleEndian(span);
	}

	/// <summary>Reads an unsigned 16-bit integer.</summary>
	public ushort ReadUInt16()
	{
		var span = Take(2);
		return span.IsEmpty ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(span);
	}

	/// <summary>Reads a UTF-16 character.</summary>
	public char ReadChar() => (char)ReadUInt16();

	/// <summary>Reads a 32-bit integer.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int ReadInt32()
	{
		var span = Take(4);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadInt32LittleEndian(span);
	}

	/// <summary>Reads an unsigned 32-bit integer.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ReadUInt32()
	{
		var span = Take(4);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(span);
	}

	/// <summary>Reads a 64-bit integer.</summary>
	public long ReadInt64()
	{
		var span = Take(8);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadInt64LittleEndian(span);
	}

	/// <summary>Reads an unsigned 64-bit integer.</summary>
	public ulong ReadUInt64()
	{
		var span = Take(8);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(span);
	}

	/// <summary>Reads a float.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float ReadSingle()
	{
		var span = Take(4);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadSingleLittleEndian(span);
	}

	/// <summary>Reads a double.</summary>
	public double ReadDouble()
	{
		var span = Take(8);
		return span.IsEmpty ? 0 : BinaryPrimitives.ReadDoubleLittleEndian(span);
	}

	/// <summary>Reads an unsigned integer written by <see cref="NetWriter.WriteVarUInt32"/>.</summary>
	public uint ReadVarUInt32()
	{
		uint result = 0;
		for (var shift = 0; shift < 35; shift += 7)
		{
			var b = ReadByte();
			if (_failed) return 0;
			result |= (uint)(b & 0x7F) << shift;
			if ((b & 0x80) == 0) return result;
		}

		_failed = true;
		return 0;
	}

	/// <summary>Reads an unsigned integer written by <see cref="NetWriter.WriteVarUInt64"/>.</summary>
	public ulong ReadVarUInt64()
	{
		ulong result = 0;
		for (var shift = 0; shift < 70; shift += 7)
		{
			var b = ReadByte();
			if (_failed) return 0;
			result |= (ulong)(b & 0x7F) << shift;
			if ((b & 0x80) == 0) return result;
		}

		_failed = true;
		return 0;
	}

	/// <summary>Reads a signed integer written by <see cref="NetWriter.WriteVarInt32"/>.</summary>
	public int ReadVarInt32()
	{
		var raw = ReadVarUInt32();
		return (int)(raw >> 1) ^ -(int)(raw & 1);
	}

	/// <summary>Reads a vector.</summary>
	public Vector2 ReadVector2()
	{
		var span = Take(8);
		return span.IsEmpty ? default : new Vector2(BinaryPrimitives.ReadSingleLittleEndian(span), BinaryPrimitives.ReadSingleLittleEndian(span[4..]));
	}

	/// <summary>Reads a vector.</summary>
	public Vector3 ReadVector3()
	{
		var span = Take(12);
		return span.IsEmpty ? default : new Vector3(BinaryPrimitives.ReadSingleLittleEndian(span), BinaryPrimitives.ReadSingleLittleEndian(span[4..]), BinaryPrimitives.ReadSingleLittleEndian(span[8..]));
	}

	/// <summary>Reads a vector.</summary>
	public Vector4 ReadVector4()
	{
		var span = Take(16);
		return span.IsEmpty ? default : new Vector4(BinaryPrimitives.ReadSingleLittleEndian(span), BinaryPrimitives.ReadSingleLittleEndian(span[4..]), BinaryPrimitives.ReadSingleLittleEndian(span[8..]), BinaryPrimitives.ReadSingleLittleEndian(span[12..]));
	}

	/// <summary>Reads a quaternion.</summary>
	public Quaternion ReadQuaternion()
	{
		var v = ReadVector4();
		return new Quaternion(v.X, v.Y, v.Z, v.W);
	}

	/// <summary>Reads a network id.</summary>
	public NetworkId ReadNetworkId()
	{
		var id = ReadUInt32();
		return new NetworkId(id, ReadByte());
	}

	/// <summary>Reads <paramref name="count"/> raw bytes (a slice of the packet, valid while it is).</summary>
	public ReadOnlySpan<byte> ReadBytes(int count) => Take(count);

	/// <summary>Reads a fixed string.</summary>
	public FixedString32 ReadFixedString32()
	{
		var length = ReadByte();
		if (length > FixedString32.Capacity) Fail();
		return _failed ? default : FixedString32.FromUtf8(Take(length));
	}

	/// <summary>Reads a fixed string.</summary>
	public FixedString64 ReadFixedString64()
	{
		var length = ReadByte();
		if (length > FixedString64.Capacity) Fail();
		return _failed ? default : FixedString64.FromUtf8(Take(length));
	}

	/// <summary>Reads a fixed string.</summary>
	public FixedString128 ReadFixedString128()
	{
		var length = ReadByte();
		if (length > FixedString128.Capacity) Fail();
		return _failed ? default : FixedString128.FromUtf8(Take(length));
	}
}
