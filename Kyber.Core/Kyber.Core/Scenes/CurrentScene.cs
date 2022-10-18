namespace Kyber.Core.Scenes;

public sealed class CurrentScene {
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

    public static implicit operator string(CurrentScene cs) => cs.Name;

    public override string ToString()
	{
		return Name ?? Root;
	}
}
