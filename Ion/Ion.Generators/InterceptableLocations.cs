using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ion.Generators;

/// <summary>
/// Bridges to <c>CSharpExtensions.GetInterceptableLocation</c> (Roslyn 4.12+, the <c>[InterceptsLocation(version, data)]</c>
/// form) while the generator is compiled against Roslyn 4.4. When the running compiler does not have it, interception is
/// unavailable and the runtime binds the schedule by reflection as before.
/// </summary>
internal static class InterceptableLocations
{
	private static readonly MethodInfo? GetInterceptableLocation = typeof(Microsoft.CodeAnalysis.CSharp.CSharpExtensions).GetMethod(
		"GetInterceptableLocation",
		BindingFlags.Public | BindingFlags.Static,
		binder: null,
		types: [typeof(SemanticModel), typeof(InvocationExpressionSyntax), typeof(CancellationToken)],
		modifiers: null);

	/// <summary>Whether the running compiler can locate interceptable calls.</summary>
	public static bool IsSupported => GetInterceptableLocation is not null;

	/// <summary>
	/// The <c>[InterceptsLocation(version, "data")]</c> attribute (without brackets or namespace) for
	/// <paramref name="invocation"/>, or null.
	/// </summary>
	public static string? AttributeFor(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
	{
		if (GetInterceptableLocation?.Invoke(null, [model, invocation, cancellationToken]) is not { } location) return null;

		var type = location.GetType();
		if (type.GetProperty("Version")?.GetValue(location) is not int version || type.GetProperty("Data")?.GetValue(location) is not string data) return null;

		return $"global::System.Runtime.CompilerServices.InterceptsLocation({version.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"{data}\")";
	}

	/// <summary>Whether the compilation enables interceptors in the <c>Ion.Generated</c> namespace.</summary>
	public static bool IsNamespaceEnabled(Compilation compilation)
	{
		if (compilation.SyntaxTrees.FirstOrDefault()?.Options is not CSharpParseOptions options) return false;

		foreach (var feature in new[] { "InterceptorsNamespaces", "InterceptorsPreviewNamespaces" })
		{
			if (options.Features.TryGetValue(feature, out var value) && value.Split(';').Any(n => n.Trim() == "Ion.Generated")) return true;
		}

		return false;
	}
}
