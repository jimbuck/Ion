using NLayer;

using NVorbis;

namespace Ion.Extensions.Audio;

/// <summary>
/// The container formats <see cref="SoundDecoder"/> recognizes.
/// </summary>
public enum SoundFormat
{
	/// <summary>Not recognized.</summary>
	Unknown = 0,
	/// <summary>RIFF/WAVE.</summary>
	Wav,
	/// <summary>OGG Vorbis.</summary>
	OggVorbis,
	/// <summary>MPEG audio (MP3, and layers 1 and 2).</summary>
	Mp3,
}

/// <summary>
/// Decodes whole sound files into <see cref="SoundEffect"/>s at a given output rate, all managed code: WAV
/// (<see cref="WavDecoder"/>), OGG Vorbis (NVorbis) and MP3 (NLayer). Used at load time; streaming is not supported yet.
/// </summary>
public static class SoundDecoder
{
	/// <summary>
	/// Detects the format from the first bytes of a file, falling back to the extension of <paramref name="name"/>.
	/// </summary>
	public static SoundFormat Detect(ReadOnlySpan<byte> header, string? name = null)
	{
		if (WavDecoder.IsWav(header)) return SoundFormat.Wav;
		if (header.Length >= 4 && header[..4].SequenceEqual("OggS"u8)) return SoundFormat.OggVorbis;
		if (header.Length >= 3 && header[..3].SequenceEqual("ID3"u8)) return SoundFormat.Mp3;
		if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0) return SoundFormat.Mp3;

		return Path.GetExtension(name ?? "").ToLowerInvariant() switch
		{
			".wav" or ".wave" => SoundFormat.Wav,
			".ogg" or ".oga" => SoundFormat.OggVorbis,
			".mp3" or ".mp2" or ".mpga" => SoundFormat.Mp3,
			_ => SoundFormat.Unknown,
		};
	}

	/// <summary>
	/// Decodes the whole of <paramref name="stream"/> and resamples it to <paramref name="outputRate"/>.
	/// </summary>
	/// <param name="name">The sound's name (the asset path); its extension is used when the content is not recognized.</param>
	/// <param name="stream">The file. It is read to the end but not disposed.</param>
	/// <param name="outputRate">The mixer's output rate.</param>
	/// <exception cref="InvalidDataException">The file is not in a supported format, or is corrupt.</exception>
	public static SoundEffect Decode(string name, Stream stream, int outputRate)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(stream);

		byte[] bytes;
		if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && ms.Position == 0)
		{
			bytes = segment.Count == segment.Array!.Length ? segment.Array : segment.AsSpan().ToArray();
		}
		else
		{
			using var copy = new MemoryStream();
			stream.CopyTo(copy);
			bytes = copy.ToArray();
		}

		return Decode(name, bytes, outputRate);
	}

	/// <summary>
	/// Decodes the file held in <paramref name="data"/> and resamples it to <paramref name="outputRate"/>.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not in a supported format, or is corrupt.</exception>
	public static SoundEffect Decode(string name, byte[] data, int outputRate)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(data);
		ArgumentOutOfRangeException.ThrowIfLessThan(outputRate, 1);

		var decoded = DecodeRaw(name, data);
		var samples = Resampler.Resample(decoded.Samples, decoded.Channels, decoded.SampleRate, outputRate);
		return new SoundEffect(name, samples, decoded.Channels, outputRate, decoded.SampleRate);
	}

	/// <summary>
	/// Decodes the file held in <paramref name="data"/> without resampling.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not in a supported format, or is corrupt.</exception>
	public static DecodedAudio DecodeRaw(string name, byte[] data)
	{
		var format = Detect(data, name);
		try
		{
			return format switch
			{
				SoundFormat.Wav => WavDecoder.Decode(data, name),
				SoundFormat.OggVorbis => _decodeVorbis(data),
				SoundFormat.Mp3 => _decodeMpeg(data),
				_ => throw new InvalidDataException($"Sound '{name}' is not in a supported format (WAV, OGG Vorbis or MP3)."),
			};
		}
		catch (Exception ex) when (ex is not InvalidDataException)
		{
			throw new InvalidDataException($"Sound '{name}' could not be decoded as {format}: {ex.Message}", ex);
		}
	}

	private static DecodedAudio _decodeVorbis(byte[] data)
	{
		using var stream = new MemoryStream(data, writable: false);
		using var reader = new VorbisReader(stream, closeOnDispose: false);
		var channels = reader.Channels;
		var rate = reader.SampleRate;
		var total = reader.TotalSamples; // frames, when known

		var buffer = new float[4096 * channels];
		var all = new List<float>(total > 0 && total < int.MaxValue / channels ? (int)total * channels : buffer.Length);
		int read;
		while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
		{
			all.AddRange(buffer.AsSpan(0, read));
		}

		return _toStereoAtMost(all, channels, rate);
	}

	private static DecodedAudio _decodeMpeg(byte[] data)
	{
		using var stream = new MemoryStream(data, writable: false);
		using var reader = new MpegFile(stream);
		var channels = reader.Channels;
		var rate = reader.SampleRate;
		if (channels <= 0 || rate <= 0) throw new InvalidDataException("No MPEG audio frames found.");

		var buffer = new float[4096 * channels];
		var all = new List<float>(buffer.Length * 4);
		int read;
		while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
		{
			all.AddRange(buffer.AsSpan(0, read));
		}

		return _toStereoAtMost(all, channels, rate);
	}

	private static DecodedAudio _toStereoAtMost(List<float> interleaved, int channels, int rate)
	{
		var frames = interleaved.Count / channels;
		if (channels <= 2)
		{
			var samples = frames * channels == interleaved.Count ? interleaved.ToArray() : interleaved.GetRange(0, frames * channels).ToArray();
			return new DecodedAudio(samples, channels, rate, channels);
		}

		var stereo = new float[frames * 2];
		for (var f = 0; f < frames; f++)
		{
			stereo[f * 2] = interleaved[f * channels];
			stereo[f * 2 + 1] = interleaved[f * channels + 1];
		}

		return new DecodedAudio(stereo, 2, rate, channels);
	}
}
