using System.Buffers.Binary;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

/// <summary>
/// A sound effect that holds only the facts read from its file header, for the headless audio backend.
/// </summary>
public sealed class NullSoundEffect : ISoundEffect
{
	private static long _nextId;

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; }

	/// <summary>
	/// Length in seconds, from the WAV header. 0 for files that are not RIFF/WAVE.
	/// </summary>
	public float Duration { get; }

	/// <summary>
	/// Channel count from the WAV header. 0 for files that are not RIFF/WAVE.
	/// </summary>
	public int Channels { get; }

	/// <summary>
	/// Samples per second from the WAV header. 0 for files that are not RIFF/WAVE.
	/// </summary>
	public int SampleRate { get; }

	/// <summary>
	/// Bits per sample from the WAV header. 0 for files that are not RIFF/WAVE.
	/// </summary>
	public int BitsPerSample { get; }

	/// <summary>
	/// True once the asset has been disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	public NullSoundEffect(string name, float duration, int channels, int sampleRate, int bitsPerSample)
	{
		Name = name;
		Duration = duration;
		Channels = channels;
		SampleRate = sampleRate;
		BitsPerSample = bitsPerSample;
	}

	public void Dispose() => IsDisposed = true;
}

/// <summary>
/// Loads <see cref="ISoundEffect"/> assets for the headless audio backend: reads the WAV header (duration, channels,
/// sample rate) without decoding samples or touching an audio device. Files in other formats (for example MP3) load
/// as a sound with no header data, so a game that uses them still runs headless.
/// </summary>
public sealed class NullSoundEffectLoader(IPersistentStorage storage) : IAssetLoader<ISoundEffect>
{
	public Type AssetType { get; } = typeof(ISoundEffect);

	public ISoundEffect Load(string path)
	{
		var filepath = storage.Assets.GetPath(path);
		if (!File.Exists(filepath))
		{
			throw new FileNotFoundException($"Sound effect '{path}' was not found at '{filepath}'. File names are case-sensitive on Linux and macOS; check the casing of the name.", filepath);
		}

		using var stream = storage.Assets.Read(path);
		return Read(path, stream);
	}

	/// <summary>
	/// Reads the WAV header of <paramref name="stream"/> into a <see cref="NullSoundEffect"/> named <paramref name="name"/>.
	/// </summary>
	/// <exception cref="InvalidDataException">The stream is a RIFF/WAVE file without a valid <c>fmt </c> chunk.</exception>
	public static NullSoundEffect Read(string name, Stream stream)
	{
		Span<byte> riff = stackalloc byte[12];
		if (stream.ReadAtLeast(riff, riff.Length, throwOnEndOfStream: false) < riff.Length
			|| !riff[..4].SequenceEqual("RIFF"u8)
			|| !riff[8..12].SequenceEqual("WAVE"u8))
		{
			return new NullSoundEffect(name, 0f, 0, 0, 0);
		}

		int channels = 0, sampleRate = 0, byteRate = 0, bitsPerSample = 0;
		long dataSize = -1;
		var hasFormat = false;

		Span<byte> header = stackalloc byte[8];
		Span<byte> format = stackalloc byte[16];
		while (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length)
		{
			var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
			var id = header[..4];

			if (id.SequenceEqual("fmt "u8))
			{
				if (chunkSize < format.Length || stream.ReadAtLeast(format, format.Length, throwOnEndOfStream: false) < format.Length)
				{
					throw new InvalidDataException($"Sound effect '{name}' has a truncated WAV fmt chunk.");
				}

				channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
				sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
				byteRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
				bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
				hasFormat = true;

				_skip(stream, chunkSize - (uint)format.Length);
			}
			else if (id.SequenceEqual("data"u8))
			{
				dataSize = chunkSize;
				break;
			}
			else
			{
				_skip(stream, chunkSize);
			}

			// Chunks are word aligned.
			if ((chunkSize & 1) == 1) _skip(stream, 1);
		}

		if (!hasFormat) throw new InvalidDataException($"Sound effect '{name}' is a WAV file without a fmt chunk.");

		var duration = dataSize > 0 && byteRate > 0 ? (float)((double)dataSize / byteRate) : 0f;

		return new NullSoundEffect(name, duration, channels, sampleRate, bitsPerSample);
	}

	private static void _skip(Stream stream, long count)
	{
		if (count <= 0) return;

		if (stream.CanSeek)
		{
			stream.Seek(count, SeekOrigin.Current);
			return;
		}

		Span<byte> buffer = stackalloc byte[256];
		while (count > 0)
		{
			var read = stream.Read(buffer[..(int)Math.Min(buffer.Length, count)]);
			if (read == 0) return;
			count -= read;
		}
	}
}
