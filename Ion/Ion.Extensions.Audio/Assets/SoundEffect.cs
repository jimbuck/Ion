using Ion.Extensions.Assets;

using NAudio.Wave;

namespace Ion.Extensions.Audio
{
	public class SoundEffect : ISoundEffect
	{
		public int Id { get; }

		public string Name { get; }

		public float Duration { get; init; }

		public required WaveFormat WaveFormat { get; init; }

		public float[] AudioData { get; }

		public SoundEffect(string name, float[] audioData)
		{
			Id = audioData.GetHashCode();
			Name = name;
			AudioData = audioData;
		}

		public void Dispose()
		{
			// Nothing to dispose.
		}
	}
}
