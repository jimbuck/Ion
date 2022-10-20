namespace Kyber.Scenes;

public interface ICurrentScene
{
	bool IsRoot { get; }
	string Name { get; }
}

public sealed class CurrentScene : ICurrentScene
{
	public static readonly string Root = "<ROOT>";

	public string Name { get; private set; }
	public bool IsRoot { get; private set; }

	public CurrentScene()
	{
		Name = Root;
		IsRoot = true;
	}

	internal void Set(string name)
	{
		Name = name;
		IsRoot = Name == Root;
	}

	public override string ToString()
	{
		return Name ?? Root;
	}
}
