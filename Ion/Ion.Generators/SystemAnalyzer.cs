using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ion.Generators;

internal enum SignatureKind
{
	Invalid,
	Async,
	NoArguments,
	GameTime,
	Injected,
	LegacyVoidNext,
	LegacyFactory,
	/// <summary>A [Query] method of a source type: the step runs its generated expansion (see <see cref="QueryEmitter"/>).</summary>
	Query,
}

internal enum ItemKind
{
	Step,
	Scope,
	Middleware,
}

/// <summary>A step, scope or legacy middleware of a system (the compile-time <c>StepPlan</c> of a system).</summary>
internal sealed class SystemStep
{
	public int Stage { get; init; }
	public ItemKind Kind { get; init; }
	public int Order { get; init; }
	public IMethodSymbol Method { get; init; } = null!;
	public SignatureKind Signature { get; init; }
	public IMethodSymbol? EndMethod { get; init; }
	public SignatureKind EndSignature { get; init; }
	public string? ScopeName { get; init; }
	public int DeclarationIndex { get; init; }
	public List<ITypeSymbol> After { get; init; } = [];
	public List<ITypeSymbol> Before { get; init; } = [];

	/// <summary>The printed method name: the method's own, or for an expanded [Query] step the query method's.</summary>
	public string Name { get; init; } = "";

	/// <summary>For a [Query] method of a source type: the query (the step calls <see cref="QueryInfo.CompanionName"/>).</summary>
	public QueryInfo? Query { get; init; }

	public string KindCode => Kind switch
	{
		ItemKind.Scope => "B",
		ItemKind.Middleware => "M",
		_ => "S",
	};
}

/// <summary>A problem found in a system, with the runtime's code, severity and message.</summary>
internal sealed record SystemDiagnostic(string Code, bool IsError, string Message, Location? Location);

/// <summary>
/// A system as the runtime planner would discover it by reflection (<c>SchedulePlanner.DiscoverSystem</c>): its steps,
/// scopes and legacy middleware in discovery order, and its diagnostics in the order the runtime reports them.
/// </summary>
internal sealed class SystemInfo
{
	public INamedTypeSymbol Service { get; init; } = null!;
	public INamedTypeSymbol Implementation { get; init; } = null!;
	public string Name { get; init; } = "";
	public List<SystemStep> Steps { get; } = [];
	public List<SystemDiagnostic> Diagnostics { get; } = [];

	/// <summary>Why the system cannot be described at compile time (then its registrations stay reflection-bound), or null.</summary>
	public string? NotDescribable { get; set; }

	public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Ports <c>SchedulePlanner.DiscoverSystem</c> and <c>StepSignature</c> to Roslyn symbols, keeping every rule and message
/// identical so that a system described at compile time plans exactly like one discovered by reflection.
/// </summary>
internal sealed class SystemAnalyzer(KnownSymbols known, QueryAnalyzer queries)
{
	private readonly Dictionary<(INamedTypeSymbol, INamedTypeSymbol), SystemInfo> _cache = new(new PairComparer());

	public SystemInfo Analyze(INamedTypeSymbol service, INamedTypeSymbol implementation)
	{
		if (_cache.TryGetValue((service, implementation), out var cached)) return cached;

		var info = new SystemInfo
		{
			Service = service,
			Implementation = implementation,
			Name = implementation.MetadataName,
		};
		_cache[(service, implementation)] = info;
		Discover(info);
		return info;
	}

	private void Discover(SystemInfo info)
	{
		var type = info.Implementation;
		var classConstraints = new List<(ITypeSymbol Target, bool IsBefore)>();
		for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
		{
			foreach (var attribute in t.GetAttributes())
			{
				if (ReadOrdering(attribute, info) is { } constraint) classConstraints.Add(constraint);
			}
		}

		var methods = GetMethodsInDeclarationOrder(type, IsBound);

		// Methods a generator expanded in a referenced assembly (a [Query] method's generated loop): run in their place.
		var expanded = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (method, _) in methods)
		{
			if (ExpandedName(method) is { } original) expanded.Add(original);
		}

		var scopeKeys = new List<(int Stage, string? Name)>();
		var scopes = new Dictionary<(int Stage, string? Name), (List<ScopeMethod> Begins, List<ScopeMethod> Ends)>();
		var count = 0;

