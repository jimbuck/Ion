namespace Ion.Extensions.Audio;

/// <summary>
/// The audio output used by <see cref="BuilderExtensions.AddAudio(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, Action{AudioConfig}?)"/>.
/// </summary>
public enum AudioBackend
{
	/// <summary>The platform device output (OpenAL), falling back to <see cref="Null"/> when no device or library is available.</summary>
	Auto = 0,

	/// <summary>OpenAL (OpenAL Soft on desktop and Linux handhelds), falling back to <see cref="Null"/> when it cannot start.</summary>
	OpenAL,

	/// <summary>No device: the mixer runs on the game thread, driven by the game clock, and nothing is heard.</summary>
	Null,
}

/// <summary>
/// How a voice is resampled when its pitch is shifted (or when a sound's stored rate differs from the output rate).
/// </summary>
public enum AudioInterpolation
{
	/// <summary>Two-point linear interpolation: cheapest, slightly dull at high pitch shifts.</summary>
	Linear = 0,

	/// <summary>Four-point cubic (Catmull-Rom) interpolation: smoother, about twice the cost of <see cref="Linear"/>.</summary>
	Cubic,
}

/// <summary>
/// What <see cref="IAudioManager.Play"/> does when all <see cref="AudioConfig.MaxVoices"/> voices are busy.
/// </summary>
public enum VoiceStealing
{
	/// <summary>
	/// Replace the voice that started first, preferring voices that do not loop, so the newest sound is always heard.
	/// </summary>
	Oldest = 0,

	/// <summary>Refuse the new voice: <see cref="IAudioManager.Play"/> returns an invalid handle.</summary>
	Refuse,
}

/// <summary>
/// Audio settings, bound from the <c>Ion:Audio</c> configuration section.
/// </summary>
public sealed class AudioConfig
{
	/// <summary>
	/// The mixer's output rate in frames per second. Every sound is resampled to it when loaded. Defaults to 48000.
	/// </summary>
	public int OutputRate { get; set; } = 48000;

	/// <summary>
	/// The size of the fixed voice pool. Defaults to 64.
	/// </summary>
	public int MaxVoices { get; set; } = 64;

	/// <summary>
	/// Frames mixed per buffer. Latency is about <see cref="BufferFrames"/> times <see cref="BufferCount"/> divided by
	/// <see cref="OutputRate"/> (43 ms with the defaults). Defaults to 512.
	/// </summary>
	public int BufferFrames { get; set; } = 512;

	/// <summary>
	/// Buffers queued on the device output. Defaults to 4.
	/// </summary>
	public int BufferCount { get; set; } = 4;

	/// <summary>
	/// The output. Defaults to <see cref="AudioBackend.Auto"/>.
	/// </summary>
	public AudioBackend Backend { get; set; } = AudioBackend.Auto;

	/// <summary>
	/// The OpenAL device to open, by name. Null or empty opens the default device.
	/// </summary>
	public string? Device { get; set; }

	/// <summary>
	/// Resampling used for pitch. Defaults to <see cref="AudioInterpolation.Linear"/>.
	/// </summary>
	public AudioInterpolation Interpolation { get; set; } = AudioInterpolation.Linear;

	/// <summary>
	/// What happens when every voice is busy. Defaults to <see cref="VoiceStealing.Oldest"/>.
	/// </summary>
	public VoiceStealing VoiceStealing { get; set; } = VoiceStealing.Oldest;

	/// <summary>
	/// Capacity of the lock-free command queue from the game thread to the audio thread, rounded up to a power of two.
	/// Commands that do not fit wait on the game thread until the next frame. Defaults to 1024.
	/// </summary>
	public int CommandCapacity { get; set; } = 1024;

	/// <summary>
	/// Throws <see cref="ArgumentOutOfRangeException"/> when a value is out of range.
	/// </summary>
	public void Validate()
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(OutputRate, 8000, nameof(OutputRate));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(OutputRate, 384000, nameof(OutputRate));
		ArgumentOutOfRangeException.ThrowIfLessThan(MaxVoices, 1, nameof(MaxVoices));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxVoices, 4096, nameof(MaxVoices));
		ArgumentOutOfRangeException.ThrowIfLessThan(BufferFrames, 64, nameof(BufferFrames));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferFrames, 65536, nameof(BufferFrames));
		ArgumentOutOfRangeException.ThrowIfLessThan(BufferCount, 2, nameof(BufferCount));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferCount, 64, nameof(BufferCount));
		ArgumentOutOfRangeException.ThrowIfLessThan(CommandCapacity, 16, nameof(CommandCapacity));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(CommandCapacity, 1 << 20, nameof(CommandCapacity));
	}
}
