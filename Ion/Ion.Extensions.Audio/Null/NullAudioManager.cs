using Microsoft.Extensions.Logging;

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

	/// <summary>The stereo balance passed to <see cref="IAudioManager.Play"/>.</summary>
	public float Pan { get; init; }

	/// <summary>Whether the sound was played looping.</summary>
	public bool Loop { get; init; }

	/// <summary>The bus the sound was played on.</summary>
	public AudioBus Bus { get; init; } = AudioBus.Sfx;

	/// <summary>The voice the mixer started, or an invalid handle when the sound has no decoded audio.</summary>
	public VoiceHandle Voice { get; init; }
}

/// <summary>
/// The headless <see cref="IAudioManager"/>, for servers, CI and tests: the real mixer on a <see cref="NullAudioOutput"/>
/// driven by the game clock, so nothing is heard but everything is mixed deterministically (see <see cref="Output"/>),
/// and every <see cref="Play"/> call is recorded in <see cref="Plays"/>. Registered by
/// <see cref="BuilderExtensions.AddNullAudio(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// </summary>
public sealed class NullAudioManager : AudioManager
{
	private readonly Lock _lock = new();
	private readonly List<SoundPlay> _plays = [];

	/// <summary>
	/// Creates a stand-alone headless manager with the default <see cref="AudioConfig"/>.
	/// </summary>
	public NullAudioManager()
		: this(new AudioMixer(new AudioConfig()), new NullAudioOutput(), null)
	{
	}

	/// <summary>
	/// Creates a headless manager over <paramref name="mixer"/> and <paramref name="output"/>.
	/// </summary>
	public NullAudioManager(AudioMixer mixer, NullAudioOutput output, ILogger<AudioManager>? logger = null)
		: base(mixer, output, logger)
	{
		NullOutput = output;
	}

	/// <summary>
	/// The null output the mix goes to. Set <see cref="NullAudioOutput.CaptureEnabled"/> to keep the mixed samples.
	/// </summary>
	public NullAudioOutput NullOutput { get; }

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

	/// <inheritdoc/>
	public override VoiceHandle Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f, float pan = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, float fadeIn = 0f)
	{
		ArgumentNullException.ThrowIfNull(soundEffect);

		var master = MasterVolume;
		var voice = base.Play(soundEffect, volume, pitchShift, pan, loop, bus, fadeIn);

		lock (_lock) _plays.Add(new SoundPlay(soundEffect, volume, pitchShift, master) { Pan = pan, Loop = loop, Bus = bus, Voice = voice });

		return voice;
	}

	/// <summary>
	/// Forgets the recorded plays.
	/// </summary>
	public void Clear()
	{
		lock (_lock) _plays.Clear();
	}
}
