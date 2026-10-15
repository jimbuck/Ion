using System.Reflection;

using Microsoft.CodeAnalysis;

namespace Ion.Generators.Tests;

/// <summary>
/// The generator declares the <c>Program</c> class of an Ion game with top-level statements public, so tests can run its
/// entry point (<c>IonTestHost.UseEntryPoint&lt;Program&gt;()</c>), unless the program declares it itself.
/// </summary>
public class PublicProgramTests
{
	private const string Game = """
		public sealed class Tick { [Update] public void Run(GameTime dt) { } }

		""";

	private const string TopLevel = """
		using Ion;

		var builder = IonApplication.CreateBuilder(args);
		builder.AddSystem<Tick>();
		using var game = builder.Build();
		game.UseSystem<Tick>();
		game.Run();

		""";

	[Fact]
	public void TheProgramOfTopLevelStatementsIsPublic()
	{
		var result = GeneratorHarness.Run(TopLevel + Game, kind: OutputKind.ConsoleApplication);
		result.AssertCompiles();

		Assert.Contains("public partial class Program", result.GeneratedProgram, StringComparison.Ordinal);
		var program = GeneratorHarness.Load(result.Output).GetType("Program")!;
		Assert.True(program.IsPublic);
	}

	[Theory]
	[InlineData("internal partial class Program { }")]
	[InlineData("public partial class Program { }")]
	public void AProgramThatDeclaresItsAccessibilityIsLeftAlone(string declaration)
	{
		var result = GeneratorHarness.Run(TopLevel + Game + declaration, kind: OutputKind.ConsoleApplication);
		result.AssertCompiles();

		Assert.Empty(result.GeneratedProgram);
	}

	[Fact]
	public void TopLevelStatementsThatDoNotCreateAnIonApplicationAreLeftAlone()
	{
		var result = GeneratorHarness.Run("System.Console.WriteLine(\"hello\");\n", kind: OutputKind.ConsoleApplication);
		result.AssertCompiles();

		Assert.Empty(result.GeneratedProgram);
		var program = GeneratorHarness.Load(result.Output).GetType("Program", throwOnError: false, ignoreCase: false);
		Assert.False(program?.IsPublic ?? false);
	}

	[Fact]
	public void AGameWithoutTopLevelStatementsHasNoGeneratedProgram()
	{
		var result = GeneratorHarness.Run("using Ion;\n" + Game + """
			public static class Program
			{
				public static void Main(string[] args)
				{
					var builder = IonApplication.CreateBuilder(args);
					builder.AddSystem<Tick>();
					using var game = builder.Build();
					game.UseSystem<Tick>();
					game.Run();
				}
			}
			""", kind: OutputKind.ConsoleApplication);
		result.AssertCompiles();

		Assert.Empty(result.GeneratedProgram);
		Assert.Equal(TypeAttributes.Public, GeneratorHarness.Load(result.Output).GetType("Program")!.Attributes & TypeAttributes.VisibilityMask);
	}
}
