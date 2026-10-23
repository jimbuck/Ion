using Microsoft.Extensions.Options;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

/// <summary>
/// Loads <see cref="ISoundEffect"/> assets for the engine mixer: a file (WAV, OGG Vorbis or MP3) longer than
/// <see cref="AudioConfig.StreamLongerThan"/> as a <see cref="StreamedSound"/>, decoded while it plays; anything else
/// decoded whole and resampled to <see cref="AudioConfig.OutputRate"/>, as a <see cref="SoundEffect"/>.
/// </summary>
public sealed class SoundEffectLoader(IPersistentStorage storage, IOptions<AudioConfig>? config = null) : IAssetLoader<ISoundEffect>
{
	private readonly int _outputRate = config?.Value.OutputRate ?? new AudioConfig().OutputRate;
	private readonly float _streamLongerThan = config?.Value.StreamLongerThan ?? new AudioConfig().StreamLongerThan;

	/// <inheritdoc/>
	public Type AssetType { get; } = typeof(ISoundEffect);

	/// <inheritdoc/>
	public ISoundEffect Load(string path)
	{
		var filepath = storage.Assets.GetPath(path);
		if (!File.Exists(filepath))
		{
			throw new FileNotFoundException($"Sound effect '{path}' was not found at '{filepath}'. File names are case-sensitive on Linux and macOS; check the casing of the name.", filepath);
		}

		if (SoundStreaming.TryOpen(storage, path, _streamLongerThan) is { } streamed) return streamed;
		using var stream = storage.Assets.Read(path);
		return SoundDecoder.Decode(path, stream, _outputRate);
	}
}

/// <summary>Whether an asset is streamed (see <see cref="AudioConfig.StreamLongerThan"/>).</summary>
internal static class SoundStreaming
{
	/// <summary>
	/// The asset as a <see cref="StreamedSound"/> when it is longer than <paramref name="longerThan"/> seconds (its length
	/// read from its headers), else null. A source that cannot seek is read into memory once, still compressed.
	/// </summary>
	public static StreamedSound? TryOpen(IPersistentStorage storage, string path, float longerThan)
	{
		if (!(longerThan > 0)) return null;
		Func<Stream> open = () => storage.Assets.Read(path);
		using (var probe = open())
		{
			if (!probe.CanSeek)
			{
				using var copy = new MemoryStream();
				probe.CopyTo(copy);
				var bytes = copy.ToArray();
				open = () => new MemoryStream(bytes, writable: false);
			}
		}

		try
		{
			var sound = new StreamedSound(path, open);
			return sound.Duration > longerThan ? sound : null;
		}
		catch (InvalidDataException)
		{
			// Not a format the decoders stream: the whole-file path reports what is wrong.
			return null;
		}
	}
}
