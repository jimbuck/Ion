using NAudio.Wave;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

public static class SoundEffectAssetManagerExtensions
{
	public static SoundEffect Load<T>(this IBaseAssetManager assetManager, string path) where T : SoundEffect
	{
		var loader = (SoundEffectLoader)assetManager.GetLoader(typeof(SoundEffect));
		return loader.Load(path);
	}
}

public class SoundEffectLoader(IPersistentStorage storage) : IAssetLoader
{
	public Type AssetType { get; } = typeof(SoundEffect);

	public SoundEffect Load(string assetPath)
	{
		var filepath = storage.Assets.GetPath(assetPath);
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
