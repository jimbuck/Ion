using System.Buffers.Binary;

using NLayer;

using NVorbis;

namespace Ion.Extensions.Audio;

/// <summary>
/// Decodes a sound file a piece at a time: interleaved float32 frames of at most two channels (files with more keep
/// their first two), at the file's rate.
/// </summary>
internal interface IStreamDecoder : IDisposable
{
	/// <summary>1 or 2.</summary>
	int Channels { get; }

	/// <summary>Frames per second of the file.</summary>
	int SampleRate { get; }

	/// <summary>The channels of the file before anything beyond the first two was dropped.</summary>
	int SourceChannels { get; }

	/// <summary>The number of frames, or -1 when unknown.</summary>
	long Frames { get; }

	/// <summary>Decodes up to <c>destination.Length / Channels</c> frames; returns how many (0 at the end).</summary>
	int Read(Span<float> destination);

	/// <summary>Moves to <paramref name="frame"/> (clamped to the file).</summary>
	void Seek(long frame);
}

/// <summary>Opens the stream decoders of <see cref="SoundFormat"/>s.</summary>
internal static class StreamDecoders
{
	/// <exception cref="InvalidDataException">The stream is not in a supported format, or is corrupt.</exception>
	public static IStreamDecoder Open(Stream stream, SoundFormat format, string name)
	{
		try
		{
			return format switch
			{
				SoundFormat.Wav => new WavStreamDecoder(stream, name),
				SoundFormat.OggVorbis => new VorbisStreamDecoder(stream),
				SoundFormat.Mp3 => new MpegStreamDecoder(stream),
				_ => throw new InvalidDataException($"Sound '{name}' is not in a supported format (WAV, OGG Vorbis or MP3)."),
			};
		}
		catch (Exception ex) when (ex is not InvalidDataException)
		{
			stream.Dispose();
			throw new InvalidDataException($"Sound '{name}' could not be opened as {format}: {ex.Message}", ex);
		}
	}

	/// <summary>Copies the first two channels of <paramref name="frames"/> interleaved frames of <paramref name="from"/> channels.</summary>
	public static void KeepTwo(ReadOnlySpan<float> source, int from, Span<float> destination, int frames)
	{
		for (var f = 0; f < frames; f++)
		{
			destination[f * 2] = source[f * from];
			destination[f * 2 + 1] = source[f * from + 1];
		}
	}
}

/// <summary>Reads the PCM data chunk of a RIFF/WAVE stream in pieces (the formats of <see cref="WavDecoder"/>).</summary>
internal sealed class WavStreamDecoder : IStreamDecoder
{
	private readonly Stream _stream;
	private readonly long _dataStart;
	private readonly long _dataLength;
	private readonly ushort _format;
	private readonly int _bits;
	private readonly int _frameSize;
	private byte[] _scratch = new byte[4096];
	private long _frame;

	public WavStreamDecoder(Stream stream, string name)
	{
		_stream = stream;
		Span<byte> riff = stackalloc byte[12];
		if (stream.ReadAtLeast(riff, 12, throwOnEndOfStream: false) < 12 || !WavDecoder.IsWav(riff)) throw new InvalidDataException($"Sound '{name}' is not a RIFF/WAVE file.");

		ushort format = 0, channels = 0, blockAlign = 0, bits = 0;
		var rate = 0;
		var hasFormat = false;
		Span<byte> header = stackalloc byte[8];
		Span<byte> fmt = stackalloc byte[40];
		long position = 12;
		while (true)
		{
			if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8) throw new InvalidDataException($"Sound '{name}' is a WAV file without a data chunk.");
			position += 8;
			var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
			if (header[..4].SequenceEqual("fmt "u8))
			{
				if (size < 16) throw new InvalidDataException($"Sound '{name}' has a truncated WAV fmt chunk.");
				var read = (int)Math.Min(size, (uint)fmt.Length);
				if (stream.ReadAtLeast(fmt[..read], read, throwOnEndOfStream: false) < read) throw new InvalidDataException($"Sound '{name}' has a truncated WAV fmt chunk.");
				format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
				channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
				rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
				blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
				bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
				if (format == 0xFFFE && read >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
				hasFormat = true;
				Skip(size - (uint)read + (size & 1));
				position += size + (size & 1);
			}
			else if (header[..4].SequenceEqual("data"u8))
			{
				if (!hasFormat) throw new InvalidDataException($"Sound '{name}' has its WAV data before its fmt chunk.");
				_dataStart = position;
				// Streaming writers leave the size at 0 or 0xFFFFFFFF: to the end of the file.
				_dataLength = size == 0 || size == uint.MaxValue ? (stream.CanSeek ? stream.Length - position : long.MaxValue) : size;
				break;
			}
			else
			{
				Skip(size + (size & 1));
				position += size + (size & 1);
			}
		}

		var supported = format switch
		{
			1 => bits is 8 or 16 or 24 or 32,
			3 => bits is 32 or 64,
			_ => false,
		};
		if (!supported || channels == 0 || rate <= 0) throw new InvalidDataException($"Sound '{name}' uses an unsupported WAV encoding (format tag {format}, {bits} bits, {channels} channels, {rate} Hz).");

		_format = format;
		_bits = bits;
		_frameSize = Math.Max(blockAlign, bits / 8 * channels);
		SourceChannels = channels;
		Channels = Math.Min((int)channels, 2);
		SampleRate = rate;
		Frames = _dataLength == long.MaxValue ? -1 : _dataLength / _frameSize;
	}

