namespace Ion.Extensions.Audio;

/// <summary>
/// A group of voices sharing a volume. Every bus feeds the master bus.
/// </summary>
public enum AudioBus
{
	/// <summary>The master bus: voices on it get only the master volume.</summary>
	Master = 0,

	/// <summary>Sound effects (the default for <see cref="IAudioManager.Play"/>).</summary>
	Sfx = 1,

	/// <summary>Music.</summary>
	Music = 2,
}

/// <summary>
/// Identifies one voice started by <see cref="IAudioManager.Play"/>. A handle stays safe to use after its voice ends:
/// the slot's generation changes when it is reused, so calls with a stale handle do nothing.
/// </summary>
/// <param name="Slot">The index of the voice in the mixer's fixed-size pool.</param>
/// <param name="Generation">The generation of the slot when the voice started. Never 0 for a valid handle.</param>
public readonly record struct VoiceHandle(int Slot, int Generation)
{
	/// <summary>
	/// An invalid handle, the same as <c>default</c>.
	/// </summary>
	public static VoiceHandle None => default;

	/// <summary>
	/// Whether this handle came from a voice that started (it may have finished since; see <see cref="IAudioManager.IsPlaying"/>).
	/// </summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// The format an <see cref="IAudioOutput"/> plays: interleaved float32 samples.
/// </summary>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Channels">Interleaved channels per frame. The engine mixer always produces 2 (stereo).</param>
public readonly record struct AudioFormat(int SampleRate, int Channels);

/// <summary>
/// Fills <paramref name="buffer"/> with interleaved float32 samples (<c>buffer.Length / channels</c> frames). Called on
/// the output's audio thread; it must not block or allocate.
/// </summary>
public delegate void AudioRenderCallback(Span<float> buffer);

/// <summary>
/// A sink for mixed audio: a device (OpenAL) or nothing (the null output). The output pulls audio by calling the render
/// callback, usually on a thread of its own.
/// </summary>
public interface IAudioOutput : IDisposable
{
	/// <summary>
	/// A short name for logs, for example <c>OpenAL</c> or <c>Null</c>.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Frames rendered per callback.
	/// </summary>
	int BufferSize { get; }

	/// <summary>
	/// Whether <see cref="Start"/> succeeded and <see cref="Stop"/> has not been called.
	/// </summary>
	bool IsRunning { get; }

	/// <summary>
	/// Opens the output and starts pulling audio from <paramref name="callback"/>.
	/// </summary>
	/// <exception cref="Exception">The device or native library is unavailable. The engine then falls back to the null output.</exception>
	void Start(AudioFormat format, AudioRenderCallback callback);

	/// <summary>
	/// Stops pulling audio and closes the output. Safe to call more than once.
	/// </summary>
	void Stop();
}
