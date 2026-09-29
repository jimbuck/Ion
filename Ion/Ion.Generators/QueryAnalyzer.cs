using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Ion.Generators;

internal enum QueryParameterKind
{
	Component,
	Entity,
	Delta,
	Time,
	Commands,
	World,
}

internal sealed record QueryParameter(QueryParameterKind Kind, ITypeSymbol Type, RefKind RefKind, string Name);

/// <summary>A valid <c>[Query]</c> method: what its generated loop iterates and passes.</summary>
internal sealed class QueryInfo
{
	public IMethodSymbol Method { get; init; } = null!;
	public List<QueryParameter> Parameters { get; } = [];
	public List<ITypeSymbol> Components { get; } = [];
	public List<ITypeSymbol> All { get; } = [];
	public List<ITypeSymbol> Any { get; } = [];
	public List<ITypeSymbol> None { get; } = [];
	public bool UsesCommands { get; set; }

	/// <summary><c>[Query(Unchecked = true)]</c>: no structural-change check after each entity.</summary>
	public bool Unchecked { get; set; }

	/// <summary>The name of the generated method that runs the query (public, hidden from IntelliSense).</summary>
	public string CompanionName => QueryAnalyzer.CompanionName(Method.Name);
}

/// <summary>
/// Validates <c>[Query]</c> methods (ION301 to ION307) and describes the valid ones for <see cref="QueryEmitter"/> and
/// <see cref="SystemAnalyzer"/>. The rules match the runtime binder (<c>QueryMethod.Describe</c> in the ECS abstractions).
/// </summary>
internal sealed class QueryAnalyzer(KnownSymbols known)
{
	private readonly Dictionary<IMethodSymbol, (QueryInfo? Info, List<Diagnostic> Diagnostics)> _cache = new(SymbolEqualityComparer.Default);

	public static string CompanionName(string method) => "__IonQuery_" + method;

	/// <summary>Whether <paramref name="method"/> has <c>[Query]</c>.</summary>
	public bool IsQuery(IMethodSymbol method) =>
		known.QueryAttribute is { } query && method.GetAttributes().Any(a => KnownSymbols.Is(a.AttributeClass, query));

	/// <summary>The query of <paramref name="method"/>, or null when it has errors (see <see cref="Diagnostics"/>).</summary>
	public QueryInfo? Analyze(IMethodSymbol method, CancellationToken cancellationToken = default) => Get(method, cancellationToken).Info;

	/// <summary>The problems of <paramref name="method"/>.</summary>
	public IReadOnlyList<Diagnostic> Diagnostics(IMethodSymbol method, CancellationToken cancellationToken = default) => Get(method, cancellationToken).Diagnostics;

	private (QueryInfo? Info, List<Diagnostic> Diagnostics) Get(IMethodSymbol method, CancellationToken cancellationToken)
	{
		method = method.OriginalDefinition;
		if (_cache.TryGetValue(method, out var cached)) return cached;
		var diagnostics = new List<Diagnostic>();
		var info = Describe(method, diagnostics, cancellationToken);
		_cache[method] = (diagnostics.Count == 0 ? info : null, diagnostics);
		return _cache[method];
	}

