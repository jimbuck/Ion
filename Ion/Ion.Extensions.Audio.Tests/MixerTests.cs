using static Ion.Extensions.Audio.Tests.AudioTestUtils;

namespace Ion.Extensions.Audio.Tests;

public class MixerTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void Gain_IsVoiceTimesBusTimesMaster()
	{
		var mixer = Mixer();
		mixer.MasterVolume = 0.5f;
		mixer.SetBusVolume(AudioBus.Sfx, 0.8f);
		mixer.SetBusVolume(AudioBus.Music, 0.25f);

		mixer.Play(Constant(0.5f, 1000), volume: 0.5f);
		var sfx = Render(mixer, 64);
		Assert.All(sfx, s => Assert.Equal(0.5f * 0.5f * 0.8f * 0.5f, s, 1e-7f));

		mixer.StopAll();
		mixer.Play(Constant(0.5f, 1000), volume: 1f, bus: AudioBus.Music);
		var music = Render(mixer, 64);
		Assert.All(music, s => Assert.Equal(0.5f * 0.25f * 0.5f, s, 1e-7f));

		Assert.Equal(0.5f, mixer.GetBusVolume(AudioBus.Master));
		Assert.Equal(0.8f, mixer.GetBusVolume(AudioBus.Sfx));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void VolumeChanges_RampOverOneBufferThenHold()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Constant(1f, 10_000));
		Render(mixer, 64);

		mixer.SetVolume(voice, 0.5f);
		var ramp = Render(mixer, 100);
		var held = Render(mixer, 100);

		// The first frame of the ramp is one step down from 1, the last is at the target.
		Assert.Equal(1f - 0.5f / 100, ramp[0], 1e-6f);
		Assert.Equal(0.5f, ramp[^1], 1e-6f);
		for (var i = 1; i < 100; i++) Assert.True(ramp[i * 2] < ramp[(i - 1) * 2]);
		Assert.All(held, s => Assert.Equal(0.5f, s, 1e-7f));
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(0f, 1f, 1f)]
	[InlineData(-1f, 1f, 0f)]
	[InlineData(1f, 0f, 1f)]
	[InlineData(0.5f, 0.5f, 1f)]
	[InlineData(-0.25f, 1f, 0.75f)]
	public void Pan_IsALinearBalance(float pan, float left, float right)
	{
		var mixer = Mixer();
		mixer.Play(Constant(0.5f, 1000), pan: pan);
		var mono = Render(mixer, 32);

		var stereoMixer = Mixer();
		var stereo = new SoundEffect("stereo", Enumerable.Range(0, 2000).Select(i => i % 2 == 0 ? 0.5f : -0.25f).ToArray(), 2, Rate);
		stereoMixer.Play(stereo, pan: pan);
		var both = Render(stereoMixer, 32);

		for (var i = 0; i < 32; i++)
		{
			Assert.Equal(0.5f * left, mono[i * 2], 1e-7f);
			Assert.Equal(0.5f * right, mono[i * 2 + 1], 1e-7f);
			Assert.Equal(0.5f * left, both[i * 2], 1e-7f);
			Assert.Equal(-0.25f * right, both[i * 2 + 1], 1e-7f);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SetPan_MovesAPlayingVoice()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Constant(1f, 10_000));
		Render(mixer, 16);

		mixer.SetPan(voice, -1f);
		Render(mixer, 16); // ramp
		var panned = Render(mixer, 16);

		Assert.Equal(1f, panned[0]);
		Assert.Equal(0f, panned[1]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PitchShift_OctaveUpReadsEveryOtherFrameAndHalvesTheDuration()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Ramp(1000, 1000f), pitchShift: 1f);

		var output = Render(mixer, 600);

		for (var i = 0; i < 500; i++) Assert.Equal(2 * i / 1000f, output[i * 2], 1e-6f);
		for (var i = 500; i < 600; i++) Assert.Equal(0f, output[i * 2]);

		mixer.Flush();
		Assert.False(mixer.IsPlaying(voice));
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(AudioInterpolation.Linear)]
	[InlineData(AudioInterpolation.Cubic)]
	public void PitchShift_OctaveDownInterpolatesBetweenFrames(AudioInterpolation interpolation)
	{
		// Both interpolations reproduce a straight line exactly.
		var mixer = Mixer(interpolation: interpolation);
		mixer.Play(Ramp(1000, 1000f), pitchShift: -1f);

		var output = Render(mixer, 1000);

		for (var i = 2; i < 1000; i++) Assert.Equal(i / 2f / 1000f, output[i * 2], 1e-5f);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(AudioInterpolation.Linear, 1f, 880)]
	[InlineData(AudioInterpolation.Cubic, 1f, 880)]
	[InlineData(AudioInterpolation.Linear, -1f, 220)]
	[InlineData(AudioInterpolation.Cubic, 0.5f, 622.25)]
	public void PitchShift_ScalesTheFrequencyOfATone(AudioInterpolation interpolation, float shift, double expected)
	{
		var mixer = Mixer(interpolation: interpolation);
		var tone = new SoundEffect("a4", Sine(440, Rate, Rate), 1, Rate);

		mixer.Play(tone, pitchShift: shift);
		var output = Render(mixer, Rate / 4);

		Assert.Equal(expected, Frequency(output, 2, 0, Rate), 0.5);
		Assert.Equal(expected, Frequency(output, 2, 1, Rate), 0.5);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SetPitch_ChangesTheRateOfAPlayingVoice()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Ramp(10_000, 10_000f));
		var first = Render(mixer, 100);
		mixer.SetPitch(voice, 1f);
		var second = Render(mixer, 100);

		Assert.Equal(99 / 10_000f, first[99 * 2], 1e-6f);
		Assert.Equal(100 / 10_000f, second[0], 1e-6f);
		Assert.Equal(102 / 10_000f, second[2], 1e-6f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Loop_WrapsAroundUntilStopped()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Ramp(100, 100f), loop: true);

		var output = Render(mixer, 350);
		for (var i = 0; i < 350; i++) Assert.Equal((i % 100) / 100f, output[i * 2], 1e-6f);

		mixer.Flush();
		Assert.True(mixer.IsPlaying(voice));

		mixer.Stop(voice);
		Assert.False(mixer.IsPlaying(voice));
		Assert.All(Render(mixer, 10), s => Assert.Equal(0f, s));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OneShot_EndsAndIsReportedFinished()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Constant(0.5f, 100));
		Assert.True(mixer.IsPlaying(voice));

		var output = Render(mixer, 150);
		Assert.Equal(0.5f, output[99 * 2]);
		Assert.Equal(0f, output[100 * 2]);

		// The report reaches the game side on the next flush.
		Assert.True(mixer.IsPlaying(voice));
		mixer.Flush();
		Assert.False(mixer.IsPlaying(voice));
		Assert.Equal(0, mixer.ActiveVoices);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FadeIn_RisesLinearly()
	{
		var mixer = Mixer();
		mixer.Play(Constant(1f, 10_000), fadeIn: 100f / Rate);

		var output = Render(mixer, 200);

		for (var i = 0; i < 100; i++) Assert.Equal(i / 100f, output[i * 2], 1e-6f);
		for (var i = 100; i < 200; i++) Assert.Equal(1f, output[i * 2]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FadeOut_FallsLinearlyThenStops()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Constant(1f, 10_000));
		Render(mixer, 10);

		mixer.Stop(voice, fadeOut: 100f / Rate);
		Assert.True(mixer.IsPlaying(voice)); // still audible while fading
		var output = Render(mixer, 200);

		for (var i = 0; i < 100; i++) Assert.Equal(1f - i / 100f, output[i * 2], 1e-6f);
		for (var i = 100; i < 200; i++) Assert.Equal(0f, output[i * 2]);

		mixer.Flush();
		Assert.False(mixer.IsPlaying(voice));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Output_IsClampedToFullScale()
	{
		var mixer = Mixer();
		mixer.Play(Constant(0.8f, 100));
		mixer.Play(Constant(0.8f, 100));
		mixer.Play(Constant(-0.8f, 100), pan: 1f);

		var output = Render(mixer, 10);

		Assert.Equal(1f, output[0]);  // 0.8 + 0.8, clamped
		Assert.Equal(0.8f, output[1], 1e-6f);  // 0.8 + 0.8 - 0.8
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void VoicePool_StealsTheOldestVoiceWhenFull()
	{
		var mixer = Mixer(maxVoices: 4);
		var looping = mixer.Play(Constant(0.1f, 1000), loop: true);
		var handles = Enumerable.Range(0, 3).Select(_ => mixer.Play(Constant(0.1f, 1000))).ToArray();

		var fifth = mixer.Play(Constant(0.1f, 1000));

		// The oldest one-shot is replaced; the looping voice, although older, is kept.
		Assert.True(fifth.IsValid);
		Assert.Equal(handles[0].Slot, fifth.Slot);
		Assert.False(mixer.IsPlaying(handles[0]));
		Assert.True(mixer.IsPlaying(looping));
		Assert.True(mixer.IsPlaying(fifth));
		Assert.Equal(1, mixer.StolenVoices);
		Assert.Equal(4, mixer.ActiveVoices);

		// Commands for the stolen voice are ignored.
		mixer.SetVolume(handles[0], 0f);
		var output = Render(mixer, 4);
		Assert.Equal(0.4f, output[0], 1e-6f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void VoicePool_RefusesNewVoicesWhenConfigured()
	{
		var mixer = Mixer(maxVoices: 2, stealing: VoiceStealing.Refuse);
		var a = mixer.Play(Constant(0.1f, 1000));
		var b = mixer.Play(Constant(0.1f, 1000));

		var refused = mixer.Play(Constant(0.1f, 1000));

		Assert.False(refused.IsValid);
		Assert.Equal(VoiceHandle.None, refused);
		Assert.True(mixer.IsPlaying(a));
		Assert.True(mixer.IsPlaying(b));
		Assert.Equal(1, mixer.RefusedVoices);

		// A slot frees up when a voice stops.
		mixer.Stop(a);
		Assert.True(mixer.Play(Constant(0.1f, 1000)).IsValid);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Handles_OfReusedSlotsAreStale()
	{
		var mixer = Mixer(maxVoices: 1);
		var first = mixer.Play(Constant(0.25f, 1000));
		mixer.Stop(first);
		var second = mixer.Play(Constant(0.5f, 1000));

		Assert.Equal(first.Slot, second.Slot);
		Assert.NotEqual(first.Generation, second.Generation);

		mixer.SetVolume(first, 0f); // stale: ignored
		mixer.Stop(first);          // stale: ignored

		var output = Render(mixer, 8);
		Assert.All(output, s => Assert.Equal(0.5f, s));
		Assert.True(mixer.IsPlaying(second));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Commands_AreAppliedInIssueOrder()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Constant(1f, 1000));
		mixer.SetVolume(voice, 0.25f);
		mixer.SetVolume(voice, 0.5f);
		mixer.SetPan(voice, 1f);
		mixer.SetBusVolume(AudioBus.Sfx, 0.5f);

		var output = Render(mixer, 8);

		// Applied before the first frame, so the voice starts at its final gain without a ramp.
		for (var i = 0; i < 8; i++)
		{
			Assert.Equal(0f, output[i * 2]);
			Assert.Equal(0.25f, output[i * 2 + 1]);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Commands_BeyondTheQueueCapacityWaitForTheNextFlush()
	{
		var mixer = Mixer(maxVoices: 64, commandCapacity: 16);
		var sound = Constant(0.01f, 100_000);
		for (var i = 0; i < 40; i++) mixer.Play(sound);

		var first = Render(mixer, 4);
		Assert.Equal(24, mixer.PendingCommands);
		Assert.Equal(0.16f, first[0], 1e-6f);

		Render(mixer, 4);
		var third = Render(mixer, 4);
		Assert.Equal(0, mixer.PendingCommands);
		Assert.Equal(0.40f, third[0], 1e-5f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Play_WithoutDecodedAudioReturnsAnInvalidHandle()
	{
		var mixer = Mixer();
		var headerOnly = new NullSoundEffect("x.mp3", 0f, 0, 0, 0);

		Assert.False(mixer.Play(headerOnly).IsValid);
		Assert.Equal(0, mixer.ActiveVoices);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(-1f, 0.5)]
	[InlineData(-2f, 0.5)]
	[InlineData(0f, 1.0)]
	[InlineData(0.5f, 1.4142135623730951)]
	[InlineData(1f, 2.0)]
	[InlineData(float.NaN, 1.0)]
	public void ToPitchFactor_IsTwoToThePowerOfOctaves(float shift, double factor)
	{
		Assert.Equal(factor, AudioMixer.ToPitchFactor(shift), 12);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SoundsAtAnotherRate_AreResampledByTheVoice()
	{
		// A sound stored at 24 kHz plays at the right speed on a 48 kHz mixer.
		var mixer = Mixer();
		var tone = new SoundEffect("a4@24k", Sine(440, 24000, 24000), 1, 24000);

		mixer.Play(tone);
		var output = Render(mixer, Rate / 4);

		Assert.Equal(440, Frequency(output, 2, 0, Rate), 0.5);
	}
}
