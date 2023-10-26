namespace Kyber.Hosting.Scenes;

public interface ISceneBuilder
{
	ISceneBuilder AddSystem<T>() where T : class;
}