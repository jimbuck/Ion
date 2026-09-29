---
title: Audio
description: Play sound effects and music through Ion's engine mixer, with buses, fades, pitch and pan, on OpenAL or the headless null output.
sidebar:
  order: 1
---

Ion's audio module (`Ion.Extensions.Audio`) is an engine-owned mixer that runs on Windows, macOS, Linux (x64 and arm64,
including handhelds such as the R36S), iOS and Android. You load sounds as assets, play them through `IAudioManager`,
and the mixer feeds a platform output: OpenAL when a device is available, or a silent, deterministic null output for
servers, CI and tests.

```csharp title="SoundSystem.cs"
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Audio;

public sealed class SoundSystem(IAssetManager assets, IAudioManager audio, IInputState input)
{
	private ISoundEffect _bonk = default!;

	[Init]
	public void Load() => _bonk = assets.Load<ISoundEffect>("bonk.wav");

	[Update]
	public void Update(GameTime dt)
	{
		if (input.Pressed(Key.Space)) audio.Play(_bonk, volume: 0.8f);
	}
}
```

## Setup

Audio is part of the engine core. `builder.AddIon()` registers it (OpenAL when windowed, the null output when
headless) and `game.UseIon()` adds its system, so most games get audio without asking for it. Call
`builder.AddAudio(...)` when you want to change `AudioConfig` in code; it also registers the engine core, so you can
list it in any order with the other modules:

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Audio;

var builder = IonApplication.CreateBuilder(args);
builder.AddAudio(audio =>
	{
		audio.MaxVoices = 32;
		audio.Interpolation = AudioInterpolation.Cubic;
	})
	.AddSystem<SoundSystem>();

using var game = builder.Build();
game.UseIon().UseSystem<SoundSystem>();
game.Run();
```

`AddAudio` applies your delegate after the `Ion:Audio` configuration section is bound, so code wins over
`appsettings.json` and the command line. Calling it more than once (directly or through another module) only applies
the extra options.

:::note[Without the Ion meta package]
The service collection forms are in `Ion.Extensions.Audio`: `services.AddAudio(configuration, configure)` with
`app.UseAudio()` for the device output, and `services.AddNullAudio(configuration, configure)` with `app.UseNullAudio()`
for the headless one. `AddIon` picks between them for you based on `Ion:Headless`.
:::

### What runs when

The module adds one system, `AudioSystem`, at order `StageOrder.Audio` (-880) in the engine setup band:

| Stage | Step | What happens |
|---|---|---|
| Init | start | Opens the output. If OpenAL or the device is missing, logs a warning and switches to the null output. |
| Last | flush | Hands the frame's queued commands to the audio thread, collects voices that ended, and (null output) renders the audio due by the game clock. |
| Destroy | stop | Stops the output (at order 880, in the teardown band). |

Every `IAudioManager` call only queues a command, so playing a sound never blocks a frame on the audio device. Because
the flush runs in `Last` before your own `Last` steps, commands issued in Init, First, FixedUpdate, Update and Render
reach the audio thread in the same frame; commands issued from a `Last` step of your own reach it one frame later. See
[Stages](/Ion/concepts/stages/) and [Stage order](/Ion/reference/stage-order/).

## Loading sounds

Load sounds through the asset manager as `ISoundEffect`, usually in an `Init` step. Paths are relative to the game's
assets folder and are cached, so loading the same path twice returns the same instance.

```csharp
var bonk = assets.Load<ISoundEffect>("bonk.wav");
var ping = assets.Load<ISoundEffect>("ping.mp3");
var theme = assets.Load<ISoundEffect>("music/theme.ogg");

