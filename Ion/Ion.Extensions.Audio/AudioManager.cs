using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ion.Extensions.Audio;

/// <summary>
/// The engine's <see cref="IAudioManager"/>: the <see cref="AudioMixer"/> plus the <see cref="IAudioOutput"/> it plays
/// through. <see cref="Start"/> opens the output and falls back to a <see cref="NullAudioOutput"/> (with a warning) when
/// the device or native library is unavailable, so a missing sound card never stops the game. <see cref="Update"/>
/// runs once per frame in the Last stage: it flushes the frame's commands to the audio thread and, on the null output,
/// renders the audio due by the game clock.
/// </summary>
public class AudioManager : IAudioManager, IDisposable
{
	private readonly ILogger _logger;
	private bool _disposed;

	/// <summary>
	/// Creates a manager that mixes with <paramref name="mixer"/> and plays through <paramref name="output"/>.
	/// </summary>
	public AudioManager(AudioMixer mixer, IAudioOutput output, ILogger<AudioManager>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(mixer);
		ArgumentNullException.ThrowIfNull(output);
		Mixer = mixer;
		Output = output;
		_logger = logger ?? (ILogger)NullLogger.Instance;
	}

	/// <summary>The mixer.</summary>
	public AudioMixer Mixer { get; }

	/// <summary>The output in use: the configured one, or a <see cref="NullAudioOutput"/> after a fallback.</summary>
	public IAudioOutput Output { get; private set; }

	/// <summary>Whether <see cref="Start"/> has run (and <see cref="Stop"/> has not).</summary>
	public bool IsStarted { get; private set; }

	/// <summary>Whether the configured output failed and the null output replaced it.</summary>
	public bool IsFallback { get; private set; }

	/// <summary>
	/// Starts the output. If it throws, logs a warning and switches to a <see cref="NullAudioOutput"/>.
	/// </summary>
	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (IsStarted) return;

		try
		{
			Output.Start(Mixer.Format, Mixer.Render);
		}
		catch (Exception ex) when (Output is not NullAudioOutput)
		{
			_fallBack(ex, "could not start");
		}

		IsStarted = true;
	}

	/// <summary>
	/// Once per frame (Last stage): hands the frame's commands to the audio thread, collects finished voices and, on the
	/// null output, renders the audio due by <paramref name="elapsed"/> (total game time).
	/// </summary>
	public void Update(TimeSpan elapsed)
	{
		if (!IsStarted) return;

		if (!Output.IsRunning && Output is not NullAudioOutput)
		{
			_fallBack((Output as OpenAlAudioOutput)?.Error, "stopped");
		}

		Mixer.Flush();

		if (Output is NullAudioOutput nullOutput) nullOutput.Advance(elapsed);
	}

	/// <summary>
	/// Stops the output. <see cref="Start"/> can start it again.
	/// </summary>
	public void Stop()
	{
		if (!IsStarted) return;
		IsStarted = false;
		Output.Stop();
	}

	private void _fallBack(Exception? error, string what)
	{
		var failed = Output;
		_logger.LogWarning(error, "Audio output {Output} {What}; falling back to the null output (the game runs silently). {Reason}",
			failed.Name, what, error?.Message);

		try
		{
			failed.Dispose();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Disposing the failed audio output threw.");
		}

		var fallback = new NullAudioOutput(failed.BufferSize > 0 ? failed.BufferSize : 512);
		fallback.Start(Mixer.Format, Mixer.Render);
		Output = fallback;
		IsFallback = true;
	}

	/// <inheritdoc/>
	public float MasterVolume
	{
		get => Mixer.MasterVolume;
		set => Mixer.MasterVolume = value;
	}

	/// <inheritdoc/>
	public virtual VoiceHandle Play(ISoundEffect soundEffect, float volume = 1f, float pitchShift = 0f, float pan = 0f, bool loop = false, AudioBus bus = AudioBus.Sfx, float fadeIn = 0f) =>
		Mixer.Play(soundEffect, volume, pitchShift, pan, loop, bus, fadeIn);

	/// <inheritdoc/>
	public void Stop(VoiceHandle voice, float fadeOut = 0f) => Mixer.Stop(voice, fadeOut);

	/// <inheritdoc/>
	public void StopAll(float fadeOut = 0f) => Mixer.StopAll(fadeOut);

	/// <inheritdoc/>
	public void SetVolume(VoiceHandle voice, float volume) => Mixer.SetVolume(voice, volume);

	/// <inheritdoc/>
	public void SetPitch(VoiceHandle voice, float pitchShift) => Mixer.SetPitch(voice, pitchShift);

	/// <inheritdoc/>
	public void SetPan(VoiceHandle voice, float pan) => Mixer.SetPan(voice, pan);

	/// <inheritdoc/>
	public bool IsPlaying(VoiceHandle voice) => Mixer.IsPlaying(voice);

	/// <inheritdoc/>
	public float GetBusVolume(AudioBus bus) => Mixer.GetBusVolume(bus);

	/// <inheritdoc/>
	public void SetBusVolume(AudioBus bus, float volume) => Mixer.SetBusVolume(bus, volume);

	/// <summary>
	/// Stops and disposes the output.
	/// </summary>
	public void Dispose()
	{
		// Registered both as itself and as IAudioManager, so the container may call this twice.
		if (_disposed) return;
		_disposed = true;

		IsStarted = false;
		Output.Dispose();
		GC.SuppressFinalize(this);
	}
}