		foreach (var (method, index) in methods)
		{
			var stageAttributes = new List<(int Stage, int Order)>();
			var scopeAttributes = new List<ScopeAttributeData>();
			var methodConstraints = new List<(ITypeSymbol Target, bool IsBefore)>();

			foreach (var attribute in InheritedAttributes(method))
			{
				var attributeClass = attribute.AttributeClass;
				if (attributeClass is null || attributeClass.TypeKind == TypeKind.Error) continue;

				if (InheritsFrom(attributeClass, known.StageAttribute))
				{
					if (!known.StageAttributes.TryGetValue(attributeClass, out var stage))
					{
						info.NotDescribable ??= $"stage attribute '{attributeClass.Name}' is not one of Ion's";
						continue;
					}

					stageAttributes.Add((stage, NamedInt(attribute, "Order") ?? 0));
				}
				else if (KnownSymbols.Is(attributeClass, known.BeginAttribute) || KnownSymbols.Is(attributeClass, known.EndAttribute))
				{
					var stage = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value ? value : 0;
					var order = NamedInt(attribute, "Order");
					scopeAttributes.Add(new ScopeAttributeData(KnownSymbols.Is(attributeClass, known.BeginAttribute), stage, order ?? 0, order.HasValue, NamedString(attribute, "ScopeName")));
				}
				else if (InheritsFrom(attributeClass, known.ScopeAttribute))
				{
					info.NotDescribable ??= $"scope attribute '{attributeClass.Name}' is not one of Ion's";
				}
				else if (ReadOrdering(attribute, info) is { } constraint)
				{
					methodConstraints.Add(constraint);
				}
			}

			if (stageAttributes.Count == 0 && scopeAttributes.Count == 0) continue;

			var location = SourceLocation(method);
			var stepName = ExpandedName(method) ?? method.Name;
			var name = info.Name + "." + stepName;

			if (IsBound(method))
			{
				if (!queries.IsQuery(method))
				{
					info.NotDescribable ??= $"'{name}' is bound by an attribute the generator does not know";
					continue;
				}

				// The original of an expansion that is visible in metadata: the expansion is the step.
				if (expanded.Contains(method.Name)) continue;

				if (!method.Locations.Any(l => l.IsInSource))
				{
					info.NotDescribable ??= $"the [Query] method '{name}' of a referenced assembly was not expanded (it was compiled without Ion.Generators)";
					continue;
				}

				// Reported by the query diagnostics (ION301 to ION307); the build fails, nothing to describe.
				if (queries.Analyze(method) is not { } query || scopeAttributes.Count > 0) continue;

				var queryConstraints = classConstraints.Concat(methodConstraints).ToList();
				foreach (var (stage, order) in stageAttributes)
				{
					if (!CheckStage(info, stage, name, location)) continue;
					info.Steps.Add(new SystemStep
					{
						Stage = stage,
						Kind = ItemKind.Step,
						Order = order,
						Method = method,
						Signature = SignatureKind.Query,
						Name = method.Name,
						Query = query,
						DeclarationIndex = index,
						After = Targets(queryConstraints, before: false),
						Before = Targets(queryConstraints, before: true),
					});
					count++;
				}

				continue;
			}

			if (method.DeclaredAccessibility != Accessibility.Public)
			{
				Error(info, "ION004", $"'{name}' has a stage or scope attribute but is not public, so it can never run. Make it public.", location);
				continue;
			}

			var constraints = classConstraints.Concat(methodConstraints).ToList();
			var after = Targets(constraints, before: false);
			var before = Targets(constraints, before: true);
			var signature = Classify(method, out var reason);

			foreach (var (stage, order) in stageAttributes)
			{
				if (!CheckStage(info, stage, name, location)) continue;
				var stageName = KnownSymbols.StageName(stage);

				switch (signature)
				{
					case SignatureKind.Async:
						Error(info, "ION005", $"'{name}' in {stageName} is async or returns {RuntimeName(method.ReturnType)}. Steps are synchronous: return void, and start background work from the step instead.", location);
						continue;

					case SignatureKind.Invalid:
						Error(info, "ION007", $"'{name}' in {stageName} has an unsupported signature: {reason}. Use void {method.Name}(GameTime dt) (extra parameters are injected services).", location);
						continue;

					case SignatureKind.LegacyVoidNext:
					case SignatureKind.LegacyFactory:
						Warning(info, "ION010",
							$"'{name}' in {stageName} uses the legacy middleware form (GameLoopDelegate next). Rewrite it as a leaf step, [{stageName}] public void {method.Name}(GameTime dt), without next(dt); move code that ran after next(dt) into a later step (a higher Order or [After<T>]) or a [Begin]/[End] scope.", location);
						info.Steps.Add(new SystemStep { Stage = stage, Kind = ItemKind.Middleware, Order = order, Method = method, Signature = signature, Name = stepName, DeclarationIndex = index, After = after, Before = before });
						count++;
						continue;

					default:
						info.Steps.Add(new SystemStep { Stage = stage, Kind = ItemKind.Step, Order = order, Method = method, Signature = signature, Name = stepName, DeclarationIndex = index, After = after, Before = before });
						count++;
						continue;
				}
			}

			foreach (var attribute in scopeAttributes)
			{
				if (!CheckStage(info, attribute.Stage, name, location)) continue;
				var stageName = KnownSymbols.StageName(attribute.Stage);
				var which = attribute.IsBegin ? "Begin" : "End";

				if (signature == SignatureKind.Async)
				{
					Error(info, "ION005", $"'{name}' ({which} {stageName}) is async or returns {RuntimeName(method.ReturnType)}. Scope methods are synchronous.", location);
					continue;
				}

				if (signature is SignatureKind.Invalid or SignatureKind.LegacyFactory or SignatureKind.LegacyVoidNext)
				{
					Error(info, "ION007", $"'{name}' ({which} {stageName}) has an unsupported signature: {reason ?? "scope methods cannot take next"}. Use void {method.Name}(GameTime dt).", location);
					continue;
				}

				var key = (attribute.Stage, attribute.ScopeName);
				if (!scopes.TryGetValue(key, out var pair))
				{
					pair = ([], []);
					scopes[key] = pair;
					scopeKeys.Add(key);
				}

				(attribute.IsBegin ? pair.Begins : pair.Ends).Add(new ScopeMethod(method, attribute, index, signature, location, methodConstraints));
			}
		}