Console.WriteLine($"{theme.Duration:0.0} s, {theme.Channels} channels, {theme.SampleRate} Hz");
```

The loader (`SoundEffectLoader`) decodes the whole file at load time, in managed code that is NativeAOT-clean, and
resamples it once to the mixer's output rate with a windowed-sinc filter:

| Format | Decoder | Notes |
|---|---|---|
| WAV (RIFF/WAVE) | built in (`WavDecoder`) | PCM 8, 16, 24 and 32-bit, IEEE float 32 and 64-bit, `WAVE_FORMAT_EXTENSIBLE`, any rate |
| OGG Vorbis | NVorbis | `.ogg`, `.oga` |
| MP3 | NLayer | also MPEG layers 1 and 2 (`.mp2`, `.mpga`) |

The format is detected from the first bytes of the file, then from the extension. Files with more than two channels
keep their first two. `ISoundEffect` exposes `Duration` (seconds), `Channels` and `SampleRate` (of the file, before
resampling).

:::caution[File names are case-sensitive]
On Linux, macOS, Android and the R36S, `Bonk.wav` and `bonk.wav` are different files. A missing file throws
`FileNotFoundException` with a hint about casing. Keep asset names lowercase and match them exactly.
:::

Sounds loaded again after the file changes (asset hot reload) are replaced with a new instance in the cache. See
[Assets](/Ion/rendering/assets/).

## Playing sounds

`IAudioManager.Play` starts a new voice and returns a `VoiceHandle`:

```csharp
VoiceHandle Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f, float pan = 0f,
	bool loop = false, AudioBus bus = AudioBus.Sfx, float fadeIn = 0f);
```

| Parameter | Default | Meaning |
|---|---|---|
| `volume` | `1` | Linear gain of this voice: 1 unchanged, 0 silent, above 1 amplifies (and may clip). |
| `pitchShift` | `0` | Pitch in octaves, clamped to [-1, 1]. -1 plays one octave down at half speed, 1 one octave up at double speed. The sound is resampled, so its duration changes too. |
| `pan` | `0` | Stereo balance in [-1, 1]: -1 left only, 0 centered, 1 right only. |
| `loop` | `false` | Restart from the beginning when it ends, until stopped. |
| `bus` | `AudioBus.Sfx` | The bus the voice is mixed into. |
| `fadeIn` | `0` | Seconds to fade in from silence. |

The Breakout samples vary the pitch of each hit slightly so repeated sounds do not become monotonous:

```csharp
private readonly Random _rand = new(6014);

// A small random pitch variation, in octaves.
audio.Play(_bonkSound, pitchShift: (_rand.NextSingle() - 0.5f) / 16f);
audio.Play(_pingSound, pitchShift: (_rand.NextSingle() - 0.5f) / 4f);
```

### Controlling a voice

Keep the handle to change or stop the voice later. Handles are safe to keep after the voice ends: each pool slot has a
generation that changes when it is reused, so calls with a stale handle do nothing.

```csharp
var engine = audio.Play(_engineLoop, volume: 0.5f, loop: true);

// Later, every frame:
audio.SetPitch(engine, Math.Clamp(speed / maxSpeed, 0f, 1f) * 0.5f);
audio.SetPan(engine, (carX / screenWidth) * 2f - 1f);
audio.SetVolume(engine, throttle);

// When the car stops:
audio.Stop(engine, fadeOut: 0.25f);
```

| Member | What it does |
|---|---|
| `Stop(voice, fadeOut = 0)` | Stops the voice now, or after fading out over `fadeOut` seconds. |
| `StopAll(fadeOut = 0)` | Stops every voice. |
| `SetVolume(voice, volume)`, `SetPitch(voice, pitchShift)`, `SetPan(voice, pan)` | Change a playing voice. |
| `IsPlaying(voice)` | True from `Play` until the voice is stopped without a fade, or until the audio thread reports that it ended (reports are collected once per frame, in `Last`). |
| `VoiceHandle.IsValid` | False for `VoiceHandle.None` (`default`) and for a play that was refused. |

`Play` returns an invalid handle when the sound has no decoded audio, or when the voice pool is full and configured to
refuse new voices.

## Music

Music is a looping voice on the `Music` bus, usually with a fade:

```csharp
public sealed class MusicSystem(IAssetManager assets, IAudioManager audio)
{
	private ISoundEffect _theme = default!;
	private VoiceHandle _music;

	[Init]
	public void Start()
	{
		_theme = assets.Load<ISoundEffect>("music/theme.ogg");
		_music = audio.Play(_theme, bus: AudioBus.Music, loop: true, fadeIn: 2f);
	}

