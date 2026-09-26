using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

/// <summary>
/// A sound loaded with <c>IAssetManager.Load&lt;ISoundEffect&gt;(path)</c> and played with <see cref="IAudioManager.Play"/>.
/// The engine decodes the whole file at load time (WAV, OGG Vorbis and MP3) into float32 samples at the mixer's
/// output rate.
/// </summary>
public interface ISoundEffect : IAsset
{
	/// <summary>
	/// Length in seconds. 0 when the file could not be read (for example a header-only headless sound of an unknown format).
	/// </summary>
	float Duration { get; }

	/// <summary>
	/// Channel count of the file (1 for mono, 2 for stereo). Files with more channels keep their first two when decoded.
	/// </summary>
	int Channels { get; }

	/// <summary>
	/// Samples per second of the file, before it was resampled to the mixer's output rate.
	/// </summary>
	int SampleRate { get; }
}
