using Ion.Extensions.Assets;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ion.Extensions.Audio;

public class AudioManager : IAudioManager, IDisposable
{
	private readonly IWavePlayer _outputDevice;
	private readonly MixingSampleProvider _mixer;

	public float MasterVolume { get; set; } = 1f;

	public AudioManager()
	{
		_outputDevice = new WaveOutEvent();
		_mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
		_outputDevice.Init(_mixer);
		_outputDevice.Play();
	}

	public void Play(ISoundEffect genericSoundEffect, float volume = 1f, float pitchShift = 0f)
	{
		if (genericSoundEffect is not SoundEffect soundEffect)
		{
			throw new NotImplementedException($"ISoundEffect type {genericSoundEffect.GetType().FullName} not supported!");
		}

		ISampleProvider sampleProvider = new SoundEffectSampleProvider(soundEffect);

		if (volume is 0) return;

		if (volume is not 1)
		{
			sampleProvider = new VolumeSampleProvider(sampleProvider) { Volume = volume * MasterVolume };
		}

		if (pitchShift is not 0)
		{
			// less than zero [-1, 0] -> [0.5, 1]
			// greater than zero [0, 1] ->  [1, 2]
			var naudioPitch = pitchShift < 0 ? (pitchShift / 2f) + 1f : (pitchShift + 1f);

			sampleProvider = new SmbPitchShiftingSampleProvider(sampleProvider) { PitchFactor = naudioPitch };
		}

		_addMixerInput(sampleProvider);
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

	public void Dispose()
	{
		_outputDevice.Dispose();
	}
}
