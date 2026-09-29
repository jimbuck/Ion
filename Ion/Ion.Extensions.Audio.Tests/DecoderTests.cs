using static Ion.Extensions.Audio.Tests.AudioTestUtils;

namespace Ion.Extensions.Audio.Tests;

public class DecoderTests
{
	private static readonly float[] StereoFrames = [0f, 0.5f, -0.5f, 0.25f, 0.75f, -0.75f, -1f, 0.125f];

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(1, 8, false)]
	[InlineData(1, 16, false)]
	[InlineData(1, 24, false)]
	[InlineData(1, 32, false)]
	[InlineData(3, 32, false)]
	[InlineData(3, 64, false)]
	[InlineData(1, 16, true)]
	[InlineData(3, 32, true)]
	public void Wav_DecodesEachEncoding(int format, int bits, bool extensible)
	{
		var wav = Wav(StereoFrames, channels: 2, sampleRate: 22050, bits, format, extensible);

		var decoded = WavDecoder.Decode(wav);

		Assert.Equal(2, decoded.Channels);
		Assert.Equal(22050, decoded.SampleRate);
		Assert.Equal(StereoFrames.Length, decoded.Samples.Length);
		var tolerance = format == 3 ? 0f : (float)Math.Pow(2, 1 - bits);
		for (var i = 0; i < StereoFrames.Length; i++) Assert.Equal(StereoFrames[i], decoded.Samples[i], tolerance);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(false)]
	[InlineData(true)]
	public void Wav_DecodesMonoAndSkipsUnknownChunks(bool listChunk)
	{
		float[] mono = [0.5f, -0.25f, 0.125f];
		var decoded = WavDecoder.Decode(Wav(mono, 1, 11025, 16, listChunk: listChunk));

		Assert.Equal(1, decoded.Channels);
		Assert.Equal(11025, decoded.SampleRate);
		Assert.Equal(mono, decoded.Samples);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Wav_KeepsTheFirstTwoOfMoreChannels()
	{
		float[] quad = [0.5f, -0.5f, 0.25f, -0.25f, 0.125f, -0.125f, 0.75f, -0.75f];
		var decoded = WavDecoder.Decode(Wav(quad, 4, 48000, 16));

		Assert.Equal(2, decoded.Channels);
		Assert.Equal(4, decoded.SourceChannels);
		Assert.Equal([0.5f, -0.5f, 0.125f, -0.125f], decoded.Samples);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Wav_RejectsUnsupportedEncodings()
	{
		var adpcm = Wav([0f, 0f], 1, 8000, 16, format: 2);
		Assert.Throws<InvalidDataException>(() => WavDecoder.Decode(adpcm));
		Assert.Throws<InvalidDataException>(() => SoundDecoder.Decode("x.bin", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, Rate));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Decode_ResamplesToTheOutputRateAndKeepsTheFileFacts()
	{
		var frames = 4410;
		var wav = Wav(Sine(440, 44100, frames), 1, 44100, 16);

		var sound = SoundDecoder.Decode("tone.wav", wav, Rate);

		Assert.Equal(1, sound.Channels);
		Assert.Equal(44100, sound.SampleRate);
		Assert.Equal(Rate, sound.MixRate);
		Assert.Equal(4800, sound.Frames);
		Assert.Equal(0.1f, sound.Duration, 4);
		Assert.Equal(440, Frequency(sound.Samples, 1, 0, Rate, skipFrames: 64), 0.5);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(44100, 48000)]
	[InlineData(22050, 48000)]
	[InlineData(8000, 48000)]
	[InlineData(48000, 44100)]
	[InlineData(96000, 48000)]
	[InlineData(44056, 48000)] // an awkward ratio: 6000 filter phases
	public void Resample_KeepsA440HzToneAtItsFrequency(int from, int to)
	{
		var input = Sine(440, from, from / 5); // 0.2 s
		var output = Resampler.Resample(input, 1, from, to);

		Assert.Equal((int)Math.Ceiling(input.Length * (double)to / from), output.Length);

		// Away from the edges the result matches the ideal tone at the new rate.
		var maxError = 0.0;
		for (var j = 100; j < output.Length - 100; j++)
		{
			var ideal = 0.5 * Math.Sin(2 * Math.PI * 440 * j / to);
			maxError = Math.Max(maxError, Math.Abs(output[j] - ideal));
		}

		Assert.True(maxError < 2e-3, $"max error {maxError}");
		Assert.Equal(440, Frequency(output, 1, 0, to, skipFrames: 100), 0.2);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Resample_KeepsStereoChannelsApartAndDcLevel()
	{
		var input = new float[2000];
		for (var i = 0; i < 1000; i++)
		{
			input[i * 2] = 0.5f;
			input[i * 2 + 1] = -0.25f;
		}

		var output = Resampler.Resample(input, 2, 32000, 48000);

		for (var j = 50; j < output.Length / 2 - 50; j++)
		{
			Assert.Equal(0.5f, output[j * 2], 1e-4f);
			Assert.Equal(-0.25f, output[j * 2 + 1], 1e-4f);
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Resample_AttenuatesFrequenciesAboveTheNewNyquistWhenDownsampling()
	{
		// 20 kHz is above the 11.025 kHz Nyquist frequency of 22.05 kHz: it must not alias into the audible band.
		var input = Sine(20000, 48000, 9600);
		var output = Resampler.Resample(input, 1, 48000, 22050);

		Assert.True(Peak(output, 1, 0, skipFrames: 100) < 0.01f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OggVorbis_DecodesAStereoTone()
	{
		// tone440_stereo_44k.ogg: 0.25 s at 44.1 kHz, 440 Hz, left amplitude 0.5, right 0.25 (encoded with libsndfile).
		var sound = SoundDecoder.Decode("tone440_stereo_44k.ogg", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "tone440_stereo_44k.ogg")), Rate);

		Assert.Equal(SoundFormat.OggVorbis, SoundDecoder.Detect("OggS"u8));
		Assert.Equal(2, sound.Channels);
		Assert.Equal(44100, sound.SampleRate);
		Assert.Equal(0.25f, sound.Duration, 2);
		Assert.Equal(440, Frequency(sound.Samples, 2, 0, Rate, skipFrames: 1000), 1.0);
		Assert.Equal(0.5f, Peak(sound.Samples, 2, 0, skipFrames: 1000), 0.03f);
		Assert.Equal(0.25f, Peak(sound.Samples, 2, 1, skipFrames: 1000), 0.03f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Mp3_DecodesAMonoTone()
	{
		// tone440_mono_22k.mp3: 0.25 s at 22.05 kHz, 440 Hz, amplitude 0.5 (MPEG-2 layer III, encoded with libsndfile/LAME).
		var sound = SoundDecoder.Decode("tone440_mono_22k.mp3", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "tone440_mono_22k.mp3")), Rate);

		Assert.Equal(1, sound.Channels);
		Assert.Equal(22050, sound.SampleRate);
		Assert.InRange(sound.Duration, 0.24f, 0.4f); // encoder delay and padding add a frame or two
		Assert.Equal(440, Frequency(sound.Samples, 1, 0, Rate, skipFrames: 2500), 1.0);
		Assert.Equal(0.5f, Peak(sound.Samples, 1, 0, skipFrames: 2500), 0.05f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Detect_UsesMagicBytesThenTheExtension()
	{
		Assert.Equal(SoundFormat.Wav, SoundDecoder.Detect(Wav([0f], 1, 8000, 16)));
		Assert.Equal(SoundFormat.Mp3, SoundDecoder.Detect("ID3\u0004"u8));
		Assert.Equal(SoundFormat.Mp3, SoundDecoder.Detect([0xFF, 0xFB, 0x90, 0x64]));
		Assert.Equal(SoundFormat.OggVorbis, SoundDecoder.Detect([0, 0, 0, 0], "music.ogg"));
		Assert.Equal(SoundFormat.Unknown, SoundDecoder.Detect([0, 0, 0, 0], "notes.txt"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void BreakoutAssets_Decode()
	{
		var bonk = SoundDecoder.Decode("bonk.wav", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "bonk.wav")), Rate);
		var ping = SoundDecoder.Decode("ping.mp3", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "ping.mp3")), Rate);

		// bonk.wav: 2 channels, 48 kHz, 32-bit, 63264 data bytes.
		Assert.Equal(2, bonk.Channels);
		Assert.Equal(48000, bonk.SampleRate);
		Assert.Equal(63264 / 8, bonk.Frames);
		Assert.True(Peak(bonk.Samples, 2, 0) > 0.01f);

		Assert.InRange(ping.Channels, 1, 2);
		Assert.True(ping.Duration > 0.05f);
		Assert.True(Peak(ping.Samples, ping.Channels, 0) > 0.01f);
	}
}
