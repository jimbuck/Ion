namespace Ion.Extensions.Scenes;

public interface ICurrentScene
{
	bool IsRoot { get; }
	int SceneId { get; }
}

public sealed class CurrentScene : ICurrentScene
{
	public static readonly int Root = 0;

	public int SceneId { get; private set; }
	public bool IsRoot { get; private set; }

	public CurrentScene()
	{
		SceneId = Root;
		IsRoot = true;
	}

	internal void Set(int? sceneId)
	{
		SceneId = sceneId ?? Root;
		IsRoot = SceneId == Root;
	}

	public override string ToString()
	{
		return $"Scene{SceneId}";
	}
}
