using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Ion.Extensions.Assets;
using Ion.Testing;

using static Ion.Extensions.Audio.Tests.AudioTestUtils;

namespace Ion.Extensions.Audio.Tests;

public class CommandQueueTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void Queue_IsFifoAcrossWrapAroundAndBounded()
	{
		var queue = new SpscQueue<int>(4);
		var next = 0;
		var expected = 0;

		for (var round = 0; round < 10; round++)
		{
			while (queue.TryEnqueue(next)) next++;
			Assert.Equal(4, queue.Count);

			Assert.True(queue.TryDequeue(out var a));
			Assert.True(queue.TryDequeue(out var b));
			Assert.Equal(expected++, a);
			Assert.Equal(expected++, b);
		}

		while (queue.TryDequeue(out var item)) Assert.Equal(expected++, item);
		Assert.Equal(next, expected);
		Assert.False(queue.TryDequeue(out _));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Queue_KeepsOrderBetweenTwoThreads()
	{
		const int count = 1_000_000;
		var queue = new SpscQueue<long>(256);
		var received = 0L;
		var outOfOrder = 0;

		var consumer = new Thread(() =>
		{
			var expected = 0L;
			while (expected < count)
			{
				if (queue.TryDequeue(out var item))
				{
					if (item != expected) outOfOrder++;
					expected++;
				}
				else
				{
					Thread.SpinWait(8);
				}
			}
			received = expected;
		});
		consumer.Start();

		for (long i = 0; i < count;)
		{
			if (queue.TryEnqueue(i)) i++;
			else Thread.SpinWait(8);
		}

		Assert.True(consumer.Join(TimeSpan.FromSeconds(30)));
		Assert.Equal(count, received);
		Assert.Equal(0, outOfOrder);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void Mixer_PlaysWhileTheGameThreadIssuesCommandsConcurrently()
	{
		// An audio thread renders continuously while the game thread plays, changes and stops voices.
		var mixer = Mixer(maxVoices: 16);
		var sound = Constant(0.01f, 480);
		using var stop = new CancellationTokenSource();
		var renders = 0;

		var audio = new Thread(() =>
		{
			var buffer = new float[256 * 2];
			while (!stop.IsCancellationRequested)
			{
				mixer.Render(buffer);
				renders++;
			}
		});
		audio.Start();

		for (var frame = 0; frame < 2000; frame++)
		{
			var voice = mixer.Play(sound, loop: frame % 7 == 0);
			mixer.SetVolume(voice, 0.5f);
			if (frame % 3 == 0) mixer.Stop(voice, fadeOut: 0.001f);
			mixer.Flush();
		}

		// Keep flushing while the audio thread drains the queue and reports the stopped voices.
		mixer.StopAll();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while ((mixer.PendingCommands > 0 || mixer.ActiveVoices > 0) && DateTime.UtcNow < deadline)
		{
			mixer.StopAll();
			mixer.Flush();
			Thread.Sleep(1);
		}

		stop.Cancel();
		audio.Join();

		Assert.True(renders > 0);
		Assert.Equal(0, mixer.ActiveVoices);
		Assert.Equal(0, mixer.PendingCommands);
	}
}

public class AllocationTests
{
	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(AudioInterpolation.Linear)]
	[InlineData(AudioInterpolation.Cubic)]
	public void Render_DoesNotAllocateAfterWarmUp(AudioInterpolation interpolation)
	{
		var mixer = Mixer(maxVoices: 64, interpolation: interpolation);
		var mono = new SoundEffect("mono", Sine(440, Rate, 4800), 1, Rate);
		var stereo = new SoundEffect("stereo", Sine(660, Rate, 2400, channels: 2), 2, Rate);
		var buffer = new float[512 * 2];

		// Warm up: every code path once (play, loop, pitch, pan, fades, stop, finish reports, bus changes).
		var handles = new List<VoiceHandle>();
		for (var i = 0; i < 64; i++)
		{
			handles.Add(mixer.Play(i % 2 == 0 ? mono : stereo, volume: 0.1f, pitchShift: (i % 5 - 2) / 4f, pan: (i % 3 - 1) / 2f, loop: i % 4 == 0, fadeIn: i % 6 == 0 ? 0.01f : 0f));
		}
		mixer.Flush();
		for (var i = 0; i < 20; i++) mixer.Render(buffer);

		// Queue more commands for the measured renders.
		for (var i = 0; i < 64; i += 3)
		{
			mixer.SetVolume(handles[i], 0.05f);
			mixer.SetPitch(handles[i], 0.3f);
			mixer.SetPan(handles[i], -0.5f);
		}
		for (var i = 1; i < 64; i += 5) mixer.Stop(handles[i], fadeOut: 0.005f);
		mixer.SetBusVolume(AudioBus.Sfx, 0.7f);
		mixer.MasterVolume = 0.9f;
		mixer.Flush();

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 200; i++) mixer.Render(buffer);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Contains(buffer, s => s != 0f);
	}
}

