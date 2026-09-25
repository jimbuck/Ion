using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ion.Generators.Tests;

/// <summary>
/// Compiles test sources against the engine assemblies, runs the schedule generator on them, and optionally loads the
/// result to run it.
/// </summary>
internal static class GeneratorHarness
{
	/// <summary>Set to 1 to rewrite the golden files from the current output instead of comparing.</summary>
	public const string UpdateGoldenVariable = "ION_UPDATE_GOLDEN";

	public static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
		.WithLanguageVersion(LanguageVersion.Latest)
		.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Ion.Generated")]);

	public static readonly ImmutableArray<MetadataReference> References = LoadReferences();

	private static ImmutableArray<MetadataReference> LoadReferences()
	{
		// The running app's trusted platform assemblies include the framework and every dependency of this test project,
		// among them the Ion engine assemblies.
		var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) && !Path.GetFileName(p).StartsWith("xunit", StringComparison.Ordinal));
		return [.. paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
	}

	public static CSharpCompilation Compile(string source, string assemblyName = "TestApp", OutputKind kind = OutputKind.DynamicallyLinkedLibrary, IEnumerable<MetadataReference>? extraReferences = null)
	{
		var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Program.cs");
		return CSharpCompilation.Create(
			assemblyName,
			[tree],
			References.Concat(extraReferences ?? []),
			new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
	}

	/// <summary>Runs the generator on <paramref name="source"/>.</summary>
	public static GeneratorResult Run(string source, string assemblyName = "TestApp", IEnumerable<MetadataReference>? extraReferences = null)
	{
		var compilation = Compile(source, assemblyName, extraReferences: extraReferences);
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new ScheduleGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
		var run = driver.GetRunResult();
		var generated = run.GeneratedTrees.Select(t => t.GetText().ToString()).SingleOrDefault() ?? "";
		return new GeneratorResult(compilation, (CSharpCompilation)output, generated, [.. run.Diagnostics.Concat(generatorDiagnostics).Distinct()]);
	}

	/// <summary>Compares <paramref name="actual"/> with the golden file <paramref name="name"/> (or rewrites it).</summary>
	public static void AssertGolden(string name, string actual, [CallerFilePath] string callerPath = "")
	{
		var path = Path.Combine(Path.GetDirectoryName(callerPath)!, "Golden", name + ".g.cs");
		actual = actual.Replace("\r\n", "\n");

		if (Environment.GetEnvironmentVariable(UpdateGoldenVariable) == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual);
			return;
		}

		Assert.True(File.Exists(path), $"Golden file {path} is missing; run the tests with {UpdateGoldenVariable}=1 to create it.");
		var expected = File.ReadAllText(path).Replace("\r\n", "\n");
		Assert.Equal(expected, actual);
	}

	/// <summary>Emits <paramref name="compilation"/> and loads it in a collectible context.</summary>
	public static Assembly Load(Compilation compilation)
	{
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		stream.Position = 0;
		return new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true).LoadFromStream(stream);
	}
}

internal sealed record GeneratorResult(CSharpCompilation Input, CSharpCompilation Output, string Generated, ImmutableArray<Diagnostic> Diagnostics)
{
	/// <summary>The errors of the compilation with the generated code (there should be none).</summary>
	public IEnumerable<Diagnostic> CompilationErrors => Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

	/// <summary>The warnings and errors of the compilation with the generated code, and the generator's.</summary>
	public IEnumerable<Diagnostic> AllDiagnostics => Output.GetDiagnostics().Concat(Diagnostics).Where(d => d.Severity >= DiagnosticSeverity.Warning);

	public void AssertCompiles()
	{
		var errors = CompilationErrors.ToList();
		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors) + Environment.NewLine + Generated);
	}
}
