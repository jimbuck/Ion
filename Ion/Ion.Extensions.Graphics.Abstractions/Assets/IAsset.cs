
namespace Ion.Extensions.Graphics;

public interface IAsset : IDisposable
{
	int Id { get; }
	string Name { get; }
}
