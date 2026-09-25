using Ion.Extensions.Debug;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ion.Extensions.Audio;

#pragma warning disable CS0618 // SoundEffect stays public (obsolete) for one release.

/// <summary>
/// Plays sounds through DirectSound (NAudio). Registered by <see cref="BuilderExtensions.AddAudio"/>.
/// </summary>
internal class AudioManager(ITraceTimer<AudioManager> trace) : IAudioManager, IDisposable
{
	private readonly DirectSoundOut _outputDevice = new();
	private readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };

	/// <summary>
	/// Multiplier applied to every sound's volume. Defaults to 1 (unchanged).
	/// </summary>
	public float MasterVolume { get; set; } = 1f;

	public void Initialize()
	{
		var timer = trace.Start("AudioManager::Initialize");

		_outputDevice.Init(_mixer);
		_outputDevice.Play();

		timer.Stop();
	}

	/// <summary>
	/// Plays a sound effect.
	/// </summary>
	/// <param name="genericSoundEffect">The sound to play.</param>
	/// <param name="volume">Volume of this sound, multiplied by <see cref="MasterVolume"/>.</param>
	/// <param name="pitchShift">
	/// Pitch shift in [-1, 1]: -1 is one octave down (factor 0.5), 0 is unchanged, 1 is one octave up (factor 2).
	/// Applied with NAudio's <see cref="SmbPitchShiftingSampleProvider"/>, which keeps the duration unchanged.
	/// </param>
	public void Play(ISoundEffect genericSoundEffect, float volume = 1f, float pitchShift = 0f)
	{
		var timer = trace.Start("AudioManager::Play");

		try
		{
			if (genericSoundEffect is not SoundEffect soundEffect)
			{
				throw new NotImplementedException($"ISoundEffect type {genericSoundEffect.GetType().FullName} not supported!");
			}

			var finalVolume = volume * MasterVolume;
			if (finalVolume <= 0f) return;

			ISampleProvider sampleProvider = new SoundEffectSampleProvider(soundEffect);

			if (pitchShift != 0f)
			{
				sampleProvider = new SmbPitchShiftingSampleProvider(sampleProvider) { PitchFactor = ToPitchFactor(pitchShift) };
			}

			sampleProvider = new VolumeSampleProvider(sampleProvider) { Volume = finalVolume };

			_addMixerInput(sampleProvider);
		}
		finally
		{
			timer.Stop();
		}
	}

	/// <summary>
	/// Maps a pitch shift in [-1, 1] to a pitch factor in [0.5, 2]: [-1, 0] maps to [0.5, 1] and [0, 1] maps to [1, 2].
	/// </summary>
	internal static float ToPitchFactor(float pitchShift)
	{
		pitchShift = Math.Clamp(pitchShift, -1f, 1f);
		return pitchShift < 0 ? (pitchShift / 2f) + 1f : (pitchShift + 1f);
	}

	private ISampleProvider _convertToRightChannelCount(ISampleProvider input)
	{
		if (input.WaveFormat.Channels == _mixer.WaveFormat.Channels)
		{
			return input;
		}

		if (input.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
		{
			return new MonoToStereoSampleProvider(input);
		}

		throw new NotImplementedException("Not yet implemented this channel count conversion");
	}

	private void _addMixerInput(ISampleProvider input)
	{
		_mixer.AddMixerInput(_convertToRightChannelCount(input));
	}

	private bool _disposed;

	public void Dispose()
	{
		// Registered both as itself and as IAudioManager, so the container may call this twice.
		if (_disposed) return;
		_disposed = true;

		_outputDevice.Dispose();
	}
}
