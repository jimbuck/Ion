namespace Ion.Extensions.Audio;

/// <summary>
/// One call to <see cref="NullAudioManager.Play"/>.
/// </summary>
/// <param name="Sound">The sound that was played.</param>
/// <param name="Volume">The volume passed to <see cref="IAudioManager.Play"/>, before <see cref="IAudioManager.MasterVolume"/>.</param>
/// <param name="PitchShift">The pitch shift passed to <see cref="IAudioManager.Play"/>.</param>
/// <param name="MasterVolume">The <see cref="IAudioManager.MasterVolume"/> at the time of the call.</param>
public readonly record struct SoundPlay(ISoundEffect Sound, float Volume, float PitchShift, float MasterVolume)
{
	/// <summary>
	/// The volume the sound would have been played at: <see cref="Volume"/> times <see cref="MasterVolume"/>.
	/// </summary>
	public float EffectiveVolume => Volume * MasterVolume;
}

/// <summary>
/// An <see cref="IAudioManager"/> that plays nothing and records every <see cref="Play"/> call in <see cref="Plays"/>,
/// for servers, CI and tests. Registered by <see cref="BuilderExtensions.AddNullAudio"/>.
/// </summary>
public sealed class NullAudioManager : IAudioManager
{
	private readonly Lock _lock = new();
	private readonly List<SoundPlay> _plays = [];

	public float MasterVolume { get; set; } = 1f;

	/// <summary>
	/// Every recorded play, oldest first, until <see cref="Clear"/> is called.
	/// </summary>
	public IReadOnlyList<SoundPlay> Plays
	{
		get
		{
			lock (_lock) return _plays.ToArray();
		}
	}

	public void Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f)
	{
		ArgumentNullException.ThrowIfNull(soundEffect);

		lock (_lock) _plays.Add(new SoundPlay(soundEffect, volume, pitchShift, MasterVolume));
	}

	/// <summary>
	/// Forgets the recorded plays.
	/// </summary>
	public void Clear()
	{
		lock (_lock) _plays.Clear();
	}
}
