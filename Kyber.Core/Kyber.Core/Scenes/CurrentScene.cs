namespace Kyber.Core.Scenes;

public sealed class CurrentScene {

	private string _value;
	public static readonly string Root = "<ROOT>";

	public CurrentScene()
	{
		_value = Root;
	}

	internal void Set(string name)
	{
		_value = name;
	}

    public static implicit operator string(CurrentScene cs) => cs._value;

    public override string ToString()
	{
		return _value ?? Root;
	}
}