		foreach (var key in scopeKeys)
		{
			var (stage, scopeName) = key;
			var (begins, ends) = scopes[key];
			var stageName = KnownSymbols.StageName(stage);
			var label = scopeName is null ? $"{info.Name} in {stageName}" : $"{info.Name} scope '{scopeName}' in {stageName}";
			var suffix = scopeName is null ? "" : $" with ScopeName \"{scopeName}\"";
			var location = begins.Concat(ends).Select(m => m.Location).FirstOrDefault(l => l is not null);

			if (begins.Count > 1 || ends.Count > 1)
			{
				Error(info, "ION011", $"{label} has {begins.Count} [Begin] and {ends.Count} [End] methods ({string.Join(", ", begins.Concat(ends).Select(m => m.Method.Name))}). Give each pair a ScopeName.", location);
				continue;
			}

			if (begins.Count == 0)
			{
				Error(info, "ION003", $"{label}: [End] method '{ends[0].Method.Name}' has no matching [Begin({stageName})]{suffix}.", ends[0].Location);
				continue;
			}

			if (ends.Count == 0)
			{
				Error(info, "ION003", $"{label}: [Begin] method '{begins[0].Method.Name}' has no matching [End({stageName})]{suffix}.", begins[0].Location);
				continue;
			}

			var begin = begins[0];
			var end = ends[0];

			if (end.Attribute.HasOrder && end.Attribute.Order != begin.Attribute.Order)
			{
				Error(info, "ION011", $"{label}: [End] '{end.Method.Name}' has Order {end.Attribute.Order.ToString(CultureInfo.InvariantCulture)} but [Begin] '{begin.Method.Name}' has Order {begin.Attribute.Order.ToString(CultureInfo.InvariantCulture)}. A scope has one order; set the same value or omit it on [End].", end.Location);
				continue;
			}

			var constraints = classConstraints.Concat(begin.Constraints).ToList();
			info.Steps.Add(new SystemStep
			{
				Stage = stage,
				Kind = ItemKind.Scope,
				Order = begin.Attribute.Order,
				Method = begin.Method,
				Signature = begin.Signature,
				Name = begin.Method.Name,
				EndMethod = end.Method,
				EndSignature = end.Signature,
				ScopeName = scopeName,
				DeclarationIndex = begin.Index,
				After = Targets(constraints, before: false),
				Before = Targets(constraints, before: true),
			});
			count++;
		}

		if (count == 0)
		{
			Warning(info, "ION013", $"System '{info.Name}' has no public method with a stage attribute ([Init], [Update], ...) or [Begin]/[End], so it never runs.", SourceLocation(type));
		}
	}

