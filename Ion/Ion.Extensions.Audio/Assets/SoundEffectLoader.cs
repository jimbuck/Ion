using NAudio.Wave;

using Ion.Extensions.Assets;


namespace Ion.Extensions.Audio;

public class SoundEffectLoader : IAssetLoader
{
	public Type AssetType { get; } = typeof(SoundEffect);

	T IAssetLoader.Load<T>(string filepath)
	{
		if (typeof(T) == typeof(SoundEffect)) return (T)_loadSoundEffect(filepath);

		throw new ArgumentException("Incorrect type specified for loading!", nameof(T));
	}

	private IAsset _loadSoundEffect(string filepath)
	{
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

		var soundEffect = new SoundEffect(filepath, audioData)
		{
			Duration = (float)audioFileReader.TotalTime.TotalSeconds,
			WaveFormat = waveFormat,
		};

		return soundEffect;
	}
}