	public void ChangeTrack(ISoundEffect next)
	{
		audio.Stop(_music, fadeOut: 1f);                                           // cross-fade: the old
		_music = audio.Play(next, bus: AudioBus.Music, loop: true, fadeIn: 1f);  // and the new overlap
	}
}
```

:::caution[Streaming music is not implemented yet]
Every sound, music included, is decoded whole into float32 stereo at the output rate. That is about 384 KB per second
at 48 kHz, so a three-minute track takes around 69 MB of memory once loaded, and the decode happens on the thread that
calls `Load`. Keep tracks short or loop them, load them during a loading screen, and consider a lower
`Ion:Audio:OutputRate` on memory-constrained devices. Streaming decode is planned.
:::

## Buses and volume

Every voice is mixed into one of three buses, and every bus feeds the master:

| Bus | Use |
|---|---|
| `AudioBus.Sfx` | Sound effects (the default for `Play`). |
| `AudioBus.Music` | Music. |
| `AudioBus.Master` | Voices on it get only the master volume. |

Gains multiply: a voice is heard at its volume, times its bus volume, times `MasterVolume`. `MasterVolume` is the same
value as the master bus volume and defaults to 1.

```csharp
audio.MasterVolume = 0.9f;
audio.SetBusVolume(AudioBus.Music, 0.5f);
audio.SetBusVolume(AudioBus.Sfx, 1f);

var music = audio.GetBusVolume(AudioBus.Music);
```

An options screen is the typical place for these. With the [UI module](/Ion/interaction/ui/overview/), a slider can
write straight to the bus:

```csharp
private float _musicVolume = 1f;

[Update]
public void Options(GameTime dt)
{
	using (ui.Panel("audio"))
	{
		if (ui.Slider("Music", ref _musicVolume, 0f, 1f, step: 0.05f)) audio.SetBusVolume(AudioBus.Music, _musicVolume);
	}
}
```

The mixed output is clamped to [-1, 1], so loud combinations clip rather than wrap. There is no compressor or limiter.

## Spatial audio

There is no 3D or positional audio: no listener, no distance attenuation and no Doppler. For 2D games, derive `pan`
and `volume` from the source's position yourself:

```csharp
// Pan by horizontal position on screen, quieter with distance from the player.
var pan = Math.Clamp((source.X - window.Width / 2f) / (window.Width / 2f), -1f, 1f);
var distance = Vector2.Distance(source, player);
var volume = Math.Clamp(1f - distance / 1200f, 0f, 1f);
audio.Play(_explosion, volume: volume, pan: pan);
```

## Configuration

`AudioConfig` is bound from the `Ion:Audio` section:

```json title="appsettings.json"
{
  "Ion": {
    "Audio": {
      "OutputRate": 48000,
      "MaxVoices": 64,
      "BufferFrames": 512,
      "BufferCount": 4,
      "Backend": "Auto",
      "Device": null,
      "Interpolation": "Linear",
      "VoiceStealing": "Oldest",
      "CommandCapacity": 1024
    }
  }
}
```

| Key | Default | Range | Meaning |
|---|---|---|---|
| `OutputRate` | `48000` | 8000 to 384000 | The mixer's rate in frames per second. Every sound is resampled to it at load. |
| `MaxVoices` | `64` | 1 to 4096 | The size of the fixed voice pool. |
| `BufferFrames` | `512` | 64 to 65536 | Frames mixed per buffer. |
| `BufferCount` | `4` | 2 to 64 | Buffers queued on the device. |
| `Backend` | `Auto` | `Auto`, `OpenAL`, `Null` | The output. `Auto` and `OpenAL` both fall back to `Null` when OpenAL cannot start. |
| `Device` | null | | The OpenAL device name; null or empty opens the default device. |
| `Interpolation` | `Linear` | `Linear`, `Cubic` | Resampling for pitch. `Cubic` (Catmull-Rom) is smoother at high shifts and costs about twice as much. |
| `VoiceStealing` | `Oldest` | `Oldest`, `Refuse` | When the pool is full: replace the voice that started first (preferring one-shots over loops), or refuse (`Play` returns an invalid handle). |
| `CommandCapacity` | `1024` | 16 to 1048576 | The game-to-audio command queue, rounded up to a power of two. Commands that do not fit wait on the game thread until the next frame. |

Output latency is about `BufferFrames * BufferCount / OutputRate`: 43 ms with the defaults. Lower `BufferFrames` for
snappier sound at the risk of underruns on slow devices. From the command line, use the long form, for example
`--Ion:Audio:Backend=Null`. See [Configuration](/Ion/reference/configuration/) for every section.

## Outputs and threads

The mixer mixes float32 interleaved stereo. The output pulls it on a thread of its own:

- **OpenAL** (`OpenAlAudioOutput`): one streaming source with a ring of `BufferCount` buffers refilled on a dedicated
  thread, float32 when the device supports `AL_EXT_FLOAT32` and 16-bit otherwise, with underrun recovery and
  disconnect detection. It loads OpenAL Soft (shipped for Windows, macOS and Linux through `Silk.NET.OpenAL.Soft.Native`)
  or the system OpenAL (the iOS and macOS framework, or the `libopenal.so` an Android app or the R36S build bundles).
  `OpenAlAudioOutput.GetDeviceNames()` lists the devices you can put in `Ion:Audio:Device`.
- **Null** (`NullAudioOutput`): no device. It mixes on the game thread from the game clock, so the same commands and
  frame times give bit-identical buffers.

The audio thread applies queued commands, mixes, and reports finished voices on a return queue. It never blocks and
does not allocate after warm-up (a test checks the mix callback).

:::tip[Startup never fails because of audio]
If the OpenAL library or the device is missing, or the device disconnects while the game runs, `AudioManager` logs a
warning and switches to the null output: the game keeps running, silently. `AudioManager.IsFallback` tells you it
happened, and `AudioManager.Output.Name` is `Null` afterwards.
:::

## Headless audio and tests

When the game runs headless (`--headless`, or `Ion:Headless=true`), `AddIon` registers `AddNullAudio`: the real mixer
on a `NullAudioOutput` driven by the game clock, and a `NullAudioManager` that also records every `Play` call. You
write the same code against `IAudioManager`; tests assert on what was played, and can even check the mixed samples.

`IonTestHost.Audio` is that `NullAudioManager`:

```csharp title="SoundTests.cs"
using Ion;
using Ion.Extensions.Audio;
using Ion.Testing;
using Xunit;