public class NullOutputTests
{
	private static float[] Run(int seed)
	{
		var mixer = Mixer();
		var output = new NullAudioOutput(512) { CaptureEnabled = true };
		using var audio = new AudioManager(mixer, output);
		audio.Start();

		var tone = new SoundEffect("tone", Sine(440, Rate, 9600), 1, Rate);
		var chirp = new SoundEffect("chirp", Sine(1234.5, Rate, 4800, 0.3f, channels: 2), 2, Rate);
		var random = new Random(seed);
		var elapsed = TimeSpan.Zero;
		var frame = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 59) / 60);

		for (var i = 0; i < 120; i++)
		{
			if (i % 10 == 0) audio.Play(tone, volume: random.NextSingle(), pitchShift: random.NextSingle() - 0.5f, pan: random.NextSingle() * 2 - 1);
			if (i % 25 == 3) audio.Play(chirp, loop: true, fadeIn: 0.05f, bus: AudioBus.Music);
			if (i == 90) audio.StopAll(fadeOut: 0.1f);
			if (i == 60) audio.SetBusVolume(AudioBus.Music, 0.5f);

			elapsed += frame;
			audio.Update(elapsed);
		}

		Assert.Equal((long)((Int128)elapsed.Ticks * Rate / TimeSpan.TicksPerSecond), output.FramesRendered);
		return output.Captured;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SameCommands_GiveBitIdenticalBuffers()
	{
		var a = Run(seed: 7);
		var b = Run(seed: 7);
		var c = Run(seed: 8);

		Assert.Equal(2 * 2 * Rate, a.Length); // 2 s of stereo
		Assert.True(a.AsSpan().SequenceEqual(b));
		Assert.False(a.AsSpan().SequenceEqual(c));
		Assert.Contains(a, s => s != 0f);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Advance_RendersWhatTheClockOwesInBuffersOfAtMostBufferSize()
	{
		var output = new NullAudioOutput(100);
		var sizes = new List<int>();
		output.Start(new AudioFormat(1000, 2), buffer => sizes.Add(buffer.Length / 2));

		Assert.Equal(250, output.Advance(TimeSpan.FromMilliseconds(250)));
		Assert.Equal([100, 100, 50], sizes);
		Assert.Equal(0, output.Advance(TimeSpan.FromMilliseconds(250)));
		Assert.Equal(1, output.Advance(TimeSpan.FromMilliseconds(251)));

		// A long gap renders at most one second.
		Assert.Equal(1000, output.Advance(TimeSpan.FromSeconds(10)));
		Assert.Equal(1251, output.FramesRendered);
	}
}

