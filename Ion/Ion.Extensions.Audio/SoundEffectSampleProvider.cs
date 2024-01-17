using NAudio.Wave;

namespace Ion.Extensions.Audio;

internal class SoundEffectSampleProvider : ISampleProvider
{
	private readonly SoundEffect cachedSound;
	private long position;

	public WaveFormat WaveFormat => cachedSound.WaveFormat;

	public SoundEffectSampleProvider(SoundEffect cachedSound)
	{
		this.cachedSound = cachedSound;
	}

	public int Read(float[] buffer, int offset, int count)
	{
		var availableSamples = cachedSound.AudioData.Length - position;
		var samplesToCopy = Math.Min(availableSamples, count);
		Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
		position += samplesToCopy;
		return (int)samplesToCopy;
	}
}