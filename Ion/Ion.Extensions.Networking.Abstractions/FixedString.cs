using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ion.Extensions.Networking;

/// <summary>Storage of <see cref="FixedString32"/>: 31 bytes.</summary>
[InlineArray(31)]
public struct FixedBytes31
{
	private byte _first;
}

/// <summary>Storage of <see cref="FixedString64"/>: 63 bytes.</summary>
[InlineArray(63)]
public struct FixedBytes63
{
	private byte _first;
}

/// <summary>Storage of <see cref="FixedString128"/>: 127 bytes.</summary>
[InlineArray(127)]
public struct FixedBytes127
{
	private byte _first;
}

/// <summary>
/// Shared conversions of the fixed-size strings: UTF-8, truncated at a character boundary to the capacity.
/// </summary>
internal static class FixedStrings
{
	public static byte Encode(ReadOnlySpan<char> text, Span<byte> destination)
	{
		if (text.IsEmpty) return 0;
		var max = destination.Length;
		var count = 0;
		foreach (var rune in text.EnumerateRunes())
		{
			var size = rune.Utf8SequenceLength;
			if (count + size > max) break;
			rune.EncodeToUtf8(destination[count..]);
			count += size;
		}

		return (byte)count;
	}

	public static string Decode(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
}

/// <summary>
/// An inline UTF-8 string of at most 31 bytes (32 with its length), <see langword="unmanaged"/> so it can be part of a
/// replicated component or a network message. Longer text is truncated at a character boundary.
/// </summary>
public struct FixedString32 : IEquatable<FixedString32>
{
	/// <summary>The capacity in UTF-8 bytes.</summary>
	public const int Capacity = 31;

	private byte _length;
	private FixedBytes31 _bytes;

	/// <summary>Creates the string from <paramref name="text"/> (truncated to <see cref="Capacity"/> bytes).</summary>
	public FixedString32(ReadOnlySpan<char> text) => _length = FixedStrings.Encode(text, _bytes);

	/// <summary>Creates the string from UTF-8 bytes (truncated to <see cref="Capacity"/>).</summary>
	public static FixedString32 FromUtf8(ReadOnlySpan<byte> utf8)
	{
		var value = default(FixedString32);
		var length = Math.Min(utf8.Length, Capacity);
		utf8[..length].CopyTo(value._bytes);
		value._length = (byte)length;
		return value;
	}

	/// <summary>The length in UTF-8 bytes.</summary>
	public readonly int Length => _length;

	/// <summary>The UTF-8 bytes.</summary>
	[UnscopedRef]
	public readonly ReadOnlySpan<byte> AsSpan() => ((ReadOnlySpan<byte>)_bytes)[..Math.Min((int)_length, Capacity)];

	/// <summary>Converts from a string.</summary>
	public static implicit operator FixedString32(string? text) => new(text);

	/// <inheritdoc/>
	public readonly bool Equals(FixedString32 other) => AsSpan().SequenceEqual(other.AsSpan());

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is FixedString32 other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode()
	{
		var hash = new HashCode();
		hash.AddBytes(AsSpan());
		return hash.ToHashCode();
	}

	/// <summary>Whether the strings are equal.</summary>
	public static bool operator ==(FixedString32 left, FixedString32 right) => left.Equals(right);

	/// <summary>Whether the strings differ.</summary>
	public static bool operator !=(FixedString32 left, FixedString32 right) => !left.Equals(right);

	/// <summary>Decodes the string (allocates).</summary>
	public override readonly string ToString() => FixedStrings.Decode(AsSpan());
}

/// <summary>An inline UTF-8 string of at most 63 bytes; see <see cref="FixedString32"/>.</summary>
public struct FixedString64 : IEquatable<FixedString64>
{
	/// <summary>The capacity in UTF-8 bytes.</summary>
	public const int Capacity = 63;

	private byte _length;
	private FixedBytes63 _bytes;

	/// <summary>Creates the string from <paramref name="text"/> (truncated to <see cref="Capacity"/> bytes).</summary>
	public FixedString64(ReadOnlySpan<char> text) => _length = FixedStrings.Encode(text, _bytes);