public class ManagerTests
{
	private sealed class ListLogger : ILogger<AudioManager>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, formatter(state, exception)));
	}

	private sealed class FailingOutput : IAudioOutput
	{
		public string Name => "Failing";
		public int BufferSize => 256;
		public bool IsRunning => false;
		public bool Disposed { get; private set; }
		public void Start(AudioFormat format, AudioRenderCallback callback) => throw new DllNotFoundException("libopenal not found");
		public void Stop() { }
		public void Dispose() => Disposed = true;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Start_FallsBackToTheNullOutputWithAWarning()
	{
		var logger = new ListLogger();
		var failing = new FailingOutput();
		using var audio = new AudioManager(Mixer(), failing, logger);

		audio.Start();

		Assert.True(audio.IsFallback);
		var fallback = Assert.IsType<NullAudioOutput>(audio.Output);
		Assert.True(fallback.IsRunning);
		Assert.Equal(256, fallback.BufferSize);
		Assert.True(failing.Disposed);
		var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
		Assert.Contains("libopenal not found", warning.Message);

		// The game keeps running: plays are mixed on the null output.
		var voice = audio.Play(Constant(0.5f, 100));
		audio.Update(TimeSpan.FromMilliseconds(10));
		Assert.Equal(480, fallback.FramesRendered);
		audio.Update(TimeSpan.FromMilliseconds(20));
		Assert.False(audio.IsPlaying(voice));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void OpenAl_WithAMissingDeviceFallsBackInsteadOfThrowing()
	{
		var logger = new ListLogger();
		using var audio = new AudioManager(Mixer(), new OpenAlAudioOutput(deviceName: "Ion test: no such device"), logger);

		audio.Start();

		Assert.True(audio.IsFallback);
		Assert.IsType<NullAudioOutput>(audio.Output);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("OpenAL"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void OpenAl_DefaultDeviceStartsOrFallsBack()
	{
		// On a machine with a sound card this plays silence for a moment; without one (CI) it falls back.
		var names = OpenAlAudioOutput.GetDeviceNames();
		Assert.NotNull(names);

		using var audio = new AudioManager(Mixer(), new OpenAlAudioOutput(), NullLogger<AudioManager>.Instance);
		audio.Start();
		audio.Play(Constant(0f, 4800));
		audio.Update(TimeSpan.FromMilliseconds(16));

		Assert.True(audio.IsFallback || audio.Output is OpenAlAudioOutput { IsRunning: true });
		audio.Stop();
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AddAudio_BindsConfigAndHonorsTheNullBackend()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Audio:OutputRate"] = "44100",
			["Ion:Audio:MaxVoices"] = "8",
			["Ion:Audio:BufferFrames"] = "256",
			["Ion:Audio:Backend"] = "Null",
			["Ion:Audio:Interpolation"] = "Cubic",
		}).Build();

		using var services = new ServiceCollection().AddLogging().AddSingleton<IPersistentStorage>(new FakeStorage()).AddAudio(config).BuildServiceProvider();

		var mixer = services.GetRequiredService<AudioMixer>();
		Assert.Equal(44100, mixer.SampleRate);
		Assert.Equal(8, mixer.MaxVoices);
		Assert.Equal(AudioInterpolation.Cubic, mixer.Interpolation);
		var output = Assert.IsType<NullAudioOutput>(services.GetRequiredService<IAudioOutput>());
		Assert.Equal(256, output.BufferSize);
		Assert.Same(services.GetRequiredService<AudioManager>(), services.GetRequiredService<IAudioManager>());
		Assert.Contains(services.GetServices<IAssetLoader>(), l => l is SoundEffectLoader);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AddNullAudio_ResolvesOneNullManagerForEveryServiceType()
	{
		using var services = new ServiceCollection().AddLogging().AddSingleton<IPersistentStorage>(new FakeStorage()).AddNullAudio().BuildServiceProvider();

		var manager = services.GetRequiredService<NullAudioManager>();
		Assert.Same(manager, services.GetRequiredService<AudioManager>());
		Assert.Same(manager, services.GetRequiredService<IAudioManager>());
		Assert.Same(manager.NullOutput, services.GetRequiredService<NullAudioOutput>());
		Assert.Same(manager.Mixer, services.GetRequiredService<AudioMixer>());
		Assert.Contains(services.GetServices<IAssetLoader>(), l => l is NullSoundEffectLoader);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullAudioManager_RecordsPlaysAndMixesThem()
	{
		var audio = new NullAudioManager { MasterVolume = 0.5f };
		audio.NullOutput.CaptureEnabled = true;
		audio.Start();
		var sound = Constant(0.5f, 48_000);
		var headerOnly = new NullSoundEffect("x.mp3", 0f, 0, 0, 0);

		var voice = audio.Play(sound, volume: 0.5f, pan: -1f, loop: true, bus: AudioBus.Music);
		var none = audio.Play(headerOnly);
		audio.Update(TimeSpan.FromMilliseconds(1));

		Assert.Equal([new SoundPlay(sound, 0.5f, 0f, 0.5f) { Pan = -1f, Loop = true, Bus = AudioBus.Music, Voice = voice }, new SoundPlay(headerOnly, 1f, 0f, 0.5f)], audio.Plays);
		Assert.True(voice.IsValid);
		Assert.False(none.IsValid);

		var mixed = audio.NullOutput.Captured;
		Assert.Equal(96, mixed.Length);
		Assert.Equal(0.125f, mixed[0]);
		Assert.Equal(0f, mixed[1]);
	}

	private sealed class FakeStorage : IPersistentStorage
	{
		public IPersistentStorageProvider Game => throw new NotSupportedException();
		public IPersistentStorageProvider Assets => throw new NotSupportedException();
		public IPersistentStorageProvider User => throw new NotSupportedException();
		public IPersistentStorageProvider Saves => throw new NotSupportedException();
	}
}

/// <summary>
/// Plays bonk.wav at half volume on the frame <see cref="PlayOnFrame"/> is set to.
/// </summary>
public sealed class BonkSystem(Ion.Extensions.Assets.IAssetManager assets, IAudioManager audio)
{
	public ISoundEffect Sound { get; private set; } = default!;
	public long PlayOnFrame { get; set; } = -1;
	public VoiceHandle Voice { get; private set; }

	[Init]
	public void Init() => Sound = assets.Load<ISoundEffect>("bonk.wav");

	[Update]
	public void Update(GameTime dt)
	{
		if (dt.Frame == PlayOnFrame) Voice = audio.Play(Sound, volume: 0.5f);
	}
}

public class IonTestHostAudioTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void PlayedSound_IsInTheMixedOutput()
	{
		using var host = new IonTestHost().WithSystem<BonkSystem>();
		var system = host.Get<BonkSystem>();
		var output = host.Audio.NullOutput;
		output.CaptureEnabled = true;

		host.Step(3);
		var before = output.FramesRendered;
		Assert.All(output.Captured, s => Assert.Equal(0f, s));

		system.PlayOnFrame = host.Frame;
		host.Step(1);

		// The play is recorded, and flushed and mixed in the same frame: from the first frame rendered after it, the
		// output is the decoded sound at half volume.
		var play = Assert.Single(host.Audio.Plays);
		Assert.Equal(0.5f, play.Volume);
		Assert.True(host.Audio.IsPlaying(system.Voice));

		host.Step(20);
		var decoded = Assert.IsType<NullSoundEffect>(system.Sound).Decoded!;
		var mixed = output.Captured;
		var frames = Math.Min(decoded.Frames, (int)(output.FramesRendered - before));
		Assert.True(frames > 1000);
		for (var i = 0; i < frames * 2; i++)
		{
			Assert.Equal(decoded.Samples[i] * 0.5f, mixed[(int)before * 2 + i]);
		}

		// bonk.wav is about 0.16 s long: finished well within 20 frames.
		Assert.False(host.Audio.IsPlaying(system.Voice));
		Assert.Equal(0, host.Audio.Mixer.ActiveVoices);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void MixedOutput_FollowsTheGameClock()
	{
		using var host = new IonTestHost();
		host.Step(60);

		// 60 frames of (1/60 s rounded up to a tick) at 48 kHz.
		var elapsed = host.FrameTime * 60;
		Assert.Equal((long)((Int128)elapsed.Ticks * 48000 / TimeSpan.TicksPerSecond), host.Audio.NullOutput.FramesRendered);
	}
}
