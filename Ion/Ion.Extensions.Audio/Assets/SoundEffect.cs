using Ion.Extensions.Assets;

using NAudio.Wave;

namespace Ion.Extensions.Audio
{
	/// <summary>
	/// A sound decoded into memory by NAudio, played by the DirectSound <see cref="IAudioManager"/>.
	/// </summary>
	[Obsolete("Depend on ISoundEffect and load it with IAssetManager.Load<ISoundEffect>(path) so the game also runs with AddNullAudio. SoundEffect becomes internal in the next release.")]
	public class SoundEffect : ISoundEffect
	{
		public nint Id { get; }

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