	private bool CheckStage(SystemInfo info, int stage, string name, Location? location)
	{
		if (KnownSymbols.IsStage(stage)) return true;
		Error(info, "ION001", $"'{name}' names stage {stage.ToString(CultureInfo.InvariantCulture)}, which is not a Stage (Init, First, FixedUpdate, Update, Render, Last, Destroy).", location);
		return false;
	}

	private static void Error(SystemInfo info, string code, string message, Location? location) => info.Diagnostics.Add(new SystemDiagnostic(code, true, message, location));

	private static void Warning(SystemInfo info, string code, string message, Location? location) => info.Diagnostics.Add(new SystemDiagnostic(code, false, message, location));

	private (ITypeSymbol Target, bool IsBefore)? ReadOrdering(AttributeData attribute, SystemInfo info)
	{
		var attributeClass = attribute.AttributeClass;
		if (attributeClass is null || attributeClass.TypeKind == TypeKind.Error) return null;

		if (attributeClass.IsGenericType && attributeClass.TypeArguments.Length == 1)
		{
			if (KnownSymbols.Is(attributeClass, known.AfterAttribute)) return (attributeClass.TypeArguments[0], false);
			if (KnownSymbols.Is(attributeClass, known.BeforeAttribute)) return (attributeClass.TypeArguments[0], true);
		}

		if (InheritsFrom(attributeClass, known.OrderingAttribute)) info.NotDescribable ??= $"ordering attribute '{attributeClass.Name}' is not one of Ion's";
		return null;
	}

	/// <summary>
	/// The <c>[After&lt;T&gt;]</c>/<c>[Before&lt;T&gt;]</c> targets of a function step's method (lambda or method group), or
	/// null when it has an ordering attribute the generator does not know.
	/// </summary>
	public (List<ITypeSymbol> After, List<ITypeSymbol> Before)? OrderingOf(IMethodSymbol method)
	{
		var scratch = new SystemInfo();
		var constraints = new List<(ITypeSymbol Target, bool IsBefore)>();
		foreach (var attribute in InheritedAttributes(method))
		{
			if (ReadOrdering(attribute, scratch) is { } constraint) constraints.Add(constraint);
		}

		if (scratch.NotDescribable is not null) return null;
		return (Targets(constraints, before: false), Targets(constraints, before: true));
	}

	private static List<ITypeSymbol> Targets(List<(ITypeSymbol Target, bool IsBefore)> constraints, bool before)
	{
		var targets = new List<ITypeSymbol>();
		foreach (var (target, isBefore) in constraints)
		{
			if (isBefore == before && !targets.Contains(target, SymbolEqualityComparer.Default)) targets.Add(target);
		}

		return targets;
	}

	/// <summary>
	/// The attributes of <paramref name="method"/> and of the methods it overrides, like
	/// <c>GetCustomAttributes(inherit: true)</c>: the method's own first, then each base declaration's, skipping inherited
	/// attributes of a single-use class the method already has.
	/// </summary>
	private static IEnumerable<AttributeData> InheritedAttributes(IMethodSymbol method)
	{
		var seenSingleUse = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		var first = true;

		for (var m = method; m is not null; m = m.OverriddenMethod)
		{
			var added = new List<INamedTypeSymbol>();
			foreach (var attribute in m.GetAttributes())
			{
				if (attribute.AttributeClass is not { } attributeClass) continue;
				var allowMultiple = AllowsMultiple(attributeClass);

				if (!first)
				{
					if (!IsInherited(attributeClass)) continue;
					if (!allowMultiple && seenSingleUse.Contains(attributeClass)) continue;
				}

				if (!allowMultiple) added.Add(attributeClass);
				yield return attribute;
			}

			foreach (var a in added) seenSingleUse.Add(a);
			first = false;
		}
	}

	private static AttributeData? Usage(INamedTypeSymbol attributeClass)
	{
		for (var t = attributeClass; t is not null; t = t.BaseType)
		{
			foreach (var attribute in t.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute") return attribute;
			}
		}

		return null;
	}

	private static bool AllowsMultiple(INamedTypeSymbol attributeClass) =>
		Usage(attributeClass)?.NamedArguments.FirstOrDefault(a => a.Key == "AllowMultiple").Value.Value is true;

	private static bool IsInherited(INamedTypeSymbol attributeClass) =>
		Usage(attributeClass)?.NamedArguments.FirstOrDefault(a => a.Key == "Inherited").Value.Value is not false;

