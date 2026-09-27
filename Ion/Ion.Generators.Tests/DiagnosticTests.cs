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
	public async Task SystemsRegisteredOnTheApplicationBuilderAreNotReportedAsUnregistered()
	{
		// builder.AddSystem<T>() (and AddSystem(typeof(T))) registers the system, like services.AddSingleton<T>(); only the
		// system registered nowhere is reported (ION009).
		var test = new GeneratorTest
		{
			TestCode = """
				using Ion;

				public sealed class Generic { [Update] public void Run(GameTime dt) { } }
				public sealed class ByType { [Update] public void Run(GameTime dt) { } }
				public sealed class Lonely { [Update] public void Run(GameTime dt) { } }

				public static class App
				{
					public static void Run()
					{
						var builder = IonApplication.CreateBuilder();
						builder.AddSystem<Generic>().AddSystem(typeof(ByType));
						var app = builder.Build();
						app.UseSystem<Generic>().UseSystem<ByType>();
						{|#0:app.UseSystem<Lonely>()|};
						app.Build();
					}
				}
				""",
		};
		test.ExpectedDiagnostics.Add(new DiagnosticResult(Diagnostics.UnregisteredSystem.Id, DiagnosticSeverity.Error).WithLocation(0));
		await test.RunAsync();
	}

	[Fact]
	public void SystemsRegisteredAfterACallThatDependsOnAnotherGeneratorAreNotReportedAsUnregistered()
	{
		// The generator cannot see other generators' output (a System.Text.Json context here), so the call to
		// AddEcsSerialization does not bind in its compilation and neither does the rest of the chain. The AddSystem<T>()
		// calls still count as registrations; the missing member is the compiler's to report, not ION009.
		var result = GeneratorHarness.Run("""
			using Ion;
			using Ion.Extensions.Ecs;

			public sealed class BallSystem { [Update] public void Run(GameTime dt) { } }

			public static class App
			{
				public static void Run()
				{
					var builder = IonApplication.CreateBuilder();
					builder.AddEcs().AddEcsSerialization(components => components.AddUnmanaged("Velocity", GameJson.Default.Velocity)).AddSystem<BallSystem>();
					var app = builder.Build();
					app.UseSystem<BallSystem>();
					app.Build();
				}
			}
			""");

		Assert.Contains(result.Input.GetDiagnostics(), d => d.Id == "CS0103");
		Assert.DoesNotContain(result.Diagnostics, d => d.Id == Diagnostics.UnregisteredSystem.Id);
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
	internal sealed class GeneratorTest : CSharpSourceGeneratorTest<ScheduleGenerator, DefaultVerifier>
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
