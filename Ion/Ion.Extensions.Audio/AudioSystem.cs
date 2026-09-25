namespace Ion.Extensions.Audio;

/// <summary>
/// Opens the audio device in Init (order <see cref="StageOrder.Audio"/>).
/// </summary>
internal class AudioSystem(AudioManager audioManager)
{
	[Init(Order = StageOrder.Audio)]
	public void Init()
	{
		audioManager.Initialize();
	}
}
