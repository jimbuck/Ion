namespace Ion.Extensions.Audio;

/// <summary>
/// Plays sounds through the engine mixer. Every member is called from the game thread; commands are handed to the audio
/// thread once per frame (in the Last stage), so a call never blocks on the audio device.
/// </summary>
/// <remarks>
/// Gains multiply: a voice is heard at its volume, times its bus volume, times <see cref="MasterVolume"/>. Volumes are
/// linear amplitude factors (1 is unchanged, 0 is silent, values above 1 amplify and may clip).
/// </remarks>
public interface IAudioManager
{
	/// <summary>
	/// Gain applied to every voice. Defaults to 1. Same as the volume of <see cref="AudioBus.Master"/>.
	/// </summary>
	float MasterVolume { get; set; }

	/// <summary>
	/// Starts playing <paramref name="soundEffect"/> on a new voice.
	/// </summary>
	/// <param name="soundEffect">The sound to play.</param>
	/// <param name="volume">Gain of this voice, multiplied by its bus volume and <see cref="MasterVolume"/>.</param>
	/// <param name="pitchShift">
	/// Pitch shift in octaves, clamped to [-1, 1]: -1 plays one octave down (half speed), 0 unchanged, 1 one octave up
	/// (double speed). The sound is resampled, so its duration changes with its pitch.
	/// </param>
	/// <param name="pan">Stereo balance in [-1, 1]: -1 is left only, 0 centered (unchanged), 1 right only.</param>
	/// <param name="loop">Whether the sound restarts from its beginning when it ends, until stopped.</param>
	/// <param name="bus">The bus the voice is mixed into.</param>
	/// <param name="fadeIn">Seconds over which the voice fades in from silence. 0 starts at full volume.</param>
	/// <returns>
	/// A handle to the voice, or an invalid handle (<see cref="VoiceHandle.IsValid"/> false) when the sound has no decoded
	/// audio or the voice pool is full and configured to refuse new voices.
	/// </returns>
	VoiceHandle Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f, float pan = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, float fadeIn = 0f);

	/// <summary>
	/// Stops a voice, immediately or after fading out over <paramref name="fadeOut"/> seconds. Does nothing when the voice
	/// has already finished.
	/// </summary>
	void Stop(VoiceHandle voice, float fadeOut = 0f);

	/// <summary>
	/// Stops every voice, immediately or after fading out over <paramref name="fadeOut"/> seconds.
	/// </summary>
	void StopAll(float fadeOut = 0f);

	/// <summary>
	/// Changes the volume of a playing voice. Does nothing when the voice has finished.
	/// </summary>
	void SetVolume(VoiceHandle voice, float volume);

	/// <summary>
	/// Changes the pitch shift (in octaves, see <see cref="Play"/>) of a playing voice. Does nothing when the voice has finished.
	/// </summary>
	void SetPitch(VoiceHandle voice, float pitchShift);

	/// <summary>
	/// Changes the stereo balance (see <see cref="Play"/>) of a playing voice. Does nothing when the voice has finished.
	/// </summary>
	void SetPan(VoiceHandle voice, float pan);

	/// <summary>
	/// Whether the voice is still playing, as seen from the game thread: true from <see cref="Play"/> until it is stopped
	/// without a fade, or until the audio thread reports that it ended (reports are collected once per frame).
	/// </summary>
	bool IsPlaying(VoiceHandle voice);

	/// <summary>
	/// The volume of <paramref name="bus"/>. <see cref="AudioBus.Master"/> is <see cref="MasterVolume"/>.
	/// </summary>
	float GetBusVolume(AudioBus bus);

	/// <summary>
	/// Sets the volume of <paramref name="bus"/>. <see cref="AudioBus.Master"/> sets <see cref="MasterVolume"/>.
	/// </summary>
	void SetBusVolume(AudioBus bus, float volume);
}