	private static int? NamedInt(AttributeData attribute, string name)
	{
		foreach (var argument in attribute.NamedArguments)
		{
			if (argument.Key == name && argument.Value.Value is int value) return value;
		}

		return null;
	}

	private static string? NamedString(AttributeData attribute, string name)
	{
		foreach (var argument in attribute.NamedArguments)
		{
			if (argument.Key == name) return argument.Value.Value as string;
		}

		return null;
	}

	private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (var t = type.BaseType; t is not null; t = t.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
		}

		return false;
	}

	/// <summary>
	/// The methods of <paramref name="type"/> that can be steps or can break ties between them, in the order reflection
	/// returns them after <c>SchedulePlanner</c>'s sort: base types first, then declaration order. Only public and
	/// protected ordinary methods are numbered (the ones every compilation can see, in source and in metadata), which keeps
	/// the relative order of steps; other methods are visited (so an attributed non-public method is reported) but numbered -1.
	/// </summary>
	/// <summary>Whether <paramref name="method"/> has a step binder attribute (<c>[Query]</c>).</summary>
	private bool IsBound(IMethodSymbol method) =>
		known.StepBinderAttribute is { } binder && method.GetAttributes().Any(a => a.AttributeClass is { } c && (KnownSymbols.Is(c, binder) || InheritsFrom(c, binder)));

	/// <summary>The method an <c>[ExpandedStep]</c> method replaces, or null.</summary>
	private string? ExpandedName(IMethodSymbol method)
	{
		if (known.ExpandedStepAttribute is not { } expanded) return null;
		foreach (var attribute in method.GetAttributes())
		{
			if (KnownSymbols.Is(attribute.AttributeClass, expanded) && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name) return name;
		}

		return null;
	}

	/// <summary>
	/// The methods in declaration order (see the overload), where the methods for which <paramref name="isBound"/> is true
	/// (<c>[Query]</c> methods) are numbered as their generated expansions are: after the other numbered methods of their
	/// declaring type, in declaration order (the expansion is emitted in a later partial part, so it comes last in
	/// metadata and reflection order). In metadata, where the expansions exist, the originals are left unnumbered.
	/// </summary>
	private static List<(IMethodSymbol Method, int Index)> GetMethodsInDeclarationOrder(INamedTypeSymbol type, Func<IMethodSymbol, bool> isBound)
	{
		var all = GetMethodsInDeclarationOrder(type);
		var result = new List<(IMethodSymbol, int)>(all.Count);
		var index = 0;
		foreach (var group in all.GroupBy(m => m.Method.ContainingType, SymbolEqualityComparer.Default))
		{
			var pending = new List<IMethodSymbol>();
			foreach (var (method, original) in group)
			{
				if (isBound(method))
				{
					if (method.Locations.Any(l => l.IsInSource)) pending.Add(method);
					else result.Add((method, -1));
					continue;
				}

				result.Add((method, original < 0 ? -1 : index++));
			}

			foreach (var method in pending) result.Add((method, index++));
		}

		return result;
	}

	private static List<(IMethodSymbol Method, int Index)> GetMethodsInDeclarationOrder(INamedTypeSymbol type)
	{
		var chain = new List<INamedTypeSymbol>();
		for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType) chain.Add(t);
		chain.Reverse();

		var overridden = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
		foreach (var t in chain)
		{
			foreach (var member in t.GetMembers())
			{
				if (member is IMethodSymbol { OverriddenMethod: { } baseMethod })
				{
					for (var m = baseMethod; m is not null; m = m.OverriddenMethod) overridden.Add(m.OriginalDefinition);
				}
			}
		}

		var result = new List<(IMethodSymbol, int)>();
		var index = 0;
		foreach (var t in chain)
		{
			var isBase = !SymbolEqualityComparer.Default.Equals(t, type);
			foreach (var member in t.GetMembers())
			{
				if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary) continue;
				if (isBase && (method.DeclaredAccessibility == Accessibility.Private || method.IsStatic)) continue;
				if (overridden.Contains(method.OriginalDefinition)) continue;

				var visible = method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
				result.Add((method, visible ? index++ : -1));
			}
		}

		return result;
	}

	public SignatureKind Classify(IMethodSymbol method, out string? reason)
	{
		reason = null;
		var returnType = method.ReturnType;
		var parameters = method.Parameters;

		if (method.IsAsync || HasAttribute(method, known.AsyncStateMachineAttribute) || IsTaskLike(returnType)) return SignatureKind.Async;

		if (method.IsGenericMethod)
		{
			reason = "it is generic";
			return SignatureKind.Invalid;
		}

		if (KnownSymbols.Is(returnType, known.GameLoopDelegate) && parameters.Length == 1 && KnownSymbols.Is(parameters[0].Type, known.GameLoopDelegate) && parameters[0].RefKind == RefKind.None) return SignatureKind.LegacyFactory;
		if (returnType.SpecialType == SpecialType.System_Void && parameters.Length == 2 && KnownSymbols.Is(parameters[0].Type, known.GameTime) && KnownSymbols.Is(parameters[1].Type, known.GameLoopDelegate)
			&& parameters[0].RefKind == RefKind.None && parameters[1].RefKind == RefKind.None) return SignatureKind.LegacyVoidNext;

		if (returnType.SpecialType != SpecialType.System_Void)
		{
			reason = $"it returns {RuntimeName(returnType)}";
			return SignatureKind.Invalid;
		}

		var gameTimes = 0;
		foreach (var parameter in parameters)
		{
			var type = parameter.Type;
			if (parameter.RefKind != RefKind.None || type is IPointerTypeSymbol || type.TypeKind == TypeKind.FunctionPointer)
			{
				reason = $"parameter '{parameter.Name}' is passed by reference";
				return SignatureKind.Invalid;
			}

			if (KnownSymbols.Is(type, known.GameLoopDelegate))
			{
				reason = $"parameter '{parameter.Name}' is a GameLoopDelegate outside the legacy forms (GameTime dt, GameLoopDelegate next) and (GameLoopDelegate next)";
				return SignatureKind.Invalid;
			}

			if (KnownSymbols.Is(type, known.GameTime)) gameTimes++;
		}

		if (gameTimes > 1)
		{
			reason = "it takes more than one GameTime";
			return SignatureKind.Invalid;
		}

		if (parameters.Length == 0) return SignatureKind.NoArguments;
		if (parameters.Length == 1 && gameTimes == 1) return SignatureKind.GameTime;
		return SignatureKind.Injected;
	}

	/// <summary>The injected service parameter types of a step method (every parameter but the <c>GameTime</c>).</summary>
	public List<ITypeSymbol> ServiceParameters(IMethodSymbol method, SignatureKind signature) =>
		signature == SignatureKind.Injected ? method.Parameters.Select(p => p.Type).Where(t => !KnownSymbols.Is(t, known.GameTime)).ToList() : [];

	private static bool HasAttribute(IMethodSymbol method, INamedTypeSymbol? attribute) =>
		attribute is not null && method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));

	private static bool IsTaskLike(ITypeSymbol type)
	{
		if (type is not INamedTypeSymbol named) return false;
		var ns = named.ContainingNamespace?.ToDisplayString();
		if (ns != "System.Threading.Tasks") return false;
		return named.MetadataName is "Task" or "ValueTask" or "Task`1" or "ValueTask`1";
	}

	/// <summary>The name reflection gives a type (<c>Type.Name</c>): metadata name, with array and pointer suffixes.</summary>
	public static string RuntimeName(ITypeSymbol type) => type switch
	{
		IArrayTypeSymbol array => RuntimeName(array.ElementType) + (array.Rank == 1 ? "[]" : "[" + new string(',', array.Rank - 1) + "]"),
		IPointerTypeSymbol pointer => RuntimeName(pointer.PointedAtType) + "*",
		_ => type.MetadataName,
	};

	public static Location? SourceLocation(ISymbol symbol) => symbol.Locations.FirstOrDefault(l => l.IsInSource);

	private sealed record ScopeAttributeData(bool IsBegin, int Stage, int Order, bool HasOrder, string? ScopeName);

	private sealed record ScopeMethod(IMethodSymbol Method, ScopeAttributeData Attribute, int Index, SignatureKind Signature, Location? Location, List<(ITypeSymbol Target, bool IsBefore)> Constraints);

	private sealed class PairComparer : IEqualityComparer<(INamedTypeSymbol, INamedTypeSymbol)>
	{
		public bool Equals((INamedTypeSymbol, INamedTypeSymbol) x, (INamedTypeSymbol, INamedTypeSymbol) y) =>
			SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && SymbolEqualityComparer.Default.Equals(x.Item2, y.Item2);

		public int GetHashCode((INamedTypeSymbol, INamedTypeSymbol) obj) =>
			(SymbolEqualityComparer.Default.GetHashCode(obj.Item1) * 397) ^ SymbolEqualityComparer.Default.GetHashCode(obj.Item2);
	}
}
