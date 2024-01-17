using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio
{
	public interface IAudioManager
	{
		float MasterVolume { get; set; }

		void Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f);
	}
}