namespace Kyber.Generators;

public readonly struct SceneToGenerate
{
	public readonly string Name;
	public readonly List<string> UpdateCalls;
	public readonly List<string> DrawCalls;

	public SceneToGenerate(string name, List<string> updateCalls, List<string> drawCalls)
	{
		Name = name;
		UpdateCalls = updateCalls;
		DrawCalls = drawCalls;
	}
}
