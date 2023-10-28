
namespace Kyber.Scenes;

public interface ISceneManager
{
	string CurrentScene { get; }

	void LoadScene(string name);
}
