using NAudio.Wave;

namespace Ion.Extensions.Audio;

internal class SoundEffectSampleProvider(SoundEffect cachedSound) : ISampleProvider
{
	private long position;

	public WaveFormat WaveFormat => cachedSound.WaveFormat;

	public int Read(float[] buffer, int offset, int count)
	{
		var availableSamples = cachedSound.AudioData.Length - position;
		var samplesToCopy = Math.Min(availableSamples, count);
		Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
		position += samplesToCopy;
		return (int)samplesToCopy;
	}
}