using Microsoft.CodeAnalysis;

namespace Ion.Extensions.Networking.Generators;

/// <summary>The networking diagnostics (ION2xx).</summary>
internal static class NetworkDiagnostics
{
	private const string Category = "Ion.Networking";
	private const string HelpLink = "https://github.com/jimbuck/Ion/blob/main/docs/design/ion-networking.md";

	public static readonly DiagnosticDescriptor NotUnmanaged = Create("ION201", "Network type is not unmanaged",
		"'{0}' is a {1} but is not unmanaged: replicated components and network messages cannot hold references (use FixedString32/64/128 for text)",
		DiagnosticSeverity.Error);

	public static readonly DiagnosticDescriptor PredictedWithoutOwner = Create("ION202", "Predicted component without owner authority",
		"'{0}' is [Predicted] but its authority is Server: a predicted component must be [Replicated(Authority = Authority.Owner)]",
		DiagnosticSeverity.Error);

	public static readonly DiagnosticDescriptor TooLarge = Create("ION203", "Replicated component too large",
		"'{0}' serializes to up to {1} bytes, more than the {2} bytes one entity's component may use in a snapshot part",
		DiagnosticSeverity.Error);

	public static readonly DiagnosticDescriptor NotUsedAsComponent = Create("ION204", "Replicated type not used as an ECS component",
		"'{0}' is [Replicated] but is never used as an ECS component in this project (World.Create/Add/Set/Get, Commands, [Query] parameters): it will not be replicated",
		DiagnosticSeverity.Warning);

	public static readonly DiagnosticDescriptor UnsupportedMember = Create("ION205", "Member cannot be serialized",
		"Member '{1}' of '{0}' cannot be serialized: {2}",
		DiagnosticSeverity.Error);

	public static readonly DiagnosticDescriptor ReaderInStage = Create("ION206", "Network reader created in a stage method",
		"NetworkReader<{0}> is created in the stage method '{1}': it starts from the oldest visible message on every call and reads messages again; create it once in the constructor or a field initializer",
		DiagnosticSeverity.Warning);

	public static readonly DiagnosticDescriptor SentNeverRead = Create("ION207", "Network message sent but never read",
		"Network message '{0}' is sent but never read (no NetworkReader<{0}> or prediction registration)",
		DiagnosticSeverity.Warning);

	public static readonly DiagnosticDescriptor ReadNeverSent = Create("ION208", "Network message read but never sent",
		"Network message '{0}' is read but never sent",
		DiagnosticSeverity.Warning);

	public static readonly DiagnosticDescriptor MissingReplicated = Create("ION209", "Predicted or Interpolated without Replicated",
		"'{0}' is [{1}] but not [Replicated]",
		DiagnosticSeverity.Error);

	public static readonly DiagnosticDescriptor EntityMember = Create("ION210", "Entity handle in a network type",
		"Member '{1}' of '{0}' is an Entity, whose handle differs between processes: it is not sent (use NetworkId to refer to another networked entity)",
		DiagnosticSeverity.Warning);

	private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity) =>
		new(id, title, message, Category, severity, isEnabledByDefault: true, helpLinkUri: HelpLink);
}
