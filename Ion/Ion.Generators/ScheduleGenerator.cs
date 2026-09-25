using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ion.Generators;

/// <summary>
/// The Ion schedule generator. It intercepts every <c>UseSystem</c>, function step (<c>app.Update(...)</c>), legacy
/// middleware (<c>app.UseUpdate(...)</c>), <c>UseScene</c> and <c>Build()</c>/<c>Run()</c> call it can see, and emits:
/// <list type="bullet">
/// <item>pre-bound registrations: each system described at compile time (its steps, scopes, constraints and diagnostics)
/// with delegates that bind its methods directly, so the runtime plans and binds it without reflection;</item>
/// <item>for each application (the registrations before a <c>Build()</c>/<c>Run()</c> call) and each scene, a
/// <c>GeneratedSchedule</c>: one method per stage that calls every step directly in plan order inside
/// <c>try/finally</c> scopes, with the systems resolved once in its constructor, used when the runtime registrations
/// match what the generator saw;</item>
/// <item>a <c>ScheduleRegistrations</c> summary of every method that takes a builder, so that applications calling into
/// this assembly see its registrations too;</item>
/// <item>the schedule diagnostics (ION001 to ION013) as compiler diagnostics.</item>
/// </list>
/// Everything it cannot see stays on the runtime (reflection) path, which remains the reference behaviour.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ScheduleGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// Cheap syntactic pre-filter: only compilations that call a registration API (or declare a method taking a builder)
		// are analyzed.
		var relevant = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (node, _) => node is InvocationExpressionSyntax invocation && RegistrationAnalyzer.IsCandidateName(invocation)
					|| node is ParameterSyntax { Type: { } type } && type.ToString() is var name && (name.EndsWith("IIonApplication", StringComparison.Ordinal) || name.EndsWith("ISceneBuilder", StringComparison.Ordinal) || name.EndsWith("IScheduleBuilder", StringComparison.Ordinal) || name.EndsWith("IonApplication", StringComparison.Ordinal)),
				static (_, _) => true)
			.Collect()
			.Select(static (items, _) => items.Length > 0);

		context.RegisterSourceOutput(context.CompilationProvider.Combine(relevant), static (spc, source) =>
		{
			if (!source.Right) return;
			Execute(source.Left, spc);
		});
	}

	private static void Execute(Compilation compilation, SourceProductionContext context)
	{
		var known = KnownSymbols.TryCreate(compilation);
		if (known is null) return;

		var reported = new HashSet<(string, Location?, string)>();
		void Report(DiagnosticDescriptor descriptor, Location? location, string message)
		{
			if (reported.Add((descriptor.Id, location, message))) context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
		}

		var namespaceEnabled = InterceptableLocations.IsNamespaceEnabled(compilation);
		var canIntercept = InterceptableLocations.IsSupported && namespaceEnabled;

		var systems = new SystemAnalyzer(known);
		var analyzer = new RegistrationAnalyzer(known, systems, canIntercept);
		analyzer.FindCalls(context.CancellationToken);

		// Per-system diagnostics, where the problem is declared (or at the registration for systems of other assemblies).
		foreach (var (system, location) in analyzer.SystemUses)
		{
			foreach (var diagnostic in system.Diagnostics)
			{
				Report(Diagnostics.ForCode(diagnostic.Code), diagnostic.Location ?? location, diagnostic.Message);
			}
		}

		if (!canIntercept)
		{
			if (analyzer.HasRegistrations)
			{
				var first = analyzer.SystemUses.Select(u => u.Location).FirstOrDefault();
				Report(Diagnostics.InterceptorsDisabled, first, namespaceEnabled
					? "The compiler does not support interceptor locations (Roslyn 4.12 or later, .NET SDK 9.0.200 or later, is required); the Ion schedule is bound by reflection at run time."
					: "Interceptors are not enabled for the Ion.Generated namespace; add <InterceptorsNamespaces>$(InterceptorsNamespaces);Ion.Generated</InterceptorsNamespaces> to the project (the Ion package does this). The Ion schedule is bound by reflection at run time.");
			}

			return;
		}

		new DependencyChecks(known, analyzer, Report).Run(context.CancellationToken);

		var planner = new CandidatePlanner(systems, Report);
		var candidates = new List<ScheduleCandidate>();
		var calls = analyzer.Calls.OrderBy(c => c.Index).ToList();

		foreach (var call in calls)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			List<RegistrationOp>? ops;
			ScheduleCandidate candidate;
			var span = call.Invocation.GetLocation().GetLineSpan();
			var where = $"{System.IO.Path.GetFileName(span.Path)}:{(span.StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture)}";

			switch (call.Kind)
			{
				case CallKind.Root:
					ops = analyzer.RootProgram(call);
					candidate = new ScheduleCandidate { Name = $"the application built at {where}", ScheduleName = "root", IsRoot = true, Location = call.Invocation.GetLocation() };
					break;

				case CallKind.UseScene:
					ops = analyzer.SceneProgram(call);
					var name = call.SceneId is int id ? "Scene " + id.ToString(CultureInfo.InvariantCulture) : "Scene";
					candidate = new ScheduleCandidate { Name = $"{name} registered at {where}", ScheduleName = name, IsRoot = false, Location = call.Invocation.GetLocation() };
					break;

				default:
					continue;
			}

			if (ops is null) continue;

			planner.Plan(candidate, ops);
			call.Candidate = candidate;
			if (candidate.Invalid is null)
			{
				candidate.ClassIndex = candidates.Count;
				candidates.Add(candidate);
			}
		}

		var summaries = analyzer.Summaries(context.CancellationToken);
		if (calls.Count == 0 && summaries.Count == 0) return;

		var source = new Emitter(known, analyzer).Emit(calls, candidates, summaries);
		context.AddSource("IonSchedule.g.cs", source);
	}
}
