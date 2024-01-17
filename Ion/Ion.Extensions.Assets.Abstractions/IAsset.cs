
namespace Ion.Extensions.Assets;

public interface IAsset : IDisposable
{
	int Id { get; }
	string Name { get; }
}