	public int Channels { get; }

	public int SampleRate { get; }

	public int SourceChannels { get; }

	public long Frames { get; }

	public int Read(Span<float> destination)
	{
		var frames = destination.Length / Channels;
		if (Frames >= 0) frames = (int)Math.Min(frames, Frames - _frame);
		if (frames <= 0) return 0;
		var bytes = frames * _frameSize;
		if (_scratch.Length < bytes) _scratch = new byte[bytes];
		var read = _stream.ReadAtLeast(_scratch.AsSpan(0, bytes), bytes, throwOnEndOfStream: false);
		frames = read / _frameSize;
		var size = _bits / 8;
		for (var f = 0; f < frames; f++)
		{
			var frame = _scratch.AsSpan(f * _frameSize, _frameSize);
			for (var c = 0; c < Channels; c++) destination[f * Channels + c] = WavDecoder._read(frame.Slice(c * size, size), _format, _bits);
		}

		_frame += frames;
		return frames;
	}

	public void Seek(long frame)
	{
		frame = Math.Clamp(frame, 0, Frames >= 0 ? Frames : long.MaxValue);
		_stream.Position = _dataStart + frame * _frameSize;
		_frame = frame;
	}

	private void Skip(long count)
	{
		if (count <= 0) return;
		if (_stream.CanSeek)
		{
			_stream.Seek(count, SeekOrigin.Current);
			return;
		}

		Span<byte> buffer = stackalloc byte[256];
		while (count > 0)
		{
			var read = _stream.Read(buffer[..(int)Math.Min(buffer.Length, count)]);
			if (read == 0) return;
			count -= read;
		}
	}

	public void Dispose() => _stream.Dispose();
}

/// <summary>OGG Vorbis through NVorbis.</summary>
internal sealed class VorbisStreamDecoder : IStreamDecoder
{
	private readonly VorbisReader _reader;
	private float[] _scratch = [];

	public VorbisStreamDecoder(Stream stream)
	{
		_reader = new VorbisReader(stream, closeOnDispose: true);
		SourceChannels = _reader.Channels;
		Channels = Math.Min(SourceChannels, 2);
		SampleRate = _reader.SampleRate;
		Frames = _reader.TotalSamples > 0 ? _reader.TotalSamples : -1;
		if (SourceChannels <= 0 || SampleRate <= 0) throw new InvalidDataException("No Vorbis stream found.");
	}

	public int Channels { get; }

	public int SampleRate { get; }

	public int SourceChannels { get; }

	public long Frames { get; }

	public int Read(Span<float> destination)
	{
		var frames = destination.Length / Channels;
		if (SourceChannels == Channels)
		{
			if (_scratch.Length < destination.Length) _scratch = new float[destination.Length];
			var read = _reader.ReadSamples(_scratch, 0, frames * Channels) / Channels;
			_scratch.AsSpan(0, read * Channels).CopyTo(destination);
			return read;
		}

		if (_scratch.Length < frames * SourceChannels) _scratch = new float[frames * SourceChannels];
		var count = _reader.ReadSamples(_scratch, 0, frames * SourceChannels) / SourceChannels;
		StreamDecoders.KeepTwo(_scratch, SourceChannels, destination, count);
		return count;
	}

	public void Seek(long frame) => _reader.SeekTo(Math.Max(0, frame), SeekOrigin.Begin);

	public void Dispose() => _reader.Dispose();
}

/// <summary>MPEG audio (MP3) through NLayer.</summary>
internal sealed class MpegStreamDecoder : IStreamDecoder
{
	private readonly MpegFile _file;
	private readonly Stream _stream;
	private float[] _scratch = [];

	public MpegStreamDecoder(Stream stream)
	{
		_stream = stream;
		_file = new MpegFile(stream);
		SourceChannels = _file.Channels;
		SampleRate = _file.SampleRate;
		if (SourceChannels <= 0 || SampleRate <= 0) throw new InvalidDataException("No MPEG audio frames found.");
		Channels = Math.Min(SourceChannels, 2);
		Frames = _file.CanSeek ? (long)Math.Round(_file.Duration.TotalSeconds * SampleRate) : -1;
	}

	public int Channels { get; }

	public int SampleRate { get; }

	public int SourceChannels { get; }

	public long Frames { get; }

	public int Read(Span<float> destination)
	{
		var frames = destination.Length / Channels;
		if (_scratch.Length < frames * SourceChannels) _scratch = new float[frames * SourceChannels];
		var count = _file.ReadSamples(_scratch, 0, frames * SourceChannels) / SourceChannels;
		if (SourceChannels == Channels) _scratch.AsSpan(0, count * Channels).CopyTo(destination);
		else StreamDecoders.KeepTwo(_scratch, SourceChannels, destination, count);
		return count;
	}

	public void Seek(long frame) => _file.Time = TimeSpan.FromSeconds(Math.Max(0, frame) / (double)SampleRate);

	public void Dispose()
	{
		// NLayer leaves the stream open.
		_file.Dispose();
		_stream.Dispose();
	}
}
