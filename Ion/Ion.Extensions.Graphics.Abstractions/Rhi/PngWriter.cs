using System.Buffers.Binary;
using System.IO.Compression;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Writes a <see cref="Screenshot"/> as an 8-bit RGBA PNG with the BCL's zlib, so screenshots need no image library (and
/// stay NativeAOT-clean on every backend).
/// </summary>
public static class PngWriter
{
	private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
	private static readonly uint[] CrcTable = _crcTable();

	/// <summary>Writes <paramref name="screenshot"/> to <paramref name="stream"/> as PNG.</summary>
	public static void Write(Screenshot screenshot, Stream stream)
	{
		ArgumentNullException.ThrowIfNull(screenshot);
		ArgumentNullException.ThrowIfNull(stream);
		stream.Write(Signature);

		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, screenshot.Width);
		BinaryPrimitives.WriteInt32BigEndian(header[4..], screenshot.Height);
		header[8] = 8; // bit depth
		header[9] = 6; // color type: RGBA
		header[10] = 0; // compression
		header[11] = 0; // filter method
		header[12] = 0; // no interlace
		_chunk(stream, "IHDR"u8, header);

		// Every scanline starts with filter type 0 (none).
		using var compressed = new MemoryStream();
		using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			var stride = screenshot.Width * 4;
			for (var y = 0; y < screenshot.Height; y++)
			{
				zlib.WriteByte(0);
				zlib.Write(screenshot.Rgba, y * stride, stride);
			}
		}

		_chunk(stream, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
		_chunk(stream, "IEND"u8, []);
	}

	/// <summary>Encodes <paramref name="screenshot"/> as PNG bytes.</summary>
	public static byte[] Encode(Screenshot screenshot)
	{
		using var stream = new MemoryStream();
		Write(screenshot, stream);
		return stream.ToArray();
	}

	private static void _chunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
		stream.Write(word);
		stream.Write(type);
		stream.Write(data);
		var crc = _crc(_crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
		BinaryPrimitives.WriteUInt32BigEndian(word, crc);
		stream.Write(word);
	}

	private static uint _crc(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	private static uint[] _crcTable()
	{
		var table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			var c = n;
			for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}

		return table;
	}
}
