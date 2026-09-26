using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ion.Extensions.Networking;

/// <summary>
/// Writes little-endian values into a span. Never throws and never allocates: a write that does not fit sets
/// <see cref="Overflowed"/> and writes nothing (and every later write is ignored), so the caller checks once at the end.
/// Used by the generated serializers.
/// </summary>
public ref struct NetWriter
{
	private readonly Span<byte> _buffer;
	private int _position;
	private bool _overflowed;

	/// <summary>Writes into <paramref name="buffer"/> from its start.</summary>
	public NetWriter(Span<byte> buffer)
	{
		_buffer = buffer;
		_position = 0;
		_overflowed = false;
	}

	/// <summary>The number of bytes written (settable to roll back to an earlier position).</summary>
	public int Position
	{
		readonly get => _position;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _buffer.Length);
			_position = value;
			_overflowed = false;
		}
	}

	/// <summary>Whether a write did not fit.</summary>
	public readonly bool Overflowed => _overflowed;

	/// <summary>The bytes left.</summary>
	public readonly int Remaining => _buffer.Length - _position;

	/// <summary>The capacity.</summary>
	public readonly int Capacity => _buffer.Length;

	/// <summary>The bytes written so far.</summary>
	public readonly ReadOnlySpan<byte> Written => _buffer[.._position];

	/// <summary>The span to write <paramref name="size"/> bytes into, or empty (and <see cref="Overflowed"/> set) if it does not fit.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Span<byte> Take(int size)
	{
		if (_overflowed || size > _buffer.Length - _position)
		{
			_overflowed = true;
			return default;
		}

		var span = _buffer.Slice(_position, size);
		_position += size;
		return span;
	}

	/// <summary>Writes a byte.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteByte(byte value)
	{
		if (_overflowed || _position >= _buffer.Length)
		{
			_overflowed = true;
			return;
		}

		_buffer[_position++] = value;
	}

	/// <summary>Writes a bool as one byte.</summary>
	public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

	/// <summary>Writes a signed byte.</summary>
	public void WriteSByte(sbyte value) => WriteByte((byte)value);

	/// <summary>Writes a 16-bit integer.</summary>
	public void WriteInt16(short value)
	{
		var span = Take(2);
		if (!span.IsEmpty) BinaryPrimitives.WriteInt16LittleEndian(span, value);
	}

	/// <summary>Writes an unsigned 16-bit integer.</summary>
	public void WriteUInt16(ushort value)
	{
		var span = Take(2);
		if (!span.IsEmpty) BinaryPrimitives.WriteUInt16LittleEndian(span, value);
	}

	/// <summary>Writes a UTF-16 character.</summary>
	public void WriteChar(char value) => WriteUInt16(value);

	/// <summary>Writes a 32-bit integer.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteInt32(int value)
	{
		var span = Take(4);
		if (!span.IsEmpty) BinaryPrimitives.WriteInt32LittleEndian(span, value);
	}

	/// <summary>Writes an unsigned 32-bit integer.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteUInt32(uint value)
	{
		var span = Take(4);
		if (!span.IsEmpty) BinaryPrimitives.WriteUInt32LittleEndian(span, value);
	}

	/// <summary>Writes a 64-bit integer.</summary>
	public void WriteInt64(long value)
	{
		var span = Take(8);
		if (!span.IsEmpty) BinaryPrimitives.WriteInt64LittleEndian(span, value);
	}

	/// <summary>Writes an unsigned 64-bit integer.</summary>
	public void WriteUInt64(ulong value)
	{
		var span = Take(8);
		if (!span.IsEmpty) BinaryPrimitives.WriteUInt64LittleEndian(span, value);
	}

	/// <summary>Writes a float (its exact bits).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteSingle(float value)
	{
		var span = Take(4);
		if (!span.IsEmpty) BinaryPrimitives.WriteSingleLittleEndian(span, value);
	}

	/// <summary>Writes a double (its exact bits).</summary>
	public void WriteDouble(double value)
	{
		var span = Take(8);
		if (!span.IsEmpty) BinaryPrimitives.WriteDoubleLittleEndian(span, value);
	}

	/// <summary>Writes an unsigned integer in 1 to 5 bytes (7 bits per byte).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteVarUInt32(uint value)
	{
		while (value >= 0x80)
		{
			WriteByte((byte)(value | 0x80));
			value >>= 7;
		}

		WriteByte((byte)value);
	}

	/// <summary>Writes an unsigned integer in 1 to 10 bytes (7 bits per byte).</summary>
	public void WriteVarUInt64(ulong value)
	{
		while (value >= 0x80)
		{
			WriteByte((byte)(value | 0x80));
			value >>= 7;
		}

		WriteByte((byte)value);
	}

	/// <summary>Writes a signed integer zig-zag encoded in 1 to 5 bytes.</summary>
	public void WriteVarInt32(int value) => WriteVarUInt32((uint)((value << 1) ^ (value >> 31)));

	/// <summary>Writes a vector.</summary>
	public void WriteVector2(Vector2 value)
	{
		var span = Take(8);
		if (span.IsEmpty) return;
		BinaryPrimitives.WriteSingleLittleEndian(span, value.X);
		BinaryPrimitives.WriteSingleLittleEndian(span[4..], value.Y);
	}

	/// <summary>Writes a vector.</summary>
	public void WriteVector3(Vector3 value)
	{
		var span = Take(12);
		if (span.IsEmpty) return;
		BinaryPrimitives.WriteSingleLittleEndian(span, value.X);
		BinaryPrimitives.WriteSingleLittleEndian(span[4..], value.Y);
		BinaryPrimitives.WriteSingleLittleEndian(span[8..], value.Z);
	}

	/// <summary>Writes a vector.</summary>
	public void WriteVector4(Vector4 value)
	{
		var span = Take(16);
		if (span.IsEmpty) return;
		BinaryPrimitives.WriteSingleLittleEndian(span, value.X);
		BinaryPrimitives.WriteSingleLittleEndian(span[4..], value.Y);
		BinaryPrimitives.WriteSingleLittleEndian(span[8..], value.Z);
		BinaryPrimitives.WriteSingleLittleEndian(span[12..], value.W);
	}

	/// <summary>Writes a quaternion.</summary>
	public void WriteQuaternion(Quaternion value) => WriteVector4(new Vector4(value.X, value.Y, value.Z, value.W));

	/// <summary>Writes a network id (5 bytes).</summary>
	public void WriteNetworkId(NetworkId value)
	{
		WriteUInt32(value.Id);
		WriteByte(value.OwnerPeer);
	}

	/// <summary>Writes raw bytes.</summary>
	public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
	{
		var span = Take(bytes.Length);
		if (!span.IsEmpty) bytes.CopyTo(span);
	}

	/// <summary>Writes a fixed string (its length and bytes).</summary>
	public void WriteFixedString32(in FixedString32 value)
	{
		var bytes = value.AsSpan();
		WriteByte((byte)bytes.Length);
		WriteBytes(bytes);
	}

	/// <summary>Writes a fixed string (its length and bytes).</summary>
	public void WriteFixedString64(in FixedString64 value)
	{
		var bytes = value.AsSpan();
		WriteByte((byte)bytes.Length);
		WriteBytes(bytes);
	}

	/// <summary>Writes a fixed string (its length and bytes).</summary>
	public void WriteFixedString128(in FixedString128 value)
	{
		var bytes = value.AsSpan();
		WriteByte((byte)bytes.Length);
		WriteBytes(bytes);
	}
}
