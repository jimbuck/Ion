using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

public interface ISoundEffect : IAsset
{
	float Duration { get; }
}
