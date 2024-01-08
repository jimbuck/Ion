namespace Ion.Hosting.Scenes;

public interface ISceneBuilder
{
	ISceneBuilder AddSystem<T>() where T : class;
}