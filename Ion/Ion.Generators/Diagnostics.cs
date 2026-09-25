using Microsoft.CodeAnalysis;

namespace Ion.Generators;

/// <summary>
/// The schedule diagnostics, with the same ids, severities and messages as the runtime (<c>Ion.ScheduleDiagnosticCodes</c>).
/// Each descriptor's message is the runtime message verbatim (<c>{0}</c>), so a problem reads the same in the IDE and in
/// an <c>IonScheduleException</c>.
/// </summary>
internal static class Diagnostics
{
	private const string Category = "Ion.Schedule";
	private const string HelpLink = "https://github.com/jimbuck/Ion/blob/main/docs/plans/2026-09-engine-review-and-roadmap.md#43-the-loop-cycle-stages-and-systems-redesign-analysis-stage-2";

	public static readonly DiagnosticDescriptor UnknownStage = Error("ION001", "Unknown stage");
	public static readonly DiagnosticDescriptor OrderingCycle = Error("ION002", "Ordering cycle");
	public static readonly DiagnosticDescriptor UnpairedScope = Error("ION003", "Scope without a matching end");
	public static readonly DiagnosticDescriptor UnreachableStep = Error("ION004", "Step can never run");
	public static readonly DiagnosticDescriptor AsyncStep = Error("ION005", "Async step");
	public static readonly DiagnosticDescriptor ScopedServiceInRoot = Error("ION006", "Scoped service in the root schedule");
	public static readonly DiagnosticDescriptor InvalidSignature = Error("ION007", "Unsupported step signature");
	public static readonly DiagnosticDescriptor UnresolvableParameter = Error("ION008", "Unregistered step parameter");
	public static readonly DiagnosticDescriptor UnregisteredSystem = Error("ION009", "Unregistered system");
	public static readonly DiagnosticDescriptor LegacyMiddleware = Warning("ION010", "Legacy middleware step");
	public static readonly DiagnosticDescriptor AmbiguousScope = Error("ION011", "Ambiguous scope");
	public static readonly DiagnosticDescriptor UnmatchedConstraint = Warning("ION012", "Constraint on a system that is not in the schedule");
	public static readonly DiagnosticDescriptor SystemWithoutSteps = Warning("ION013", "System without steps");

	/// <summary>The generator cannot emit interceptors (the namespace is not enabled, or the compiler is too old).</summary>
	public static readonly DiagnosticDescriptor InterceptorsDisabled = new(
		"ION014",
		"Ion schedule generator is disabled",
		"{0}",
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "The Ion source generator needs C# interceptors in the Ion.Generated namespace. Without them the schedule is bound by reflection at run time.",
		helpLinkUri: HelpLink);

	public static DiagnosticDescriptor ForCode(string code) => code switch
	{
		"ION001" => UnknownStage,
		"ION002" => OrderingCycle,
		"ION003" => UnpairedScope,
		"ION004" => UnreachableStep,
		"ION005" => AsyncStep,
		"ION006" => ScopedServiceInRoot,
		"ION007" => InvalidSignature,
		"ION008" => UnresolvableParameter,
		"ION009" => UnregisteredSystem,
		"ION010" => LegacyMiddleware,
		"ION011" => AmbiguousScope,
		"ION012" => UnmatchedConstraint,
		"ION013" => SystemWithoutSteps,
		_ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown schedule diagnostic."),
	};

	private static DiagnosticDescriptor Error(string id, string title) => new(id, title, "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

	private static DiagnosticDescriptor Warning(string id, string title) => new(id, title, "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);
}
