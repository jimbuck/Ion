namespace Ion.Extensions.Audio;

public class AudioSystem(IAudioManager audioManager)
{
	private readonly AudioManager _audioManager = (AudioManager)audioManager;

	[Init]
	public void Init()
	{
		_audioManager.Initialize();
	}
}
