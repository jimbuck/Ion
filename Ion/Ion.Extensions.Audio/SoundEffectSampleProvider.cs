using NAudio.Wave;

namespace Ion.Extensions.Audio;

#pragma warning disable CS0618 // SoundEffect stays public (obsolete) for one release.

internal class SoundEffectSampleProvider(SoundEffect cachedSound) : ISampleProvider
{
	private int position;

	public WaveFormat WaveFormat => cachedSound.WaveFormat;

	public int Read(float[] buffer, int offset, int count)
	{
		count = Math.Min(cachedSound.AudioData.Length - position, count);
		Array.Copy(cachedSound.AudioData, position, buffer, offset, count);
		position += count;
		return count;
	}
}