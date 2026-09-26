using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

using Xunit.Abstractions;

namespace Ion.Generators.Tests;

/// <summary>
/// The [Query] expansion: the generated loops (golden), the schedule that calls them, their behaviour against the
/// reflection binder the runtime uses without the generator, and the ION301 to ION307 diagnostics.
/// </summary>
public class QueryGeneratorTests(ITestOutputHelper output)
{
	private const string Preamble = """
		using System;
		using System.Collections.Generic;
		using System.Numerics;
		using Arch.Core;
		using Ion;
		using Ion.Extensions.Ecs;
		using Microsoft.Extensions.DependencyInjection;
		using Microsoft.Extensions.Logging;

		public static class Log
		{
			public static readonly List<string> Calls = new();
		}

		public record struct Velocity(Vector2 Value);
		public record struct Frozen;
		public record struct Health(int Value);

		""";

	private const string QueryApp = Preamble + """
		public sealed partial class Movement
		{
			[Update, Query, None<Frozen>]
			private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt) => transform.Position += velocity.Value * dt;

			[Update(Order = 5), Query]
			private void Damage(Entity entity, ref Health health, Commands commands)
			{
				health = new Health(health.Value - 1);
				Log.Calls.Add($"damage {entity.Id} {health.Value}");
				if (health.Value <= 0) commands.Destroy(entity);
			}

			[Last(Order = 7), Query, All<Velocity>, Any<Frozen, Transform2D>]
			private static void Report(in Transform2D transform, GameTime time, World world) => Log.Calls.Add($"at {transform.Position.X} of {world.Size}");

			[Render] public void Draw(GameTime dt) => Log.Calls.Add("draw");
		}

		public static class App
		{
			public static object Run()
			{
				var builder = IonApplication.CreateBuilder(new[] { "--Ion:Headless=true" });
				builder.Services.AddLogging(logging => logging.ClearProviders());
				builder.Services.AddEcs().AddSingleton<Movement>();
				using var app = builder.Build();
				app.UseEcs().UseSystem<Movement>();
				var loop = app.Build();
				loop.Initialize();
				var world = app.Services.GetRequiredService<World>();
				world.Create(new Transform2D(), new Velocity(new Vector2(2, 0)));
				world.Create(new Transform2D(), new Velocity(new Vector2(2, 0)), new Frozen());
				world.Create(new Health(2));
				for (var i = 0; i < 3; i++) loop.Step(new GameTime { Delta = 0.5f, Frame = (uint)i });
				loop.Shutdown();
				return (app.PrintSchedule(), loop.Schedule!.IsGenerated, string.Join(", ", Log.Calls));
			}
		}
		""";

