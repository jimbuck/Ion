using Microsoft.CodeAnalysis;

namespace Ion.Extensions.Web.Generators;

/// <summary>The web routing diagnostics (ION4xx).</summary>
internal static class WebDiagnostics
{
	private const string Category = "Ion.Web";
	private const string HelpLink = "https://github.com/jimbuck/Ion/blob/main/docs/design/ion-web.md#3-routing";

	/// <summary>A route template, HTTP method or WebSocket path is not valid, or a route parameter has no method parameter.</summary>
	public static readonly DiagnosticDescriptor InvalidRoute = Create("ION401", "Invalid route", DiagnosticSeverity.Error);

	/// <summary>Two endpoints match the same method and paths (or two WebSocket endpoints share a path).</summary>
	public static readonly DiagnosticDescriptor DuplicateRoute = Create("ION402", "Duplicate route", DiagnosticSeverity.Error);

	/// <summary>The method cannot be an endpoint (static, generic, private, in a generic type, a bad WebSocket signature).</summary>
	public static readonly DiagnosticDescriptor UnsupportedMethod = Create("ION403", "Unsupported endpoint method", DiagnosticSeverity.Error);

	/// <summary>A parameter cannot be bound (its type, its modifier, a second body).</summary>
	public static readonly DiagnosticDescriptor UnsupportedParameter = Create("ION404", "Unsupported endpoint parameter", DiagnosticSeverity.Error);

	/// <summary>A body or result type needs a JSON context and has none, or the type cannot be written at all.</summary>
	public static readonly DiagnosticDescriptor UnsupportedType = Create("ION405", "Unsupported body or result type", DiagnosticSeverity.Error);

	/// <summary>The JSON context has no [JsonSerializable] for a type it must serialize.</summary>
	public static readonly DiagnosticDescriptor MissingJsonType = Create("ION406", "Type missing from the JSON context", DiagnosticSeverity.Warning);

	/// <summary>An endpoint returns a task: handlers run synchronously on the game thread.</summary>
	public static readonly DiagnosticDescriptor AsyncEndpoint = Create("ION407", "Async endpoint", DiagnosticSeverity.Error);

	public static DiagnosticDescriptor ForId(string id) => id switch
	{
		"ION401" => InvalidRoute,
		"ION402" => DuplicateRoute,
		"ION403" => UnsupportedMethod,
		"ION404" => UnsupportedParameter,
		"ION405" => UnsupportedType,
		"ION406" => MissingJsonType,
		"ION407" => AsyncEndpoint,
		_ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown web diagnostic."),
	};

	private static DiagnosticDescriptor Create(string id, string title, DiagnosticSeverity severity) =>
		new(id, title, "{0}", Category, severity, isEnabledByDefault: true, helpLinkUri: HelpLink);
}
