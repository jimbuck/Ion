namespace Ion;

/// <summary>
/// The diagnostic codes reported when a schedule is planned or bound. Errors are thrown as
/// <see cref="IonScheduleException"/> from <c>IonApplication.Build()</c>; warnings are logged (category
/// <c>Ion.Schedule</c>) and kept on <see cref="SchedulePlan.Diagnostics"/>. The source generator reports the same codes at
/// compile time.
/// </summary>
public static class ScheduleDiagnosticCodes
{
	/// <summary>Error: a stage or scope attribute names a value that is not a <see cref="Stage"/>.</summary>
	public const string UnknownStage = "ION001";

	/// <summary>Error: the <c>[Before&lt;T&gt;]</c>/<c>[After&lt;T&gt;]</c> constraints of a stage form a cycle.</summary>
	public const string OrderingCycle = "ION002";

	/// <summary>Error: a <c>[Begin]</c> has no matching <c>[End]</c> on the same system, or the reverse.</summary>
	public const string UnpairedScope = "ION003";

	/// <summary>Error: a step can never run (a stage or scope attribute on a method that is not public, or a registration on a schedule that was already built).</summary>
	public const string UnreachableStep = "ION004";

	/// <summary>Error: a stage method is <c>async</c> or returns a <c>Task</c>/<c>ValueTask</c>; steps are synchronous.</summary>
	public const string AsyncStep = "ION005";

	/// <summary>Error: a system of the root schedule is registered as scoped, or a root step injects a scoped service.</summary>
	public const string ScopedServiceInRoot = "ION006";

	/// <summary>Error: a stage or scope method has a signature that is not supported.</summary>
	public const string InvalidSignature = "ION007";

	/// <summary>Error: a service injected into a step parameter is not registered.</summary>
	public const string UnresolvableParameter = "ION008";

	/// <summary>Error: a system type passed to <c>UseSystem</c> is not registered in the service collection.</summary>
	public const string UnregisteredSystem = "ION009";

	/// <summary>Warning: a step uses the legacy middleware form (<c>GameLoopDelegate next</c>); rewrite it as a leaf step or a scope.</summary>
	public const string LegacyMiddleware = "ION010";

	/// <summary>Error: a system has two unnamed scopes in one stage, or a <c>[End]</c> order that differs from its <c>[Begin]</c>.</summary>
	public const string AmbiguousScope = "ION011";

	/// <summary>Warning: a <c>[Before&lt;T&gt;]</c>/<c>[After&lt;T&gt;]</c> constraint names a system that has no step in that stage of the schedule.</summary>
	public const string UnmatchedConstraint = "ION012";

	/// <summary>Warning: a system passed to <c>UseSystem</c> has no stage or scope methods, so it never runs.</summary>
	public const string SystemWithoutSteps = "ION013";
}

/// <summary>The severity of a <see cref="ScheduleDiagnostic"/>.</summary>
public enum ScheduleDiagnosticSeverity
{
	/// <summary>Logged; the schedule still builds.</summary>
	Warning,
	/// <summary>The schedule does not build; thrown as <see cref="IonScheduleException"/>.</summary>
	Error,
}

/// <summary>
/// A problem found while planning or binding a schedule.
/// </summary>
/// <param name="Code">The diagnostic code (see <see cref="ScheduleDiagnosticCodes"/>).</param>
/// <param name="Severity">Whether the problem stops the build.</param>
/// <param name="Message">A description naming the systems and methods involved.</param>
public sealed record ScheduleDiagnostic(string Code, ScheduleDiagnosticSeverity Severity, string Message)
{
	/// <inheritdoc/>
	public override string ToString() => $"{Code} {(Severity == ScheduleDiagnosticSeverity.Error ? "error" : "warning")}: {Message}";
}

/// <summary>
/// Thrown when a schedule cannot be built. <see cref="Diagnostics"/> lists every error found (not only the first).
/// </summary>
public sealed class IonScheduleException : InvalidOperationException
{
	/// <summary>Creates the exception from the errors found.</summary>
	public IonScheduleException(IReadOnlyList<ScheduleDiagnostic> diagnostics)
		: base(FormatMessage(diagnostics))
	{
		Diagnostics = diagnostics;
	}

	/// <summary>Every error found, in the order they were found.</summary>
	public IReadOnlyList<ScheduleDiagnostic> Diagnostics { get; }

	/// <summary>The codes of <see cref="Diagnostics"/>.</summary>
	public IEnumerable<string> Codes => Diagnostics.Select(d => d.Code);

	private static string FormatMessage(IReadOnlyList<ScheduleDiagnostic> diagnostics)
	{
		if (diagnostics.Count == 1) return "The schedule is invalid: " + diagnostics[0];
		return $"The schedule is invalid ({diagnostics.Count} errors):" + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Select(d => "  " + d));
	}
}
