using System.Collections;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Ion.Generators.Tests;

/// <summary>
/// The schedule diagnostics as compiler diagnostics (Roslyn source generator test SDK): id, severity and location, and
/// the message compared with the one the runtime reports for the same registrations.
/// </summary>
public class DiagnosticTests
{
	public static TheoryData<string> Scenarios => [.. DiagnosticScenarios.All.Select(s => s.Id)];

	private static DiagnosticScenarios.Scenario Get(string id) => DiagnosticScenarios.All.Single(s => s.Id == id);

	[Theory]
	[MemberData(nameof(Scenarios))]
	public async Task ReportsTheDiagnosticAtTheMarkedLocations(string id)
	{
		var scenario = Get(id);
		var descriptor = Diagnostics.ForCode(scenario.Code);
		var markers = System.Text.RegularExpressions.Regex.Matches(scenario.Source, @"\{\|#(\d+):").Count;

		var test = new GeneratorTest { TestCode = scenario.Source };
		for (var i = 0; i < markers; i++) test.ExpectedDiagnostics.Add(new DiagnosticResult(descriptor.Id, descriptor.DefaultSeverity).WithLocation(i));

		await test.RunAsync();
	}

	[Theory]
	[MemberData(nameof(Scenarios))]
	public void MessagesMatchTheRuntime(string id)
	{
		var scenario = Get(id);

		// Compile time: the generator's diagnostics with this code.
		var generated = GeneratorHarness.Run(DiagnosticScenarios.Plain(scenario.Source));
		var compileTime = generated.Diagnostics.Where(d => d.Id == scenario.Code).Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ToList();
		Assert.NotEmpty(compileTime);

		// Run time: the same registrations planned by reflection (compiled without the generator).
		var assembly = GeneratorHarness.Load(GeneratorHarness.Compile(scenario.RuntimeSource, "Runtime" + id));
		var diagnostics = (IEnumerable)assembly.GetType("App")!.GetMethod("Plan")!.Invoke(null, null)!;
		var runtime = diagnostics.Cast<ScheduleDiagnostic>().Where(d => d.Code == scenario.Code).Select(d => d.Message).ToList();
		Assert.NotEmpty(runtime);

		foreach (var message in compileTime) Assert.Contains(message, runtime);
	}

	[Fact]
	public async Task WarnsWhenInterceptorsAreNotEnabled()
	{
		var test = new GeneratorTest
		{
			TestCode = """
				using Ion;

				public sealed class Tick { [Update] public void Run(GameTime dt) { } }

				public static class App
				{
					public static void Run(IIonApplication app) => {|#0:app.UseSystem<Tick>()|};
				}
				""",
			Features = [],
		};
		test.ExpectedDiagnostics.Add(new DiagnosticResult(Diagnostics.InterceptorsDisabled.Id, DiagnosticSeverity.Warning).WithLocation(0));
		await test.RunAsync();
	}

	/// <summary>The Roslyn SDK test, with the engine assemblies referenced and interceptors enabled.</summary>
	private sealed class GeneratorTest : CSharpSourceGeneratorTest<ScheduleGenerator, DefaultVerifier>
	{
		public GeneratorTest()
		{
			// The engine and framework assemblies of this test run (no reference packs are downloaded).
			ReferenceAssemblies = new ReferenceAssemblies("net10.0");
			TestState.AdditionalReferences.AddRange(GeneratorHarness.References);
			TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
		}

		public KeyValuePair<string, string>[] Features { get; init; } = [new("InterceptorsNamespaces", "Ion.Generated")];

		protected override ParseOptions CreateParseOptions() =>
			((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion.Latest).WithFeatures(Features);

		protected override CompilationOptions CreateCompilationOptions() =>
			((CSharpCompilationOptions)base.CreateCompilationOptions()).WithNullableContextOptions(NullableContextOptions.Enable);
	}
}
