using Microsoft.Extensions.Options;

using Ion;

using static Ion.Extensions.Audio.Tests.AudioTestUtils;

namespace Ion.Extensions.Audio.Tests;

/// <summary>Streamed sounds: long tracks decoded while they play, with a small fixed footprint, loops and seeks.</summary>
public sealed class StreamingTests : IDisposable
{
	private readonly string _root = Directory.CreateTempSubdirectory("ion-audio-stream").FullName;

	public void Dispose() => Directory.Delete(_root, recursive: true);

	private SoundEffectLoader Loader(float streamLongerThan = 10f) =>
		new(new FolderStorage(_root), Options.Create(new AudioConfig { OutputRate = Rate, StreamLongerThan = streamLongerThan }));

	/// <summary>Writes a 16-bit WAV straight to disk: <paramref name="value"/> gives each channel's sample of each frame.</summary>
	private void WriteWav(string name, int seconds, int rate, int channels, Func<long, int, float> value)
	{
		var frames = (long)seconds * rate;
		using var file = File.Create(Path.Combine(_root, name));
		using var w = new BinaryWriter(file);
		var data = frames * channels * 2;
		w.Write("RIFF"u8);
		w.Write((uint)(36 + data));
		w.Write("WAVEfmt "u8);
		w.Write(16);
		w.Write((ushort)1);
		w.Write((ushort)channels);
		w.Write(rate);
		w.Write(rate * channels * 2);
		w.Write((ushort)(channels * 2));
		w.Write((ushort)16);
		w.Write("data"u8);
		w.Write((uint)data);
		for (long f = 0; f < frames; f++)
		{
			for (var c = 0; c < channels; c++) w.Write((short)Math.Round(Math.Clamp(value(f, c), -1f, 1f) * 32767f));
		}
	}

	/// <summary>A mono ramp at the output rate whose frame f is (f mod 32768) / 32768: exact through 16-bit PCM and the mixer.</summary>
	private void WriteRamp(string name, int seconds) => WriteWav(name, seconds, Rate, 1, (f, _) => (f % 32768) / 32768f);

	private static float RampAt(long frame) => (short)Math.Round((frame % 32768) / 32768f * 32767f) / 32768f;