	[Fact]
	public void GeneratesTheGoldenExpansionAndSchedule()
	{
		var result = GeneratorHarness.Run(QueryApp, "QueryApp");

		Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
		result.AssertCompiles();
		Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning));
		GeneratorHarness.AssertGolden("QueryExpansion", result.GeneratedQueries);
		GeneratorHarness.AssertGolden("QueryApp", result.Generated);
	}

	[Fact]
	public void TheGeneratedLoopsRunLikeTheReflectionBinder()
	{
		var generated = Run(GeneratorHarness.Run(QueryApp, "GeneratedQueryApp").Output);
		var reflection = Run(GeneratorHarness.Compile(QueryApp, "ReflectionQueryApp"));

		output.WriteLine(generated.Print);
		output.WriteLine(generated.Calls);

		Assert.True(generated.IsGenerated, "The generated schedule did not run.");
		Assert.False(reflection.IsGenerated);
		Assert.Equal(reflection.Print, generated.Print);
		Assert.Equal(reflection.Calls, generated.Calls);
		Assert.Equal("damage 2 1, draw, at 1 of 3, at 0 of 3, damage 2 0, draw, at 2 of 2, at 0 of 2, draw, at 3 of 2, at 0 of 2", generated.Calls);
		Assert.Contains("Movement.Move", generated.Print);
		Assert.Contains("SpriteAnimationSystem.Animate", generated.Print);
		Assert.DoesNotContain("__IonQuery", generated.Print);
	}

	[Fact]
	public void TheExpansionIsAPublicHiddenMethodWithTheOriginalsAttributes()
	{
		var assembly = GeneratorHarness.Load(GeneratorHarness.Run(QueryApp, "ExpansionShape").Output);
		var type = assembly.GetType("Movement")!;

		var move = type.GetMethod("__IonQuery_Move", BindingFlags.Public | BindingFlags.Instance)!;
		Assert.Equal(["GameTime", "World"], move.GetParameters().Select(p => p.ParameterType.Name));
		Assert.Contains(move.GetCustomAttributes(), a => a.GetType().Name == "UpdateAttribute");
		Assert.Contains(move.GetCustomAttributes(), a => a.GetType().Name == "ExpandedStepAttribute");

		var damage = type.GetMethod("__IonQuery_Damage", BindingFlags.Public | BindingFlags.Instance)!;
		Assert.Equal(["GameTime", "World", "Commands"], damage.GetParameters().Select(p => p.ParameterType.Name));

		Assert.NotNull(type.GetMethod("__IonQuery_Report", BindingFlags.Public | BindingFlags.Static));
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

	private const string DiagnosticPreamble = """
		using Arch.Core;
		using Ion;
		using Ion.Extensions.Ecs;

		public record struct Hp(int Value);
		public record struct Dead;

		""";

	public static TheoryData<string, string> DiagnosticScenarios => new()
	{
		{ "ION301", "public sealed class NotPartial { [Update, Query] private void {|#0:Tick|}(ref Hp hp) { } }" },
		{ "ION302", "public sealed partial class ByValue { [Update, Query] private void Tick(Hp {|#0:hp|}) { } }" },
		{ "ION303", "public sealed partial class NotStruct { [Update, Query] private void Tick(ref string {|#0:name|}) { } }" },
		{ "ION304", "public sealed partial class Overlap { [Update, Query, None<Hp>] private void {|#0:Tick|}(ref Hp hp) { } }" },
		{ "ION305", "public sealed partial class Direct { [Update, Query] private void Tick(Entity entity, ref Hp hp, World world) { if (hp.Value <= 0) {|#0:world.Destroy(entity)|}; } }" },
		{ "ION306", "public sealed partial class Returns { [Update, Query] private int {|#0:Tick|}(ref Hp hp) => hp.Value; }" },
		{ "ION307", "public sealed partial class Stageless { [Query] private void {|#0:Tick|}(ref Hp hp) { } }" },
	};

	[Theory]
	[MemberData(nameof(DiagnosticScenarios))]
	public async Task ReportsTheQueryDiagnostic(string id, string source)
	{
		var descriptor = typeof(Diagnostics).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Select(f => f.GetValue(null)).OfType<DiagnosticDescriptor>().Single(d => d.Id == id);

		var test = new DiagnosticTests.GeneratorTest { TestCode = DiagnosticPreamble + source };
		test.ExpectedDiagnostics.Add(new DiagnosticResult(descriptor.Id, descriptor.DefaultSeverity).WithLocation(0));
		await test.RunAsync();
	}

	[Fact]
	public void UncheckedQueriesHaveNoStructuralCheck()
	{
		var result = GeneratorHarness.Run(DiagnosticPreamble + """
			public sealed partial class Fast
			{
				[Update, Query(Unchecked = true)]
				private void Tick(ref Hp hp) => hp = new Hp(hp.Value + 1);
			}
			""");

		Assert.Empty(result.Diagnostics);
		result.AssertCompiles();
		Assert.Contains("__IonQuery_Tick", result.GeneratedQueries);
		Assert.DoesNotContain("ThrowStructuralChange", result.GeneratedQueries);
		Assert.DoesNotContain("__entity", result.GeneratedQueries);
	}

	[Fact]
	public void ACommandsParameterAllowsStructuralChangesAndNoDiagnostic()
	{
		var result = GeneratorHarness.Run(DiagnosticPreamble + """
			public sealed partial class Careful
			{
				[Update, Query, None<Dead>]
				private void Tick(Entity entity, ref Hp hp, Commands commands) { if (hp.Value <= 0) commands.Add(entity, new Dead()); }
			}
			""");

		Assert.Empty(result.Diagnostics);
		result.AssertCompiles();
		Assert.Contains("__IonQuery_Tick(global::Ion.GameTime dt, global::Arch.Core.World world, global::Ion.Extensions.Ecs.Commands commands)", result.GeneratedQueries);
	}
}
