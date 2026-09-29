using Microsoft.Extensions.Options;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

/// <summary>
/// Loads <see cref="ISoundEffect"/> assets for the engine mixer: decodes the whole file (WAV, OGG Vorbis or MP3) and
/// resamples it to <see cref="AudioConfig.OutputRate"/>. The result is a <see cref="SoundEffect"/>.
/// </summary>
public sealed class SoundEffectLoader(IPersistentStorage storage, IOptions<AudioConfig>? config = null) : IAssetLoader<ISoundEffect>
{
	private readonly int _outputRate = config?.Value.OutputRate ?? new AudioConfig().OutputRate;

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

		using var stream = storage.Assets.Read(path);
		return SoundDecoder.Decode(path, stream, _outputRate);
	}
}