	/// <summary>Creates the string from UTF-8 bytes (truncated to <see cref="Capacity"/>).</summary>
	public static FixedString64 FromUtf8(ReadOnlySpan<byte> utf8)
	{
		var value = default(FixedString64);
		var length = Math.Min(utf8.Length, Capacity);
		utf8[..length].CopyTo(value._bytes);
		value._length = (byte)length;
		return value;
	}

	/// <summary>The length in UTF-8 bytes.</summary>
	public readonly int Length => _length;

	/// <summary>The UTF-8 bytes.</summary>
	[UnscopedRef]
	public readonly ReadOnlySpan<byte> AsSpan() => ((ReadOnlySpan<byte>)_bytes)[..Math.Min((int)_length, Capacity)];

	/// <summary>Converts from a string.</summary>
	public static implicit operator FixedString64(string? text) => new(text);

	/// <inheritdoc/>
	public readonly bool Equals(FixedString64 other) => AsSpan().SequenceEqual(other.AsSpan());

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is FixedString64 other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode()
	{
		var hash = new HashCode();
		hash.AddBytes(AsSpan());
		return hash.ToHashCode();
	}

	/// <summary>Whether the strings are equal.</summary>
	public static bool operator ==(FixedString64 left, FixedString64 right) => left.Equals(right);

	/// <summary>Whether the strings differ.</summary>
	public static bool operator !=(FixedString64 left, FixedString64 right) => !left.Equals(right);

	/// <summary>Decodes the string (allocates).</summary>
	public override readonly string ToString() => FixedStrings.Decode(AsSpan());
}

/// <summary>An inline UTF-8 string of at most 127 bytes; see <see cref="FixedString32"/>.</summary>
public struct FixedString128 : IEquatable<FixedString128>
{
	/// <summary>The capacity in UTF-8 bytes.</summary>
	public const int Capacity = 127;

	private byte _length;
	private FixedBytes127 _bytes;

	/// <summary>Creates the string from <paramref name="text"/> (truncated to <see cref="Capacity"/> bytes).</summary>
	public FixedString128(ReadOnlySpan<char> text) => _length = FixedStrings.Encode(text, _bytes);

	/// <summary>Creates the string from UTF-8 bytes (truncated to <see cref="Capacity"/>).</summary>
	public static FixedString128 FromUtf8(ReadOnlySpan<byte> utf8)
	{
		var value = default(FixedString128);
		var length = Math.Min(utf8.Length, Capacity);
		utf8[..length].CopyTo(value._bytes);
		value._length = (byte)length;
		return value;
	}

	/// <summary>The length in UTF-8 bytes.</summary>
	public readonly int Length => _length;

	/// <summary>The UTF-8 bytes.</summary>
	[UnscopedRef]
	public readonly ReadOnlySpan<byte> AsSpan() => ((ReadOnlySpan<byte>)_bytes)[..Math.Min((int)_length, Capacity)];

	/// <summary>Converts from a string.</summary>
	public static implicit operator FixedString128(string? text) => new(text);

	/// <inheritdoc/>
	public readonly bool Equals(FixedString128 other) => AsSpan().SequenceEqual(other.AsSpan());

	/// <inheritdoc/>
	public override readonly bool Equals(object? obj) => obj is FixedString128 other && Equals(other);

	/// <inheritdoc/>
	public override readonly int GetHashCode()
	{
		var hash = new HashCode();
		hash.AddBytes(AsSpan());
		return hash.ToHashCode();
	}

	/// <summary>Whether the strings are equal.</summary>
	public static bool operator ==(FixedString128 left, FixedString128 right) => left.Equals(right);

	/// <summary>Whether the strings differ.</summary>
	public static bool operator !=(FixedString128 left, FixedString128 right) => !left.Equals(right);

	/// <summary>Decodes the string (allocates).</summary>
	public override readonly string ToString() => FixedStrings.Decode(AsSpan());
}
