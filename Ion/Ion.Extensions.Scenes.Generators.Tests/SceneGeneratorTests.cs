using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ion.Extensions.Scenes.Generators.Tests;

public class SceneGeneratorTests
{
	[Fact(Skip = "WIP")]
	public Task TestSceneGenerator()
	{
		// The source code to test
		var source = @"
using Ion.Extensions.Scenes;

namespace Test.SceneGenerator {
	[Scenes]
	public enum Scene
	{
		MainMenu = 1,
		Gameplay,
	}
}";

		// Pass the source code to our helper and snapshot test the output
		return TestHelper.Verify(source);
	}
}

public static class TestHelper
{
	public static Task Verify(string source)
	{
		// Parse the provided string into a C# syntax tree
		SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

		// Create a Roslyn compilation for the syntax tree.
		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName: "Tests",
			syntaxTrees: [syntaxTree]);


		// Create an instance of our incremental source generator
		var generator = new ScenesGenerator();

		// The GeneratorDriver is used to run our generator against a compilation
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

		// Run the source generator!
		driver = driver.RunGenerators(compilation);

		var runResults = driver.GetRunResult();
		foreach(var generatedTree in runResults.GeneratedTrees)
		{
			var filename = generatedTree.FilePath;
			var contents = generatedTree.GetText().ToString();
			Console.WriteLine(@$"// Generated File: {filename}
{contents}");
		}

		// Use verify to snapshot test the source generator output!
		return Verifier.Verify(driver);
	}
}