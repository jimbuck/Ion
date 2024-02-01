
namespace Ion.Extensions.Assets;

public interface IAsset : IDisposable
{
	nint Id { get; }
	string Name { get; }
}