	private QueryInfo? Describe(IMethodSymbol method, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
	{
		var location = SystemAnalyzer.SourceLocation(method);
		var name = method.ContainingType.Name + "." + method.Name;
		void Report(DiagnosticDescriptor descriptor, Location? at, string message) => diagnostics.Add(Diagnostic.Create(descriptor, at ?? location, message));

		for (var type = method.ContainingType; type is not null; type = type.ContainingType)
		{
			if (!IsPartial(type))
			{
				Report(Generators.Diagnostics.QueryNotPartial, location,
					$"'{name}' has [Query] but {(SymbolEqualityComparer.Default.Equals(type, method.ContainingType) ? "its class" : "its containing type")} '{type.Name}' is not partial, so the generator cannot add the query loop. Declare '{type.Name}' partial.");
				break;
			}
		}

		if (!method.ReturnsVoid) Report(Generators.Diagnostics.QueryUnsupported, location, $"'{name}' has [Query] but returns {method.ReturnType.Name}; a query method returns void.");
		if (method.IsGenericMethod) Report(Generators.Diagnostics.QueryUnsupported, location, $"'{name}' has [Query] but is generic; a query method cannot be generic.");
		if (method.IsAsync) Report(Generators.Diagnostics.QueryUnsupported, location, $"'{name}' has [Query] but is async; query methods are synchronous.");

		var hasStage = method.GetAttributes().Any(a => a.AttributeClass is { } c && known.StageAttributes.ContainsKey(c));
		if (!hasStage) Report(Generators.Diagnostics.QueryWithoutStage, location, $"'{name}' has [Query] but no stage attribute ([Update], [FixedUpdate], ...), so it never runs.");

		var info = new QueryInfo
		{
			Method = method,
			Unchecked = method.GetAttributes().Any(a => KnownSymbols.Is(a.AttributeClass, known.QueryAttribute) && a.NamedArguments.Any(n => n.Key == "Unchecked" && n.Value.Value is true)),
		};
		foreach (var parameter in method.Parameters)
		{
			var type = parameter.Type;
			var at = SystemAnalyzer.SourceLocation(parameter) ?? location;

			if (parameter.RefKind == RefKind.Out)
			{
				Report(Generators.Diagnostics.QueryUnsupported, at, $"Parameter '{parameter.Name}' of '{name}' is an out parameter; query parameters are components (ref or in), Entity, [Data] float, GameTime, Commands or World.");
				continue;
			}

			if (parameter.GetAttributes().Any(a => KnownSymbols.Is(a.AttributeClass, known.DataAttribute)))
			{
				if (type.SpecialType == SpecialType.System_Single) info.Parameters.Add(new QueryParameter(QueryParameterKind.Delta, type, parameter.RefKind, parameter.Name));
				else if (KnownSymbols.Is(type, known.GameTime)) info.Parameters.Add(new QueryParameter(QueryParameterKind.Time, type, parameter.RefKind, parameter.Name));
				else Report(Generators.Diagnostics.QueryUnsupported, at, $"Parameter '{parameter.Name}' of '{name}' is marked [Data] but is a {type.Name}; [Data] parameters are a float (the delta time) or a GameTime.");
				continue;
			}

			if (KnownSymbols.Is(type, known.ArchEntity) && parameter.RefKind is RefKind.None or RefKind.In)
			{
				info.Parameters.Add(new QueryParameter(QueryParameterKind.Entity, type, parameter.RefKind, parameter.Name));
				continue;
			}

			if (parameter.RefKind == RefKind.None)
			{
				if (KnownSymbols.Is(type, known.GameTime)) info.Parameters.Add(new QueryParameter(QueryParameterKind.Time, type, parameter.RefKind, parameter.Name));
				else if (KnownSymbols.Is(type, known.Commands))
				{
					info.Parameters.Add(new QueryParameter(QueryParameterKind.Commands, type, parameter.RefKind, parameter.Name));
					info.UsesCommands = true;
				}
				else if (KnownSymbols.Is(type, known.ArchWorld)) info.Parameters.Add(new QueryParameter(QueryParameterKind.World, type, parameter.RefKind, parameter.Name));
				else if (type.IsValueType) Report(Generators.Diagnostics.QueryParameterNotByRef, at, $"Component parameter '{parameter.Name}' ({type.Name}) of '{name}' is passed by value, so changes would be lost and the component copied; pass it by ref (or in, to read it).");
				else Report(Generators.Diagnostics.QueryUnsupported, at, $"Parameter '{parameter.Name}' ({type.Name}) of '{name}' is not a component (ref or in), Entity, [Data] float, GameTime, Commands or World; inject services through the system's constructor.");
				continue;
			}

			if (!type.IsValueType || type.TypeKind == TypeKind.TypeParameter)
			{
				Report(Generators.Diagnostics.QueryComponentNotStruct, at, $"Parameter '{parameter.Name}' of '{name}' is a {type.Name}, which is not a struct; query components are structs (read a class component through the Entity).");
				continue;
			}

			if (info.Components.Contains(type, SymbolEqualityComparer.Default))
			{
				Report(Generators.Diagnostics.QueryUnsupported, at, $"Component {type.Name} is a parameter of '{name}' more than once.");
				continue;
			}

			info.Parameters.Add(new QueryParameter(QueryParameterKind.Component, type, parameter.RefKind, parameter.Name));
			info.Components.Add(type);
		}

		AddDistinct(info.All, info.Components);
		foreach (var attribute in method.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass || !InheritsFrom(attributeClass, known.QueryFilterAttribute)) continue;
			var list = attributeClass.Name switch
			{
				"AllAttribute" => info.All,
				"AnyAttribute" => info.Any,
				"NoneAttribute" => info.None,
				_ => null,
			};

			if (list is null) continue;
			foreach (var argument in attributeClass.TypeArguments)
			{
				if (!argument.IsValueType)
				{
					Report(Generators.Diagnostics.QueryComponentNotStruct, AttributeLocation(attribute) ?? location, $"{attributeClass.Name.Replace("Attribute", "")}<{argument.Name}> on '{name}' names a {argument.Name}, which is not a struct; query components are structs.");
					continue;
				}

				AddDistinct(list, [argument]);
			}
		}

		foreach (var overlap in info.All.Where(t => info.None.Contains(t, SymbolEqualityComparer.Default)))
		{
			Report(Generators.Diagnostics.QueryOverlap, location, $"Component {overlap.Name} of '{name}' is both required (a parameter or All) and excluded (None), so the query matches no entity.");
		}

		if (!info.UsesCommands) CheckStructuralChanges(method, name, Report, cancellationToken);
		return info;
	}

