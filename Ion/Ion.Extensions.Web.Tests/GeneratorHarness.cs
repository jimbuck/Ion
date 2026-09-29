using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Ion.Extensions.Web.Generators;

namespace Ion.Extensions.Web.Tests;

/// <summary>Compiles test sources against the engine assemblies and runs the routing generator on them.</summary>
internal static class GeneratorHarness
{
	/// <summary>Set to 1 to rewrite the golden files from the current output instead of comparing.</summary>
	public const string UpdateGoldenVariable = "ION_UPDATE_GOLDEN";

	private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

	private static readonly ImmutableArray<MetadataReference> References =
	[
		.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Where(static p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) && !Path.GetFileName(p).StartsWith("xunit", StringComparison.Ordinal))
			.Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p)),
	];

	/// <summary>The usings every test source gets.</summary>
	public const string Usings = """
		using System;
		using System.Text.Json;
		using System.Text.Json.Serialization;
		using System.Text.Json.Serialization.Metadata;
		using System.Threading.Tasks;
		using Ion.Extensions.Web;

		""";

	/// <summary>
	/// A hand-written JsonSerializerContext (the System.Text.Json generator does not run in these compilations) listing
	/// <c>Info</c> and <c>Payload</c>.
	/// </summary>
	public const string JsonContext = """

		public record struct Info(int Score, int Balls);
		public sealed class Payload { public string? Name { get; set; } }

		[JsonSerializable(typeof(Info))]
		[JsonSerializable(typeof(Payload))]
		public sealed class GameJson : JsonSerializerContext
		{
			public GameJson() : base(null) { }
			public static GameJson Default { get; } = new();
			protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
			public override JsonTypeInfo? GetTypeInfo(Type type) => null;
		}

		""";

	public static GeneratorResult Run(string source)
	{
		var tree = CSharpSyntaxTree.ParseText(Usings + source, ParseOptions, path: "Game.cs");
		var compilation = CSharpCompilation.Create("TestGame", [tree], References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new WebRoutesGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
		var run = driver.GetRunResult();
		var generated = run.GeneratedTrees.Where(static t => Path.GetFileName(t.FilePath) == WebRoutesGenerator.FileName).Select(static t => t.GetText().ToString()).SingleOrDefault() ?? "";
		return new GeneratorResult((CSharpCompilation)output, generated, [.. run.Diagnostics.Concat(generatorDiagnostics).Distinct()]);
	}

	public static void AssertGolden(string name, string actual, [CallerFilePath] string callerPath = "")
	{
		var path = Path.Combine(Path.GetDirectoryName(callerPath)!, "Golden", name + ".g.cs");
		actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
		if (Environment.GetEnvironmentVariable(UpdateGoldenVariable) == "1")
		{
			File.WriteAllText(path, actual);
			return;
		}

		Assert.True(File.Exists(path), $"Golden file {path} is missing; run the tests with {UpdateGoldenVariable}=1 to create it.");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
	}
}

internal sealed record GeneratorResult(CSharpCompilation Output, string Generated, ImmutableArray<Diagnostic> Diagnostics)
{
	/// <summary>Errors and warnings of the compilation with the generated code (there should be none).</summary>
	public IReadOnlyList<Diagnostic> CompilationProblems => [.. Output.GetDiagnostics().Where(static d => d.Severity >= DiagnosticSeverity.Warning)];

	public IReadOnlyList<Diagnostic> Web(string id) => [.. Diagnostics.Where(d => d.Id == id)];
}
