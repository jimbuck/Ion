using System.Buffers.Binary;

namespace Ion.Extensions.Graphics.Tests;

public class NullAudioTests
{
	private static MemoryStream CreateWav(int channels, int sampleRate, int bitsPerSample, int frames, bool withListChunk = false)
	{
		var blockAlign = channels * bitsPerSample / 8;
		var dataSize = frames * blockAlign;
		var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);

		writer.Write("RIFF"u8);
		writer.Write(0); // RIFF size, not needed by the reader
		writer.Write("WAVE"u8);

		if (withListChunk)
		{
			writer.Write("LIST"u8);
			writer.Write(3);
			writer.Write(new byte[] { 1, 2, 3, 0 }); // 3 bytes plus a pad byte
		}

		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)channels);
		writer.Write(sampleRate);
		writer.Write(sampleRate * blockAlign);
		writer.Write((short)blockAlign);
		writer.Write((short)bitsPerSample);

		writer.Write("data"u8);
		writer.Write(dataSize);
		writer.Write(new byte[dataSize]);

		stream.Position = 0;
		return stream;
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(false)]
	[InlineData(true)]
	public void NullSoundEffectLoader_ReadsTheWavHeader(bool withListChunk)
	{
		using var wav = CreateWav(channels: 2, sampleRate: 44100, bitsPerSample: 16, frames: 22050, withListChunk);

		var sound = NullSoundEffectLoader.Read("test.wav", wav);

		Assert.Equal(2, sound.Channels);
		Assert.Equal(44100, sound.SampleRate);
		Assert.Equal(16, sound.BitsPerSample);
		Assert.Equal(0.5f, sound.Duration, 5);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullSoundEffectLoader_LoadsOtherFormatsWithoutHeaderData()
	{
		using var mp3 = new MemoryStream([0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

		var sound = NullSoundEffectLoader.Read("ping.mp3", mp3);

		Assert.Equal(0f, sound.Duration);
		Assert.Equal(0, sound.Channels);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void LoadISoundEffect_ReadsARealWavFileThroughTheAssetManager()
	{
		using var app = TestApp.CreateHeadless();
		using var scope = app.Services.CreateScope();
		var assets = scope.ServiceProvider.GetRequiredService<IAssetManager>();

		var sound = Assert.IsType<NullSoundEffect>(assets.Load<ISoundEffect>("bonk.wav"));

		// bonk.wav: 2 channels, 48 kHz, 32-bit, 63264 data bytes.
		Assert.Equal(2, sound.Channels);
		Assert.Equal(48000, sound.SampleRate);
		Assert.Equal(63264f / 384000f, sound.Duration, 5);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullAudioManager_RecordsPlays()
	{
		var audio = new NullAudioManager { MasterVolume = 0.5f };
		var sound = new NullSoundEffect("s", 1f, 1, 8000, 8);

		audio.Play(sound);
		audio.Play(sound, volume: 0.8f, pitchShift: -0.25f);

		Assert.Equal([new SoundPlay(sound, 1f, 0f, 0.5f), new SoundPlay(sound, 0.8f, -0.25f, 0.5f)], audio.Plays);
		Assert.Equal(0.4f, audio.Plays[1].EffectiveVolume, 5);

		audio.Clear();
		Assert.Empty(audio.Plays);
	}
}
