using Kyber.Hosting.Scenes;

namespace Kyber.Scenes;

public interface ISceneManager
{
	string CurrentScene { get; }
	string[] Scenes { get; }

	void LoadScene(string name);
	//void LoadScene<TScene, TTransition>(float duration) where TScene : ISceneConfiguration where TTransition : Transition;
	void LoadScene<TScene>() where TScene : ISceneConfiguration;
}
