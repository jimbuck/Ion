namespace Ion.Extensions.Audio;

internal class AudioSystem(AudioManager audioManager)
{
	[Init]
	public void Init()
	{
		audioManager.Initialize();
	}
}
