namespace Ion.Extensions.Audio;

/// <summary>
/// A sound decoded into memory: interleaved float32 samples (mono or stereo) at <see cref="MixRate"/>, which is the
/// mixer's output rate when the sound was loaded through the asset manager. Create one with
/// <see cref="SoundDecoder.Decode(string, Stream, int)"/> or from samples you generate.
/// </summary>
public sealed class SoundEffect : ISoundEffect
{
	private static long _nextId;

	/// <summary>
	/// Creates a sound from interleaved samples.
	/// </summary>
	/// <param name="name">The name, usually the asset path.</param>
	/// <param name="samples">Interleaved samples in [-1, 1]; the array is used as is, not copied.</param>
	/// <param name="channels">1 (mono) or 2 (stereo).</param>
	/// <param name="mixRate">The rate of <paramref name="samples"/> in frames per second.</param>
	/// <param name="sourceSampleRate">The rate of the original file. Defaults to <paramref name="mixRate"/>.</param>
	public SoundEffect(string name, float[] samples, int channels, int mixRate, int sourceSampleRate = 0)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(samples);
		if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels), channels, "A sound has 1 or 2 channels.");
		ArgumentOutOfRangeException.ThrowIfLessThan(mixRate, 1);
		if (samples.Length % channels != 0) throw new ArgumentException($"The sample count {samples.Length} is not a multiple of the channel count {channels}.", nameof(samples));

		Id = (nint)Interlocked.Increment(ref _nextId);
		Name = name;
		Samples = samples;
		Channels = channels;
		MixRate = mixRate;
		SampleRate = sourceSampleRate > 0 ? sourceSampleRate : mixRate;
		Frames = samples.Length / channels;
		Duration = (float)((double)Frames / mixRate);
	}

	/// <inheritdoc/>
	public nint Id { get; }

	/// <inheritdoc/>
	public string Name { get; }

	/// <inheritdoc/>
	public float Duration { get; }

	/// <summary>1 (mono) or 2 (stereo): the channels of <see cref="Samples"/>.</summary>
	public int Channels { get; }

	/// <inheritdoc/>
	public int SampleRate { get; }

	/// <summary>The rate of <see cref="Samples"/> in frames per second.</summary>
	public int MixRate { get; }

	/// <summary>The number of frames (samples per channel).</summary>
	public int Frames { get; }

	/// <summary>Interleaved float32 samples, <see cref="Frames"/> times <see cref="Channels"/> long.</summary>
	public float[] Samples { get; }

	/// <summary>Nothing to release: the samples are managed memory, and voices still playing keep them alive.</summary>
	public void Dispose()
	{
	}
}
