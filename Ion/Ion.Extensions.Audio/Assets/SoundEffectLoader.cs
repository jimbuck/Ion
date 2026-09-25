using NAudio.Wave;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

#pragma warning disable CS0618 // SoundEffect stays public (obsolete) for one release.

public static class SoundEffectAssetManagerExtensions
{
	/// <summary>
	/// Loads the sound effect at <paramref name="path"/>, cached and owned by <paramref name="assetManager"/>.
	/// </summary>
	/// <remarks>
	/// Instance-call syntax (<c>assets.Load&lt;SoundEffect&gt;(path)</c>) now binds to <see cref="IBaseAssetManager.Load{T}(string)"/>,
	/// which returns the same cached sound; this forwarder is only reached through a static call.
	/// </remarks>
	[Obsolete("Use assets.Load<ISoundEffect>(path) and depend on ISoundEffect.")]
	public static SoundEffect Load<T>(this IBaseAssetManager assetManager, string path) where T : SoundEffect
	{
		return (SoundEffect)assetManager.Load<ISoundEffect>(path);
	}
}

/// <summary>
/// Decodes a whole sound file (any format NAudio reads) into memory for <see cref="AudioManager"/>.
/// </summary>
internal class SoundEffectLoader(IPersistentStorage storage) : IAssetLoader<ISoundEffect>
{
	public Type AssetType { get; } = typeof(ISoundEffect);

	public ISoundEffect Load(string assetPath)
	{
		var filepath = storage.Assets.GetPath(assetPath);
		if (!File.Exists(filepath))
		{
			throw new FileNotFoundException($"Sound effect '{assetPath}' was not found at '{filepath}'. File names are case-sensitive on Linux and macOS; check the casing of the name.", filepath);
		}
		using var audioFileReader = new AudioFileReader(filepath);

		// TODO: could add resampling in here if required
		var waveFormat = audioFileReader.WaveFormat;
		var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
		var readBuffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
		int samplesRead;
		while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
		{
			wholeFile.AddRange(readBuffer.Take(samplesRead));
		}
		var audioData = wholeFile.ToArray();

		var soundEffect = new SoundEffect(assetPath, audioData)
		{
			Duration = (float)audioFileReader.TotalTime.TotalSeconds,
			WaveFormat = waveFormat,
		};

		return soundEffect;
	}
}