	/// <summary>Renders <paramref name="frames"/> frames in device-sized buffers, filling the rings before each (as the null output does).</summary>
	private static float[] RenderStreamed(AudioMixer mixer, int frames)
	{
		mixer.Flush();
		var output = new float[frames * 2];
		for (var at = 0; at < frames; at += 512)
		{
			mixer.Streamer.Pump();
			mixer.Render(output.AsSpan(at * 2, Math.Min(512, frames - at) * 2));
		}

		return output;
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AThreeMinuteTrackPlaysFromASmallFixedFootprint()
	{
		// 3 minutes of 44.1 kHz stereo: 31.8 MB as 16-bit PCM on disk, 69 MB decoded to float at 48 kHz.
		WriteWav("music.wav", 180, 44100, 2, (f, c) => 0.5f * MathF.Sin(2 * MathF.PI * (c == 0 ? 440 : 660) * f / 44100f));
		var sound = Assert.IsType<StreamedSound>(Loader().Load("music.wav"));
		Assert.Equal(180f, sound.Duration, 2);
		Assert.Equal(2, sound.Channels);
		Assert.Equal(44100, sound.SampleRate);

		var output = new NullAudioOutput(512);
		using var manager = new AudioManager(new AudioMixer(new AudioConfig { OutputRate = Rate }), output);
		manager.Start();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		var before = GC.GetTotalMemory(forceFullCollection: true);

		var voice = manager.Play(sound, bus: AudioBus.Music);
		var time = TimeSpan.Zero;
		long allocated = 0;
		for (var frame = 0; frame < 600; frame++)
		{
			// 10 seconds at 60 frames per second; after the first second, nothing is allocated.
			if (frame == 60) allocated = GC.GetAllocatedBytesForCurrentThread();
			time += TimeSpan.FromSeconds(1.0 / 60);
			manager.Update(time);
		}

		allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
		var after = GC.GetTotalMemory(forceFullCollection: true);
		Assert.True(after - before < 2 * 1024 * 1024, $"{(after - before) / 1024} KB held while playing");
		Assert.True(allocated < 16 * 1024, $"{allocated} bytes allocated in 9 seconds of playback");

		// It is the track: 440 Hz left, 660 Hz right, without underruns.
		output.CaptureEnabled = true;
		manager.Update(time + TimeSpan.FromSeconds(0.5));
		var captured = output.Captured;
		Assert.Equal(440, Frequency(captured, 2, 0, Rate), 0);
		Assert.Equal(660, Frequency(captured, 2, 1, Rate), 0);
		Assert.Equal(0, manager.Mixer.StreamUnderruns);
		Assert.True(manager.IsPlaying(voice));
		Assert.Equal(1, manager.Mixer.Streamer.Count);

		// Stopping it closes its decoder.
		manager.Stop(voice);
		manager.Update(time + TimeSpan.FromSeconds(0.6));
		Assert.Equal(0, manager.Mixer.Streamer.Count);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AStreamedLoopGoesOnWithoutAGap()
	{
		WriteRamp("loop.wav", 1);
		var sound = Assert.IsType<StreamedSound>(Loader(streamLongerThan: 0.5f).Load("loop.wav"));
		var mixer = Mixer();
		using var streamer = mixer.Streamer;
		mixer.Play(sound, loop: true);
		var rendered = RenderStreamed(mixer, Rate * 5 / 2);

		// Two and a half times through: frame f of the output is frame f mod 48000 of the file, on both channels.
		for (var f = 0; f < Rate * 5 / 2; f += 97)
		{
			Assert.Equal(RampAt(f % Rate), rendered[f * 2], 6);
			Assert.Equal(RampAt(f % Rate), rendered[f * 2 + 1], 6);
		}

		Assert.Equal(0, mixer.StreamUnderruns);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SeekingAStreamedVoiceMovesItAndItEndsAtTheEnd()
	{
		WriteRamp("track.wav", 3);
		var sound = Assert.IsType<StreamedSound>(Loader(streamLongerThan: 1f).Load("track.wav"));
		var mixer = Mixer();
		using var streamer = mixer.Streamer;
		var voice = mixer.Play(sound);
		var first = RenderStreamed(mixer, 4800);
		Assert.Equal(RampAt(4799), first[4799 * 2], 6);

		mixer.Seek(voice, 2.5f);
		var after = RenderStreamed(mixer, 4800);
		Assert.Equal(RampAt(120000), after[0], 6);
		Assert.Equal(RampAt(120000 + 4799), after[4799 * 2], 6);

		// Half a second is left: it ends, and its decoder is released.
		var rest = RenderStreamed(mixer, Rate);
		Assert.Equal(0f, rest[^2]);
		mixer.Flush();
		Assert.False(mixer.IsPlaying(voice));
		mixer.Streamer.Pump();
		Assert.Equal(0, mixer.Streamer.Count);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheBackgroundThreadKeepsAStreamAheadOfARealTimeMixer()
	{
		// As with a device: the streamer's thread fills the ring while buffers are mixed at (faster than) real time.
		WriteRamp("track.wav", 3);
		var sound = Assert.IsType<StreamedSound>(Loader(streamLongerThan: 1f).Load("track.wav"));
		var mixer = Mixer();
		using var streamer = mixer.Streamer;
		streamer.Start();
		Assert.True(streamer.IsThreaded);
		mixer.Play(sound);
		mixer.Flush();

		var buffer = new float[512 * 2];
		for (var at = 0; at < Rate * 2; at += 512)
		{
			mixer.Render(buffer);
			Assert.Equal(RampAt(at + 511), buffer[511 * 2], 6);
			Thread.Sleep(5);   // about half of the 10.7 ms a buffer lasts
		}

		Assert.Equal(0, mixer.StreamUnderruns);
		streamer.Stop();
		Assert.False(streamer.IsThreaded);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SeekingAVoiceInMemoryMovesIt()
	{
		var mixer = Mixer();
		var voice = mixer.Play(Ramp(Rate, 1_000_000f));
		Render(mixer, 100);
		mixer.Seek(voice, 0.5f);
		var after = Render(mixer, 10);
		Assert.Equal(24000 / 1_000_000f, after[0], 6);
	}

	[Theory, Trait(CATEGORY, INTEGRATION)]
	[InlineData("tone440_stereo_44k.ogg", 1000)]
	[InlineData("tone440_mono_22k.mp3", 2500)]
	public void OggAndMp3Stream(string file, int skipFrames)
	{
		// 0.25 s tones at 440 Hz (see DecoderTests), played once.
		File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", file), Path.Combine(_root, file));
		var sound = Assert.IsType<StreamedSound>(Loader(streamLongerThan: 0.01f).Load(file));
		Assert.InRange(sound.Duration, 0.24f, 0.4f);

		var mixer = Mixer();
		using var streamer = mixer.Streamer;
		var voice = mixer.Play(sound);
		var rendered = RenderStreamed(mixer, (int)(sound.Duration * Rate));
		Assert.Equal(440, Frequency(rendered, 2, 0, Rate, skipFrames), 1.0);
		Assert.Equal(0, mixer.StreamUnderruns);

		RenderStreamed(mixer, Rate / 10);
		mixer.Flush();
		Assert.False(mixer.IsPlaying(voice));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ShortSoundsAndADisabledThresholdDecodeWhole()
	{
		WriteRamp("short.wav", 2);
		Assert.IsType<SoundEffect>(Loader().Load("short.wav"));
		Assert.IsType<SoundEffect>(Loader(streamLongerThan: 0).Load("short.wav"));
		Assert.IsType<StreamedSound>(Loader(streamLongerThan: 1).Load("short.wav"));
	}

	private sealed class FolderStorage(string root) : IPersistentStorage
	{
		private readonly Provider _provider = new(root);

		public IPersistentStorageProvider Game => _provider;
		public IPersistentStorageProvider Assets => _provider;
		public IPersistentStorageProvider User => _provider;
		public IPersistentStorageProvider Saves => _provider;

		private sealed class Provider(string root) : IPersistentStorageProvider
		{
			public string GetPath(params string[] path) => Path.Combine(root, Path.Combine(path));
			public Stream Read(params string[] path) => File.OpenRead(GetPath(path));
			public void CreateDirectory(params string[] path) => Directory.CreateDirectory(GetPath(path));
			public void Write(string text, params string[] path) => File.WriteAllText(GetPath(path), text);
			public void Write(byte[] bytes, params string[] path) => File.WriteAllBytes(GetPath(path), bytes);
			public BinaryWriter OpenWrite(params string[] path) => new(File.Create(GetPath(path)));
			public void Append(string text, params string[] path) => File.AppendAllText(GetPath(path), text);
			public IEnumerable<string> List(params string[] path) => Directory.EnumerateFileSystemEntries(GetPath(path));
			public void DeleteFile(params string[] path) => File.Delete(GetPath(path));
			public void DeleteDirectory(params string[] path) => Directory.Delete(GetPath(path), recursive: true);
		}
	}
}
