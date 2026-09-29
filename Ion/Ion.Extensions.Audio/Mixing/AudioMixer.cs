using System.Runtime.CompilerServices;

namespace Ion.Extensions.Audio;

/// <summary>
/// The engine mixer: a fixed pool of voices mixed into interleaved float32 stereo at <see cref="SampleRate"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two threads use a mixer. The game thread calls the <see cref="IAudioManager"/> members, which only update game-side
/// bookkeeping and append commands to a pending list, and <see cref="Flush"/> once per frame, which moves the pending
/// commands into a lock-free single-producer single-consumer queue and collects the voices the audio thread reported
/// as finished. The audio thread calls <see cref="Render"/>, which applies the queued commands in order, mixes, and
/// reports finished voices on a second lock-free queue. <see cref="Render"/> never blocks and, after the first call,
/// never allocates.
/// </para>
/// <para>
/// Each voice is resampled from a 32.32 fixed-point position (linear or cubic interpolation, see
/// <see cref="AudioConfig.Interpolation"/>), so its pitch can change freely and the output is bit-identical for the
/// same commands and buffer sizes. Gains: voice volume times bus volume times master volume, then a linear balance pan
/// (a channel is attenuated only when panned away from it), then the fade. Gain changes are ramped linearly over one
/// buffer to avoid clicks; a new voice starts at its gain. The mix is clamped to [-1, 1].
/// </para>
/// </remarks>
public sealed class AudioMixer : IAudioManager
{
	/// <summary>The mixer always produces stereo.</summary>
	public const int Channels = 2;

	private const int FixedShift = 32;
	private const long FixedOne = 1L << FixedShift;
	private const float FixedToFloat = 1f / FixedOne;
	private const int BusCount = 3;

	// Game thread.
	private readonly Lock _lock = new();
	private readonly int[] _slotGeneration;
	private readonly bool[] _slotBusy;
	private readonly bool[] _slotLoop;
	private readonly long[] _slotStarted;
	private readonly float[] _busVolume = [1f, 1f, 1f];
	private readonly List<AudioCommand> _pending = [];
	private long _playSequence;
	private readonly int _maxPending;
	private long _stolenVoices;
	private long _refusedVoices;
	private long _droppedCommands;

	// Shared, lock-free.
	private readonly SpscQueue<AudioCommand> _commands;
	private readonly SpscQueue<FinishedVoice> _finished;

	// Audio thread.
	private readonly Voice[] _voices;
	private readonly float[] _audioBus = [1f, 1f, 1f];
	private long _framesRendered;

	/// <summary>
	/// Creates a mixer from <paramref name="config"/> (<see cref="AudioConfig.OutputRate"/>, <see cref="AudioConfig.MaxVoices"/>,
	/// <see cref="AudioConfig.Interpolation"/>, <see cref="AudioConfig.VoiceStealing"/> and <see cref="AudioConfig.CommandCapacity"/>).
	/// </summary>
	public AudioMixer(AudioConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);
		config.Validate();

		SampleRate = config.OutputRate;
		MaxVoices = config.MaxVoices;
		Interpolation = config.Interpolation;
		VoiceStealing = config.VoiceStealing;

		_slotGeneration = new int[MaxVoices];
		_slotBusy = new bool[MaxVoices];
		_slotLoop = new bool[MaxVoices];
		_slotStarted = new long[MaxVoices];
		_voices = new Voice[MaxVoices];