	/// <summary>ION305: direct structural changes in the body of a query method that does not take <c>Commands</c>.</summary>
	private void CheckStructuralChanges(IMethodSymbol method, string name, Action<DiagnosticDescriptor, Location?, string> report, CancellationToken cancellationToken)
	{
		foreach (var reference in method.DeclaringSyntaxReferences)
		{
			if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration) continue;
			var model = known.Compilation.GetSemanticModel(declaration.SyntaxTree);
			foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation) continue;
				var target = operation.TargetMethod;
				var type = target.ContainingType;
				var structural =
					(KnownSymbols.Is(type, known.ArchWorld) && target.Name is "Create" or "Destroy" or "Add" or "Remove" or "AddRange" or "RemoveRange" or "Clear" or "TrimExcess" or "Dispose")
					|| (KnownSymbols.Is(type, known.ArchEntityExtensions) && target.Name is "Add" or "Remove" or "AddRange" or "RemoveRange")
					|| (KnownSymbols.Is(type, known.HierarchyExtensions) && target.Name is "SetParent" or "RemoveParent" or "DestroyRecursive")
					|| (KnownSymbols.Is(type, known.ArchCommandBuffer) && target.Name == "Playback")
					|| (KnownSymbols.Is(type, known.Commands) && target.Name == "Flush");
				if (!structural) continue;

				report(Generators.Diagnostics.QueryStructuralChange, invocation.GetLocation(),
					$"'{name}' calls {type!.Name}.{target.Name} inside its query, a structural change that invalidates the chunks being iterated. Add a Commands parameter and record it there (commands.{CommandFor(target.Name)}); the ECS module plays commands back at the end of the stage.");
			}
		}
	}

	private static string CommandFor(string method) => method switch
	{
		"Create" => "Create",
		"Destroy" or "DestroyRecursive" => method,
		"Add" or "AddRange" => "Add",
		"Remove" or "RemoveRange" => "Remove",
		"SetParent" or "RemoveParent" => method,
		_ => "Add/Remove/Create/Destroy",
	};

	private static void AddDistinct(List<ITypeSymbol> list, IEnumerable<ITypeSymbol> types)
	{
		foreach (var type in types)
		{
			if (!list.Contains(type, SymbolEqualityComparer.Default)) list.Add(type);
		}
	}

	private static bool IsPartial(INamedTypeSymbol type)
	{
		if (type.DeclaringSyntaxReferences.Length == 0) return false;
		foreach (var reference in type.DeclaringSyntaxReferences)
		{
			if (reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(m => m.ValueText == "partial")) return true;
		}

		return false;
	}

	private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
	{
		if (baseType is null) return false;
		for (var t = type.BaseType; t is not null; t = t.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
		}

		return false;
	}

	private static Location? AttributeLocation(AttributeData attribute) => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
}
