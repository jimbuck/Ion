using System.Reflection;

using Microsoft.CodeAnalysis;

using Xunit.Abstractions;

namespace Ion.Generators.Tests;

/// <summary>
/// Golden-output tests: the generated file for each scenario (run with <c>ION_UPDATE_GOLDEN=1</c> to rewrite them), and
/// the behaviour of the generated schedule compared with the same application bound by reflection.
/// </summary>
public class GoldenTests(ITestOutputHelper output)
{
	public static TheoryData<string> Scenarios => [.. GoldenScenarios.All.Select(s => s.Name)];

	private static string Source(string name) => GoldenScenarios.All.Single(s => s.Name == name).Source;

	[Theory]
	[MemberData(nameof(Scenarios))]
	public void GeneratesTheGoldenOutput(string name)
	{
		var result = GeneratorHarness.Run(Source(name));

		Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
		result.AssertCompiles();
		Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning));
		GeneratorHarness.AssertGolden(name, result.Generated);
	}

	[Theory]
	[MemberData(nameof(Scenarios))]
	public void RunsLikeTheReflectionBoundRuntime(string name)
	{
		var source = Source(name);
		var generated = Run(GeneratorHarness.Run(source, "Generated" + name).Output);
		var reflection = Run(GeneratorHarness.Compile(source, "Reflection" + name));

		output.WriteLine(generated.Print);
		output.WriteLine(generated.Calls);

		Assert.True(generated.IsGenerated, "The generated schedule did not run.");
		Assert.False(reflection.IsGenerated);
		Assert.Equal(reflection.Print, generated.Print);
		Assert.Equal(reflection.Calls, generated.Calls);
	}

	[Fact]
	public void FallsBackToTheRuntimeWhenARegistrationIsOnlyKnownAtRunTime()
	{
		var source = GoldenScenarios.Preamble + """
			public sealed class Known { [Update] public void Tick(GameTime dt) => Log.Calls.Add("known"); }
			public sealed class Dynamic { [Update(Order = -1)] public void Tick(GameTime dt) => Log.Calls.Add("dynamic"); }

			public static class App
			{
				public static object Run()
				{
					var builder = IonApplication.CreateBuilder(new[] { "--Ion:Headless=true" });
					builder.Services.AddLogging(logging => logging.ClearProviders());
					builder.Services.AddSingleton<Known>().AddSingleton<Dynamic>();
					using var app = builder.Build();
					app.UseSystem<Known>();
					Type plugin = Type.GetType("Dynamic")!;
					app.UseSystem(plugin);
					var loop = app.Build();
					loop.Initialize();
					loop.Step(new GameTime());
					loop.Shutdown();
					return (app.PrintSchedule(), loop.Schedule!.IsGenerated, string.Join(", ", Log.Calls));
				}
			}
			""";

		var generated = Run(GeneratorHarness.Run(source, "FallbackGenerated").Output);
		var reflection = Run(GeneratorHarness.Compile(source, "FallbackReflection"));

		Assert.False(generated.IsGenerated);
		Assert.Equal(reflection.Print, generated.Print);
		Assert.Equal("dynamic, known", generated.Calls);
	}

	private static (string Print, bool IsGenerated, string Calls) Run(Compilation compilation)
	{
		var assembly = GeneratorHarness.Load(compilation);
		try
		{
			return ((string, bool, string))assembly.GetType("App")!.GetMethod("Run")!.Invoke(null, null)!;
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}

	[Fact]
	public void LibrarySummariesLetAnApplicationSeeWhatHelpersRegister()
	{
		// A library compiled with the generator: its helper's registrations are summarized in an assembly attribute.
		var library = GeneratorHarness.Run("""
			using Ion;
			using Microsoft.Extensions.DependencyInjection;

			namespace Plugin;

			public sealed class PluginSystem
			{
				[Update(Order = -100)] public void Tick(GameTime dt) { }
			}

			internal sealed class HiddenSystem
			{
				[Render] public void Draw(GameTime dt) { }
			}

			public static class PluginExtensions
			{
				public static IServiceCollection AddPlugin(this IServiceCollection services) => services.AddSingleton<PluginSystem>().AddSingleton<HiddenSystem>();

				public static IIonApplication UsePlugin(this IIonApplication app, bool withHidden)
				{
					app.UseSystem<PluginSystem>();
					if (withHidden) app.UseSystem<HiddenSystem>();
					return app;
				}
			}
			""", "Plugin");
		library.AssertCompiles();
		GeneratorHarness.AssertGolden("LibrarySummary", library.Generated);

		using var stream = new MemoryStream();
		Assert.True(library.Output.Emit(stream).Success);
		var reference = MetadataReference.CreateFromImage(stream.ToArray());

		// An application that calls the helper: its generated schedule includes the helper's registrations, the internal
		// system through a bound delegate and the conditional one behind a guard.
		var app = GeneratorHarness.Run("using Plugin;\n" + GoldenScenarios.Preamble + """

			public sealed class Game
			{
				[Update] public void Tick(GameTime dt) { }
			}

			public static class App
			{
				public static void Run(bool hidden)
				{
					var builder = IonApplication.CreateBuilder();
					builder.Services.AddPlugin().AddSingleton<Game>();
					using var app = builder.Build();
					app.UsePlugin(hidden).UseSystem<Game>();
					app.Run();
				}
			}
			""", "PluginApp", [reference]);
		app.AssertCompiles();
		GeneratorHarness.AssertGolden("LibraryConsumer", app.Generated);
	}
}
