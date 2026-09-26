using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

namespace Ion.Generators.Tests;

/// <summary>
/// Metrics v2 in the generated schedule: every step and scope is bracketed with profiler timestamps behind the
/// <c>Ion.Metrics.Profiling</c> feature switch, and not at all when the project sets <c>IonMetricsProfiling=false</c>.
/// </summary>
public partial class ProfilingTests
{
	private const string Systems = """
		public sealed class Frame
		{
			[Begin(Stage.Render, Order = -900)] public void Begin(GameTime dt) => Log.Calls.Add("frame begin");
			[End(Stage.Render)] public void End() => Log.Calls.Add("frame end");
		}

		public sealed class Sprites
		{
			[Render] public void Draw(GameTime dt) => Log.Calls.Add("draw");
			[Update] public void Tick(GameTime dt) => Log.Calls.Add("tick");
			[Update(Order = 5)] public void Late(GameTime dt) => Log.Calls.Add("late");
		}

		public static class App
		{
			public static object Run()
			{
				var profiler = new FrameProfiler(4, 64) { IsActive = true };
				var builder = IonApplication.CreateBuilder(new[] { "--Ion:Headless=true" });
				builder.Services.AddLogging(logging => logging.ClearProviders());
				builder.Services.AddSingleton(profiler);
				builder.Services.AddSingleton<Frame>().AddSingleton<Sprites>();
				using var app = builder.Build();
				app.UseSystem<Sprites>().UseSystem<Frame>();
				var loop = app.Build();
				loop.Initialize();
				loop.Step(new GameTime());
				loop.Shutdown();

				// Init, the frame, Destroy: the frame is the second most recent.
				var names = new List<string>();
				foreach (var span in profiler.GetFrame(1).Spans) names.Add(span.Id.Name);
				return (loop.Schedule!.IsGenerated, string.Join(", ", names));
			}
		}
		""";

	private static readonly Dictionary<string, string> ProfilingOff = new() { ["IonMetricsProfiling"] = "false" };

	[Fact]
	public void BracketsEveryStepAndScopeBehindTheFeatureSwitch()
	{
		var result = GeneratorHarness.Run(GoldenScenarios.Preamble + Systems);
		result.AssertCompiles();

		// Three steps and one scope: one Begin and one End each in the bracketed copy of their stage, guarded by the feature switch.
		Assert.Equal(4, BeginCalls().Matches(result.Generated).Count);
		Assert.Equal(4, EndCalls().Matches(result.Generated).Count);
		// One check per stage (Update and Render) choosing the bracketed copy, and one per bracket.
		Assert.Equal(10, Regex.Matches(result.Generated, Regex.Escape("global::Ion.FrameProfiler.IsProfilingEnabled")).Count);
		Assert.Equal(2, Regex.Matches(result.Generated, Regex.Escape("&& _prof.IsActive)")).Count);
		Assert.Contains("_prof = context.Profiler;", result.Generated);
		Assert.Contains("global::Ion.MetricsIds.Register(\"Sprites.Tick\")", result.Generated);
		GeneratorHarness.AssertGolden("Profiling", result.Generated);
	}

	[Theory]
	[InlineData("false")]
	[InlineData("False")]
	public void EmitsNoBracketsWhenProfilingIsOff(string value)
	{
		var result = GeneratorHarness.Run(GoldenScenarios.Preamble + Systems, buildProperties: new Dictionary<string, string> { ["IonMetricsProfiling"] = value });
		result.AssertCompiles();

		Assert.DoesNotContain("FrameProfiler", result.Generated);
		Assert.DoesNotContain("_prof", result.Generated);
		Assert.DoesNotContain("MetricsIds", result.Generated);
		GeneratorHarness.AssertGolden("ProfilingOff", result.Generated);
	}

	[Fact]
	public void TheGeneratedAndRuntimeSchedulesRecordTheSameSpans()
	{
		var source = GoldenScenarios.Preamble + Systems;
		var generated = Run(GeneratorHarness.Run(source, "ProfiledGenerated").Output);
		var reflection = Run(GeneratorHarness.Compile(source, "ProfiledReflection"));
		var unbracketed = Run(GeneratorHarness.Run(source, "ProfiledOff", buildProperties: ProfilingOff).Output);

		Assert.True(generated.IsGenerated);
		Assert.False(reflection.IsGenerated);
		Assert.True(unbracketed.IsGenerated);

		// Spans end innermost first; the loop adds one per stage.
		const string expected = "First, FixedUpdate, Sprites.Tick, Sprites.Late, Update, Sprites.Draw, Frame.Begin, Render, Last";
		Assert.Equal(expected, reflection.Spans);
		Assert.Equal(expected, generated.Spans);

		// Without brackets only the loop's stage spans remain.
		Assert.Equal("First, FixedUpdate, Update, Render, Last", unbracketed.Spans);
	}

	private static (bool IsGenerated, string Spans) Run(Compilation compilation)
	{
		var assembly = GeneratorHarness.Load(compilation);
		try
		{
			return ((bool, string))assembly.GetType("App")!.GetMethod("Run")!.Invoke(null, null)!;
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}

	[GeneratedRegex(@"_prof\.Begin\(")]
	private static partial Regex BeginCalls();

	[GeneratedRegex(@"_prof\.End\(")]
	private static partial Regex EndCalls();
}