public class SoundTests
{
	[Fact]
	public void SpacePlaysTheBonk()
	{
		using var host = new IonTestHost().WithSystem<SoundSystem>();
		host.Step();

		host.Input.Tap(Key.Space);
		host.Step();

		var play = Assert.Single(host.Audio.Plays);
		Assert.Equal("bonk.wav", play.Sound.Name);
		Assert.Equal(0.8f, play.Volume);
		Assert.Equal(AudioBus.Sfx, play.Bus);
		Assert.True(host.Audio.IsPlaying(play.Voice));
	}
}
```

The headless loader still needs the file: it reads it from the `Assets` folder next to the test binary, so copy the
sound into the test project's output (the engine's own tests link `bonk.wav` with
`<None Include="Assets\bonk.wav" CopyToOutputDirectory="PreserveNewest" />`). A missing file throws
`FileNotFoundException` at `Load`, headless or not.

Each `SoundPlay` has `Sound`, `Volume`, `PitchShift`, `MasterVolume` (at the time of the call), `EffectiveVolume`
(volume times master), `Pan`, `Loop`, `Bus` and `Voice`. `host.Audio.Clear()` forgets the recorded plays.

To assert on the audio itself, turn on capture before stepping:

```csharp
var output = host.Audio.NullOutput;
output.CaptureEnabled = true;
host.Step(20);
float[] samples = output.Captured;   // interleaved stereo at the output rate
```

The headless loader (`NullSoundEffectLoader`) reads the WAV header and decodes the samples too, so headless runs mix
real audio and a test can compare the output sample by sample. The engine's own test does this with `bonk.wav`. Capture
is off by default so long headless runs do not grow memory. See [Testing](/Ion/tooling/testing/).

## Known gaps

- No streaming decode: long music is decoded whole at load time (planned).
- No 3D or positional audio, effects (reverb, filters) or user-defined buses; only `Master`, `Sfx` and `Music`.
- Pitch changes the playback rate, so a shifted sound is also shorter or longer (there is no time stretching).
- Not yet verified on real R36S hardware.

## See also

- [Assets](/Ion/rendering/assets/): loading and hot reload.
- [Stages](/Ion/concepts/stages/): when the `Last` flush runs relative to your steps.
- [Testing](/Ion/tooling/testing/): `IonTestHost`, the deterministic clock and headless runs.
- [Breakout](/Ion/examples/breakout/) and [Breakout ECS](/Ion/examples/breakout-ecs/): sound effects with random pitch.
- [Configuration reference](/Ion/reference/configuration/).
- Source: [Ion.Extensions.Audio](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Audio/).
