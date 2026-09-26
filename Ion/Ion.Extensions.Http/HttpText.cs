using System.Buffers;
using System.Text;

namespace Ion.Extensions.Http;

/// <summary>Byte-level helpers for HTTP text: tokens, field values, percent-decoding and query strings. Nothing allocates unless it returns a string.</summary>
public static class HttpText
{
	// RFC 9110 tchar: "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
	private static readonly SearchValues<byte> TokenChars = SearchValues.Create("!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"u8);

	/// <summary>Whether <paramref name="value"/> is a non-empty RFC 9110 token.</summary>
	public static bool IsToken(ReadOnlySpan<byte> value) => value.Length > 0 && !value.ContainsAnyExcept(TokenChars);

	/// <summary>Whether <paramref name="value"/> is a valid field value (visible characters, spaces, tabs and obs-text; no controls).</summary>
	public static bool IsFieldValue(ReadOnlySpan<byte> value)
	{
		foreach (var b in value)
		{
			if (b < 0x20 && b != (byte)'\t') return false;
			if (b == 0x7F) return false;
		}

		return true;
	}

	/// <summary>Whether every byte is an ASCII digit.</summary>
	public static bool AllDigits(ReadOnlySpan<byte> value)
	{
		foreach (var b in value)
		{
			if (b is < (byte)'0' or > (byte)'9') return false;
		}

		return true;
	}

	/// <summary>Whether the comma-separated list <paramref name="list"/> contains <paramref name="token"/> (ASCII, case-insensitive, whitespace trimmed).</summary>
	public static bool ContainsToken(ReadOnlySpan<byte> list, ReadOnlySpan<byte> token)
	{
		while (list.Length > 0)
		{
			var comma = list.IndexOf((byte)',');
			var item = comma < 0 ? list : list[..comma];
			if (Ascii.EqualsIgnoreCase(item.Trim(" \t"u8), token)) return true;
			if (comma < 0) break;
			list = list[(comma + 1)..];
		}

		return false;
	}

	/// <summary>
	/// Finds <paramref name="name"/> in the query string <paramref name="query"/> (<c>a=1&amp;b=2</c>, without the <c>?</c>) and
	/// returns its still-encoded value. Keys are compared after percent-decoding (and <c>+</c> as a space) when they contain
	/// escapes; the first match wins.
	/// </summary>
	public static bool TryGetQuery(ReadOnlySpan<byte> query, ReadOnlySpan<byte> name, out ReadOnlySpan<byte> rawValue)
	{
		Span<byte> scratch = stackalloc byte[128];
		while (query.Length > 0)
		{
			var amp = query.IndexOf((byte)'&');
			var pair = amp < 0 ? query : query[..amp];
			var eq = pair.IndexOf((byte)'=');
			var key = eq < 0 ? pair : pair[..eq];
			var matches = key.IndexOfAny((byte)'%', (byte)'+') < 0
				? key.SequenceEqual(name)
				: key.Length <= scratch.Length && TryDecode(key, scratch, plusIsSpace: true, out var written) && scratch[..written].SequenceEqual(name);
			if (matches)
			{
				rawValue = eq < 0 ? [] : pair[(eq + 1)..];
				return true;
			}

			if (amp < 0) break;
			query = query[(amp + 1)..];
		}

		rawValue = default;
		return false;
	}

	/// <summary>
	/// Percent-decodes <paramref name="source"/> into <paramref name="destination"/> (which may be as long as the source).
	/// Returns false for a malformed escape or a destination that is too small.
	/// </summary>
	public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, bool plusIsSpace, out int written)
	{
		written = 0;
		for (var i = 0; i < source.Length; i++)
		{
			if (written >= destination.Length) return false;
			var b = source[i];
			if (b == (byte)'%')
			{
				if (i + 2 >= source.Length || !TryHex(source[i + 1], out var high) || !TryHex(source[i + 2], out var low)) return false;
				destination[written++] = (byte)((high << 4) | low);
				i += 2;
			}
			else if (b == (byte)'+' && plusIsSpace)
			{
				destination[written++] = (byte)' ';
			}
			else
			{
				destination[written++] = b;
			}
		}

		return true;
	}

	/// <summary>Percent-decodes <paramref name="source"/> as UTF-8 into a string (malformed escapes are kept as they are).</summary>
	public static string DecodeToString(ReadOnlySpan<byte> source, bool plusIsSpace)
	{
		if (source.IndexOfAny((byte)'%', (byte)'+') < 0) return Encoding.UTF8.GetString(source);
		var buffer = source.Length <= 512 ? stackalloc byte[source.Length] : new byte[source.Length];
		return TryDecode(source, buffer, plusIsSpace, out var written) ? Encoding.UTF8.GetString(buffer[..written]) : Encoding.UTF8.GetString(source);
	}

	private static bool TryHex(byte c, out int value)
	{
		value = c switch
		{
			>= (byte)'0' and <= (byte)'9' => c - '0',
			>= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
			>= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
			_ => -1,
		};
		return value >= 0;
	}
}
