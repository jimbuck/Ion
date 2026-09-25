using System.Buffers.Binary;

namespace Ion.Extensions.Audio;

/// <summary>
/// Decoded audio before resampling: interleaved float32 samples at the file's rate.
/// </summary>
/// <param name="Samples">Interleaved samples in [-1, 1].</param>
/// <param name="Channels">Channels in <paramref name="Samples"/> (1 or 2).</param>
/// <param name="SampleRate">Frames per second of the file.</param>
/// <param name="SourceChannels">Channels of the file, before anything beyond the first two was dropped.</param>
public readonly record struct DecodedAudio(float[] Samples, int Channels, int SampleRate, int SourceChannels);

/// <summary>
/// Reads RIFF/WAVE files: PCM 8 (unsigned), 16, 24 and 32-bit integer, 32 and 64-bit IEEE float, plain or
/// <c>WAVE_FORMAT_EXTENSIBLE</c>, any channel count (the first two are kept) and any rate.
/// </summary>
public static class WavDecoder
{
	private const ushort FormatPcm = 1;
	private const ushort FormatFloat = 3;
	private const ushort FormatExtensible = 0xFFFE;

	/// <summary>Whether <paramref name="header"/> (at least 12 bytes) starts a RIFF/WAVE file.</summary>
	public static bool IsWav(ReadOnlySpan<byte> header) =>
		header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8);

	/// <summary>
	/// Decodes a whole WAV file held in <paramref name="data"/>.
	/// </summary>
	/// <exception cref="InvalidDataException">The data is not a supported WAV file.</exception>
	public static DecodedAudio Decode(ReadOnlySpan<byte> data, string name = "sound")
	{
		if (!IsWav(data)) throw new InvalidDataException($"Sound '{name}' is not a RIFF/WAVE file.");

		ushort format = 0, channels = 0, bits = 0, blockAlign = 0;
		var sampleRate = 0;
		var hasFormat = false;
		ReadOnlySpan<byte> samples = default;
		var hasData = false;

		var position = 12;
		while (position + 8 <= data.Length)
		{
			var id = data.Slice(position, 4);
			var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 4, 4));
			var body = position + 8;
			// Streaming writers leave the size at 0 or 0xFFFFFFFF; clamp to what the file holds.
			var available = data.Length - body;
			var length = size > (uint)available ? available : (int)size;

			if (id.SequenceEqual("fmt "u8))
			{
				if (length < 16) throw new InvalidDataException($"Sound '{name}' has a truncated WAV fmt chunk.");
				var fmt = data.Slice(body, length);
				format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
				channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
				sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
				blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
				bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);

				if (format == FormatExtensible)
				{
					// cbSize(2) validBits(2) channelMask(4) subFormat GUID(16): the GUID starts with the format tag.
					if (length < 40) throw new InvalidDataException($"Sound '{name}' has a truncated WAVE_FORMAT_EXTENSIBLE fmt chunk.");
					format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
				}

				hasFormat = true;
			}
			else if (id.SequenceEqual("data"u8))
			{
				samples = data.Slice(body, length);
				hasData = true;
				if (hasFormat) break;
			}

			// Chunks are word aligned.
			position = body + length + (length & 1);
		}

		if (!hasFormat) throw new InvalidDataException($"Sound '{name}' is a WAV file without a fmt chunk.");
		if (!hasData) throw new InvalidDataException($"Sound '{name}' is a WAV file without a data chunk.");
		if (channels == 0) throw new InvalidDataException($"Sound '{name}' has 0 channels.");
		if (sampleRate <= 0) throw new InvalidDataException($"Sound '{name}' has an invalid sample rate {sampleRate}.");

		var bytesPerSample = bits / 8;
		var supported = format switch
		{
			FormatPcm => bits is 8 or 16 or 24 or 32,
			FormatFloat => bits is 32 or 64,
			_ => false,
		};
		if (!supported)
		{
			throw new InvalidDataException($"Sound '{name}' uses an unsupported WAV encoding (format tag {format}, {bits} bits). Supported: PCM 8/16/24/32-bit and IEEE float 32/64-bit.");
		}

		var frameSize = Math.Max(blockAlign, (ushort)(bytesPerSample * channels));
		var frames = samples.Length / frameSize;
		var outChannels = Math.Min((int)channels, 2);
		var output = new float[frames * outChannels];

		for (var f = 0; f < frames; f++)
		{
			var frame = samples.Slice(f * frameSize, frameSize);
			for (var c = 0; c < outChannels; c++)
			{
				output[f * outChannels + c] = _read(frame.Slice(c * bytesPerSample, bytesPerSample), format, bits);
			}
		}

		return new DecodedAudio(output, outChannels, sampleRate, channels);
	}

	private static float _read(ReadOnlySpan<byte> s, ushort format, int bits)
	{
		if (format == FormatFloat)
		{
			return bits == 32
				? BinaryPrimitives.ReadSingleLittleEndian(s)
				: (float)BinaryPrimitives.ReadDoubleLittleEndian(s);
		}

		return bits switch
		{
			8 => (s[0] - 128) / 128f,
			16 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
			24 => (((s[2] << 24) | (s[1] << 16) | (s[0] << 8)) >> 8) / 8388608f,
			_ => (float)(BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648.0),
		};
	}
}