		_commands = new SpscQueue<AudioCommand>(config.CommandCapacity);
		_maxPending = _commands.Capacity * 16;
		// Every voice reports at most once per generation and retries when the queue is full, so MaxVoices slots suffice;
		// twice that leaves room for reports of stolen voices.
		_finished = new SpscQueue<FinishedVoice>(MaxVoices * 2);
	}

	/// <summary>Output frames per second.</summary>
	public int SampleRate { get; }

	/// <summary>The size of the voice pool.</summary>
	public int MaxVoices { get; }

	/// <summary>The interpolation used to resample voices.</summary>
	public AudioInterpolation Interpolation { get; }

	/// <summary>What <see cref="Play"/> does when every voice is busy.</summary>
	public VoiceStealing VoiceStealing { get; }

	/// <summary>The format <see cref="Render"/> produces.</summary>
	public AudioFormat Format => new(SampleRate, Channels);

	/// <summary>Frames rendered so far (read from any thread).</summary>
	public long FramesRendered => Interlocked.Read(ref _framesRendered);

	/// <summary>Voices replaced because the pool was full (<see cref="VoiceStealing.Oldest"/>).</summary>
	public long StolenVoices => Interlocked.Read(ref _stolenVoices);

	/// <summary>Plays refused because the pool was full (<see cref="VoiceStealing.Refuse"/>).</summary>
	public long RefusedVoices => Interlocked.Read(ref _refusedVoices);

	/// <summary>
	/// Commands dropped because far more were issued than flushed (more than 16 times <see cref="AudioConfig.CommandCapacity"/>
	/// waiting, which happens only when nothing calls <see cref="Flush"/>, for example without the audio system).
	/// </summary>
	public long DroppedCommands => Interlocked.Read(ref _droppedCommands);

	/// <summary>Voices playing as seen from the game thread (see <see cref="IsPlaying"/>).</summary>
	public int ActiveVoices
	{
		get
		{
			lock (_lock)
			{
				var count = 0;
				for (var i = 0; i < _slotBusy.Length; i++) if (_slotBusy[i]) count++;
				return count;
			}
		}
	}

	/// <summary>Commands waiting on the game thread for the next <see cref="Flush"/>.</summary>
	public int PendingCommands
	{
		get
		{
			lock (_lock) return _pending.Count;
		}
	}

	// ---------------------------------------------------------------------------------------------------------------
	// Game thread
	// ---------------------------------------------------------------------------------------------------------------

	/// <inheritdoc/>
	public float MasterVolume
	{
		get => GetBusVolume(AudioBus.Master);
		set => SetBusVolume(AudioBus.Master, value);
	}

	/// <summary>
	/// The decoded samples behind <paramref name="sound"/>: the <see cref="SoundEffect"/> itself, or the decoded data of a
	/// headless <see cref="NullSoundEffect"/>. Null when the sound has none.
	/// </summary>
	public static SoundEffect? GetSamples(ISoundEffect sound) => sound switch
	{
		SoundEffect pcm => pcm,
		NullSoundEffect headless => headless.Decoded,
		_ => null,
	};

	/// <summary>
	/// Maps a pitch shift in octaves (clamped to [-1, 1]) to a playback-rate factor in [0.5, 2].
	/// </summary>
	public static double ToPitchFactor(float pitchShift)
	{
		if (float.IsNaN(pitchShift)) return 1.0;
		return Math.Pow(2.0, Math.Clamp(pitchShift, -1f, 1f));
	}

	/// <inheritdoc/>
	public VoiceHandle Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f, float pan = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, float fadeIn = 0f)
	{
		ArgumentNullException.ThrowIfNull(soundEffect);
		var sound = GetSamples(soundEffect);
		if (sound is null || sound.Frames == 0) return VoiceHandle.None;

		lock (_lock)
		{
			var slot = _findFreeSlot();
			if (slot < 0)
			{
				if (VoiceStealing == VoiceStealing.Refuse)
				{
					_refusedVoices++;
					return VoiceHandle.None;
				}

				slot = _oldestSlot();
				_stolenVoices++;
			}

			var generation = _slotGeneration[slot] + 1;
			if (generation <= 0) generation = 1;
			_slotGeneration[slot] = generation;
			_slotBusy[slot] = true;
			_slotLoop[slot] = loop;
			_slotStarted[slot] = ++_playSequence;

			_enqueue(new AudioCommand
			{
				Kind = CommandKind.Play,
				Slot = slot,
				Generation = generation,
				Sound = sound,
				Value = _sanitizeGain(volume),
				Pan = _sanitizePan(pan),
				Step = _step(sound, pitchShift),
				Frames = _frames(fadeIn),
				Loop = loop,
				Bus = _sanitizeBus(bus),
			});

			return new VoiceHandle(slot, generation);
		}
	}

	/// <inheritdoc/>
	public void Stop(VoiceHandle voice, float fadeOut = 0f)
	{
		lock (_lock) _stop(voice, _frames(fadeOut));
	}

	private void _stop(VoiceHandle voice, int frames)
	{
		if (!_isCurrent(voice)) return;

		// Without a fade the voice is gone for the game at once; with one it plays until the audio thread reports it.
		if (frames == 0) _slotBusy[voice.Slot] = false;

		_enqueue(new AudioCommand { Kind = CommandKind.Stop, Slot = voice.Slot, Generation = voice.Generation, Frames = frames });
	}

	/// <inheritdoc/>
	public void StopAll(float fadeOut = 0f)
	{
		var frames = _frames(fadeOut);
		lock (_lock)
		{
			for (var slot = 0; slot < _slotBusy.Length; slot++)
			{
				if (_slotBusy[slot]) _stop(new VoiceHandle(slot, _slotGeneration[slot]), frames);
			}
		}
	}

	/// <inheritdoc/>
	public void SetVolume(VoiceHandle voice, float volume) => _enqueueVoice(voice, CommandKind.SetVolume, _sanitizeGain(volume));

	/// <inheritdoc/>
	public void SetPan(VoiceHandle voice, float pan) => _enqueueVoice(voice, CommandKind.SetPan, _sanitizePan(pan));

	/// <inheritdoc/>
	public void SetPitch(VoiceHandle voice, float pitchShift)
	{
		lock (_lock)
		{
			if (!_isCurrent(voice)) return;
			// The step is computed from the sound's rate on the audio thread's copy; send the factor in 32.32.
			_enqueue(new AudioCommand
			{
				Kind = CommandKind.SetPitch,
				Slot = voice.Slot,
				Generation = voice.Generation,
				Step = (long)Math.Round(ToPitchFactor(pitchShift) * FixedOne),
			});
		}
	}

	/// <inheritdoc/>
	public bool IsPlaying(VoiceHandle voice)
	{
		lock (_lock) return _isCurrent(voice);
	}

	/// <inheritdoc/>
	public float GetBusVolume(AudioBus bus)
	{
		lock (_lock) return _busVolume[(int)_sanitizeBus(bus)];
	}

	/// <inheritdoc/>
	public void SetBusVolume(AudioBus bus, float volume)
	{
		bus = _sanitizeBus(bus);
		volume = _sanitizeGain(volume);
		lock (_lock)
		{
			_busVolume[(int)bus] = volume;
			_enqueue(new AudioCommand { Kind = CommandKind.SetBus, Bus = bus, Value = volume });
		}
	}

	/// <summary>
	/// Hands the pending commands to the audio thread (as many as fit in the queue; the rest wait for the next call) and
	/// collects the voices it reported as finished. Called once per frame by the audio system in the Last stage.
	/// </summary>
	public void Flush()
	{
		lock (_lock)
		{
			while (_finished.TryDequeue(out var done))
			{
				if (_slotGeneration[done.Slot] == done.Generation) _slotBusy[done.Slot] = false;
			}

			var sent = 0;
			while (sent < _pending.Count && _commands.TryEnqueue(_pending[sent])) sent++;
			if (sent > 0) _pending.RemoveRange(0, sent);
		}
	}

	private void _enqueue(in AudioCommand command)
	{
		if (_pending.Count >= _maxPending)
		{
			_droppedCommands++;
			return;
		}

		_pending.Add(command);
	}

	private void _enqueueVoice(VoiceHandle voice, CommandKind kind, float value)
	{
		lock (_lock)
		{
			if (!_isCurrent(voice)) return;
			_enqueue(new AudioCommand { Kind = kind, Slot = voice.Slot, Generation = voice.Generation, Value = value });
		}
	}

	private bool _isCurrent(VoiceHandle voice) =>
		voice.IsValid && (uint)voice.Slot < (uint)_slotBusy.Length && _slotGeneration[voice.Slot] == voice.Generation && _slotBusy[voice.Slot];

	private int _findFreeSlot()
	{
		for (var slot = 0; slot < _slotBusy.Length; slot++) if (!_slotBusy[slot]) return slot;
		return -1;
	}

	/// <summary>The busy slot that started first, preferring voices that do not loop.</summary>
	private int _oldestSlot()
	{
		int oldest = -1, oldestLooping = -1;
		for (var slot = 0; slot < _slotBusy.Length; slot++)
		{
			if (_slotLoop[slot])
			{
				if (oldestLooping < 0 || _slotStarted[slot] < _slotStarted[oldestLooping]) oldestLooping = slot;
			}
			else if (oldest < 0 || _slotStarted[slot] < _slotStarted[oldest])
			{
				oldest = slot;
			}
		}

		return oldest >= 0 ? oldest : oldestLooping;
	}

	private long _step(SoundEffect sound, float pitchShift) =>
		(long)Math.Round(ToPitchFactor(pitchShift) * sound.MixRate / SampleRate * FixedOne);

	private int _frames(float seconds)
	{
		if (!(seconds > 0f)) return 0;
		var frames = Math.Round((double)seconds * SampleRate);
		return frames >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)frames);
	}

	private static float _sanitizeGain(float gain) => float.IsFinite(gain) ? Math.Max(0f, gain) : 0f;

	private static float _sanitizePan(float pan) => float.IsFinite(pan) ? Math.Clamp(pan, -1f, 1f) : 0f;

	private static AudioBus _sanitizeBus(AudioBus bus) => (uint)bus < BusCount ? bus : AudioBus.Sfx;

	// ---------------------------------------------------------------------------------------------------------------
	// Audio thread
	// ---------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// Mixes the next <c>buffer.Length / 2</c> frames into <paramref name="buffer"/> (interleaved stereo), after applying
	/// the commands flushed so far. Called on the audio thread only (one thread at a time). Does not allocate.
	/// </summary>
	public void Render(Span<float> buffer)
	{
		buffer.Clear();

		while (_commands.TryDequeue(out var command)) _apply(in command);

		var frames = buffer.Length / Channels;
		var master = _audioBus[(int)AudioBus.Master];
		var voices = _voices;

		for (var slot = 0; slot < voices.Length; slot++)
		{
			ref var voice = ref voices[slot];
			if (voice.NotifyPending) _notify(ref voice, slot);
			if (!voice.Active) continue;

			_mix(ref voice, slot, buffer, frames, master);
		}

		for (var i = 0; i < buffer.Length; i++)
		{
			var s = buffer[i];
			if (s > 1f) buffer[i] = 1f;
			else if (s < -1f) buffer[i] = -1f;
		}

		Interlocked.Add(ref _framesRendered, frames);
	}

	private void _apply(in AudioCommand command)
	{
		if (command.Kind == CommandKind.SetBus)
		{
			_audioBus[(int)command.Bus] = command.Value;
			return;
		}

		ref var voice = ref _voices[command.Slot];

		if (command.Kind == CommandKind.Play)
		{
			var sound = command.Sound!;
			voice = new Voice
			{
				Active = true,
				Generation = command.Generation,
				Samples = sound.Samples,
				Frames = sound.Frames,
				SourceChannels = sound.Channels,
				RateStep = (double)sound.MixRate / SampleRate,
				Step = command.Step,
				Volume = command.Value,
				Pan = command.Pan,
				Bus = command.Bus,
				Loop = command.Loop,
				Fade = command.Frames > 0 ? 0f : 1f,
				FadeFrom = 0f,
				FadeTo = 1f,
				FadeLength = command.Frames,
				FadeElapsed = 0,
			};
			return;
		}

		if (!voice.Active || voice.Generation != command.Generation) return;

		switch (command.Kind)
		{
			case CommandKind.Stop:
				if (command.Frames <= 0)
				{
					_finish(ref voice, command.Slot, notify: false);
				}
				else
				{
					voice.FadeFrom = voice.Fade;
					voice.FadeTo = 0f;
					voice.FadeLength = command.Frames;
					voice.FadeElapsed = 0;
					voice.StopWhenFaded = true;
				}
				break;
			case CommandKind.SetVolume:
				voice.Volume = command.Value;
				break;
			case CommandKind.SetPan:
				voice.Pan = command.Value;
				break;
			case CommandKind.SetPitch:
				voice.Step = (long)Math.Round(command.Step * voice.RateStep);
				break;
		}
	}

	private void _mix(ref Voice voice, int slot, Span<float> buffer, int frames, float master)
	{
		var bus = voice.Bus == AudioBus.Master ? 1f : _audioBus[(int)voice.Bus];
		var gain = voice.Volume * bus * master;
		var targetL = voice.Pan > 0f ? gain * (1f - voice.Pan) : gain;
		var targetR = voice.Pan < 0f ? gain * (1f + voice.Pan) : gain;

		if (!voice.HasGain)
		{
			voice.GainL = targetL;
			voice.GainR = targetR;
			voice.HasGain = true;
		}

		var gainL = voice.GainL;
		var gainR = voice.GainR;
		var stepL = frames > 0 ? (targetL - gainL) / frames : 0f;
		var stepR = frames > 0 ? (targetR - gainR) / frames : 0f;
		var ramp = stepL != 0f || stepR != 0f;

		var samples = voice.Samples!;
		var soundFrames = voice.Frames;
		var stereo = voice.SourceChannels == 2;
		var loop = voice.Loop;
		var cubic = Interpolation == AudioInterpolation.Cubic;
		var end = (long)soundFrames << FixedShift;
		var position = voice.Position;
		var step = voice.Step;

		for (var i = 0; i < frames; i++)
		{
			if (position >= end)
			{
				if (!loop)
				{
					voice.Position = position;
					_finish(ref voice, slot, notify: true);
					return;
				}
				position %= end;
			}

			var index = (int)(position >> FixedShift);
			var t = (uint)position * FixedToFloat;

			float left, right;
			if (stereo)
			{
				left = _sample(samples, soundFrames, 2, 0, index, t, loop, cubic);
				right = _sample(samples, soundFrames, 2, 1, index, t, loop, cubic);
			}
			else
			{
				left = right = _sample(samples, soundFrames, 1, 0, index, t, loop, cubic);
			}

			var fade = voice.Fade;
			var finished = false;
			if (voice.FadeLength > 0)
			{
				fade = voice.FadeFrom + (voice.FadeTo - voice.FadeFrom) * ((float)voice.FadeElapsed / voice.FadeLength);
				voice.Fade = fade;
				if (++voice.FadeElapsed >= voice.FadeLength)
				{
					// The fade's last step is heard this frame; the next frame is at the target.
					voice.Fade = voice.FadeTo;
					voice.FadeLength = 0;
					finished = voice.StopWhenFaded;
				}
			}

			if (ramp)
			{
				gainL += stepL;
				gainR += stepR;
			}

			buffer[i * 2] += left * gainL * fade;
			buffer[i * 2 + 1] += right * gainR * fade;

			if (finished)
			{
				_finish(ref voice, slot, notify: true);
				return;
			}

			position += step;
		}

		if (loop && position >= end) position %= end;
		voice.Position = position;
		voice.GainL = targetL;
		voice.GainR = targetR;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float _sample(float[] samples, int frames, int channels, int channel, int index, float t, bool loop, bool cubic)
	{
		var p1 = _at(samples, frames, channels, channel, index, loop);
		if (t == 0f) return p1;

		var p2 = _at(samples, frames, channels, channel, index + 1, loop);
		if (!cubic) return p1 + (p2 - p1) * t;

		var p0 = _at(samples, frames, channels, channel, index - 1, loop);
		var p3 = _at(samples, frames, channels, channel, index + 2, loop);

		// Catmull-Rom.
		var a = -0.5f * p0 + 1.5f * p1 - 1.5f * p2 + 0.5f * p3;
		var b = p0 - 2.5f * p1 + 2f * p2 - 0.5f * p3;
		var c = -0.5f * p0 + 0.5f * p2;
		return ((a * t + b) * t + c) * t + p1;
	}

	/// <summary>The sample of <paramref name="channel"/> at frame <paramref name="index"/>: wrapped when looping, else
	/// silence past the end and the first frame before the start.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float _at(float[] samples, int frames, int channels, int channel, int index, bool loop)
	{
		if ((uint)index >= (uint)frames)
		{
			if (loop)
			{
				index %= frames;
				if (index < 0) index += frames;
			}
			else if (index < 0)
			{
				index = 0;
			}
			else
			{
				return 0f;
			}
		}

		return samples[index * channels + channel];
	}

	private void _finish(ref Voice voice, int slot, bool notify)
	{
		var generation = voice.Generation;
		voice = default;
		voice.Generation = generation;
		if (!notify) return;

		voice.NotifyPending = true;
		_notify(ref voice, slot);
	}

	private void _notify(ref Voice voice, int slot)
	{
		if (_finished.TryEnqueue(new FinishedVoice(slot, voice.Generation))) voice.NotifyPending = false;
	}

	internal enum CommandKind : byte
	{
		Play,
		Stop,
		SetVolume,
		SetPitch,
		SetPan,
		SetBus,
	}

	internal struct AudioCommand
	{
		public CommandKind Kind;
		public AudioBus Bus;
		public bool Loop;
		public int Slot;
		public int Generation;
		public int Frames;
		public float Value;
		public float Pan;
		public long Step;
		public SoundEffect? Sound;
	}

	internal readonly record struct FinishedVoice(int Slot, int Generation);

	private struct Voice
	{
		public bool Active;
		public bool Loop;
		public bool HasGain;
		public bool StopWhenFaded;
		public bool NotifyPending;
		public AudioBus Bus;
		public int Generation;
		public int SourceChannels;
		public int Frames;
		public int FadeLength;
		public int FadeElapsed;
		public float Volume;
		public float Pan;
		public float Fade;
		public float FadeFrom;
		public float FadeTo;
		public float GainL;
		public float GainR;
		public long Position;
		public long Step;
		public double RateStep;
		public float[]? Samples;
	}
}
