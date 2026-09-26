using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ion.Generators;

/// <summary>How a call site uses an event type.</summary>
internal enum EventSiteKind
{
	/// <summary>Emits the event (<c>IEvents.Emit</c> or a method marked <c>[EmitsEvent]</c>).</summary>
	Emit,
	/// <summary>Creates a reader (a <c>[ReadsEvent]</c> method returning <c>EventReader&lt;T&gt;</c>).</summary>
	CreateReader,
	/// <summary>Reads (a <c>[ReadsEvent]</c> method that does not return a reader, or a read member of <c>EventReader&lt;T&gt;</c>).</summary>
	Read,
}

/// <summary>Which engine API a call site is, when the generated bus can route it to a typed field.</summary>
internal enum EventApi
{
	None,
	EventsEmit,
	BusEmit,
	ExtensionEmit,
	EventsReader,
	BusReader,
}

/// <summary>Where the enclosing code runs: a stage method or function step (stage 1 to 7), or anything else (0).</summary>
internal readonly record struct EventContext(int Stage, string Name, bool InLoop);

internal sealed class EventSite
{
	public EventSiteKind Kind { get; init; }
	public ITypeSymbol Type { get; init; } = null!;
	public InvocationExpressionSyntax Invocation { get; init; } = null!;
	public SemanticModel Model { get; init; } = null!;
	public IMethodSymbol Method { get; init; } = null!;
	public EventContext Context { get; init; }
	public EventApi Api { get; init; }
	public bool IsReaderMember { get; init; }

	/// <summary>Set by the emitter: the interceptor's attribute, when the call is routed to the generated bus.</summary>
	public string? Attribute { get; set; }
	public int Index { get; set; }

	public Location Location => Invocation.GetLocation();

	public bool IsPerFrameStage => Context.Stage is >= 2 and <= 6;
}

/// <summary>Everything the compilation (and the Ion assemblies it references) does with one event type.</summary>
internal sealed class EventTypeUsage(ITypeSymbol type)
{
	public ITypeSymbol Type { get; } = type;
	public List<EventSite> Sites { get; } = [];

	/// <summary>The usage flags of this compilation (<c>Ion.EventUsage</c> values).</summary>
	public int Own { get; set; }

	/// <summary>The usage flags summarized by referenced assemblies.</summary>
	public int Referenced { get; set; }

	public int All => Own | Referenced;
}

/// <summary>
/// Finds every event call site of a compilation (emits, reader creations, reads), the <c>IonApplication.CreateBuilder</c>
/// calls that install the generated bus, and the event summaries of referenced assemblies, and reports ION101 to ION106.
/// </summary>
internal sealed class EventAnalyzer
{
	// Ion.EventUsage values (the runtime enum, repeated here: the generator does not reference the engine).
	public const int Emitted = 1;
	public const int Read = 2;
	public const int EmittedPerFrame = 4;
	public const int EmittedInLoop = 8;

	private readonly KnownSymbols _known;
	private readonly Compilation _compilation;
	private readonly Action<DiagnosticDescriptor, Location?, string> _report;
	private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];
	private readonly Dictionary<ITypeSymbol, EventTypeUsage> _types = new(SymbolEqualityComparer.Default);

	public EventAnalyzer(KnownSymbols known, Action<DiagnosticDescriptor, Location?, string> report)
	{
		_known = known;
		_compilation = known.Compilation;
		_report = report;
	}

	/// <summary>The event types this compilation or its references use, in a stable order.</summary>
	public IEnumerable<EventTypeUsage> Types => _types.Values.OrderBy(t => TypeName(t.Type), StringComparer.Ordinal);

	/// <summary>The <c>IonApplication.CreateBuilder</c> calls of this compilation (it is an application).</summary>
	public List<(InvocationExpressionSyntax Invocation, SemanticModel Model, IMethodSymbol Method)> CreateBuilderCalls { get; } = [];

	/// <summary>Whether this compilation has event call sites of its own.</summary>
	public bool HasOwnSites => _types.Values.Any(t => t.Sites.Count > 0);

	/// <summary>A cheap syntactic filter for the generator's trigger (see <see cref="ScheduleGenerator"/>).</summary>
	public static bool IsCandidate(SyntaxNode node) => node switch
	{
		InvocationExpressionSyntax invocation => NameOf(invocation) is { } name && (name is GenericNameSyntax || IsEventName(name.Identifier.ValueText)),
		FieldDeclarationSyntax field => field.Declaration.Type.ToString().Contains("EventReader"),
		PropertyDeclarationSyntax property => property.Type.ToString().Contains("EventReader"),
		MethodDeclarationSyntax method => HasEventAttribute(method),
		_ => false,
	};

	private static SimpleNameSyntax? NameOf(InvocationExpressionSyntax invocation) => invocation.Expression switch
	{
		MemberAccessExpressionSyntax member => member.Name,
		SimpleNameSyntax simple => simple,
		MemberBindingExpressionSyntax binding => binding.Name,
		_ => null,
	};

	private static bool IsEventName(string name) =>
		name.StartsWith("Emit", StringComparison.Ordinal) || name is "CreateBuilder" or "TryRead" or "Read" or "Any" or "TryReadLatest" or "Reader";

	private static bool HasEventAttribute(MethodDeclarationSyntax method) =>
		method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is var n && (n.Contains("EmitsEvent") || n.Contains("ReadsEvent")));

	// Names of the methods marked [EmitsEvent]/[ReadsEvent] that do not pass the name filter: in this compilation's source
	// and in the Ion assemblies it references.
	private readonly HashSet<string> _markedNames = new(StringComparer.Ordinal);

	private bool IsCandidateInvocation(InvocationExpressionSyntax invocation) =>
		NameOf(invocation) is { } name && (name is GenericNameSyntax || IsEventName(name.Identifier.ValueText) || _markedNames.Contains(name.Identifier.ValueText));

	private void FindMarkedNames(CancellationToken cancellationToken)
	{
		foreach (var tree in _compilation.SyntaxTrees)
		{
			foreach (var method in tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
			{
				if (HasEventAttribute(method)) _markedNames.Add(method.Identifier.ValueText);
			}
		}

		foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
		{
			if (!assembly.Name.StartsWith("Ion", StringComparison.Ordinal)) continue;
			var types = new Stack<INamespaceOrTypeSymbol>();
			types.Push(assembly.GlobalNamespace);
			while (types.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				foreach (var member in types.Pop().GetMembers())
				{
					switch (member)
					{
						case INamespaceOrTypeSymbol container:
							types.Push(container);
							break;
						case IMethodSymbol method when method.GetAttributes().Any(a => KnownSymbols.Is(a.AttributeClass, _known.EmitsEventAttribute) || KnownSymbols.Is(a.AttributeClass, _known.ReadsEventAttribute)):
							_markedNames.Add(method.Name);
							break;
					}
				}
			}
		}
	}

	public void Analyze(CancellationToken cancellationToken)
	{
		FindMarkedNames(cancellationToken);

		foreach (var tree in _compilation.SyntaxTrees)
		{
			SemanticModel? model = null;
			foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
			{
				cancellationToken.ThrowIfCancellationRequested();

				switch (node)
				{
					case InvocationExpressionSyntax invocation when IsCandidateInvocation(invocation):
						AnalyzeInvocation(invocation, model ??= Model(tree), cancellationToken);
						break;
					case FieldDeclarationSyntax field when IsCandidate(field):
						CheckField(field, model ??= Model(tree), cancellationToken);
						break;
					case PropertyDeclarationSyntax property when IsCandidate(property):
						CheckProperty(property, model ??= Model(tree), cancellationToken);
						break;
				}
			}
		}

		ReadSummaries();
	}

	private SemanticModel Model(SyntaxTree tree)
	{
		if (!_models.TryGetValue(tree, out var model))
		{
			model = _compilation.GetSemanticModel(tree);
			_models[tree] = model;
		}

		return model;
	}

	private EventTypeUsage Usage(ITypeSymbol type)
	{
		if (!_types.TryGetValue(type, out var usage))
		{
			usage = new EventTypeUsage(type);
			_types[type] = usage;
		}

		return usage;
	}

	#region Call sites

	private void AnalyzeInvocation(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
	{
		var info = model.GetSymbolInfo(invocation, cancellationToken);
		var method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		if (method is null) return;

		if (method.IsStatic && method.Name == "CreateBuilder" && KnownSymbols.Is(method.ContainingType, _known.IonApplicationClass))
		{
			// The engine's own overloads call each other; only an application's call installs a bus.
			if (info.Symbol is not null && !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly)) CreateBuilderCalls.Add((invocation, model, method));
			return;
		}

		// Reads through a reader: EventReader<T>.TryRead(), Read(), Any(), TryReadLatest().
		if (method.ContainingType is { } containing && KnownSymbols.Is(containing, _known.EventReader) && method.Name is "TryRead" or "Read" or "Any" or "TryReadLatest")
		{
			var readType = containing.TypeArguments[0];
			if (readType is ITypeParameterSymbol or IErrorTypeSymbol) return;
			AddSite(new EventSite
			{
				Kind = EventSiteKind.Read,
				Type = readType,
				Invocation = invocation,
				Model = model,
				Method = method,
				Context = ContextOf(invocation, model, cancellationToken),
				IsReaderMember = true,
			});
			return;
		}

		var definition = (method.ReducedFrom ?? method).OriginalDefinition;
		foreach (var attribute in definition.GetAttributes())
		{
			var isEmit = KnownSymbols.Is(attribute.AttributeClass, _known.EmitsEventAttribute);
			if (!isEmit && !KnownSymbols.Is(attribute.AttributeClass, _known.ReadsEventAttribute)) continue;

			var type = attribute.ConstructorArguments.Length == 1
				? attribute.ConstructorArguments[0].Value as ITypeSymbol
				: method.TypeArguments.Length > 0 ? method.TypeArguments[0] : null;
			if (type is null or ITypeParameterSymbol or IErrorTypeSymbol) continue;

			if (!type.IsUnmanagedType)
			{
				_report(Diagnostics.NotUnmanaged, invocation.GetLocation(), $"'{Display(type)}' cannot be an event: events are stored unboxed in typed channels, so the payload must be an unmanaged struct, and {WhyManaged(type)}. Use ids, indices or handles instead of references.");
				continue;
			}

			var kind = isEmit ? EventSiteKind.Emit : KnownSymbols.Is(method.ReturnType, _known.EventReader) ? EventSiteKind.CreateReader : EventSiteKind.Read;
			AddSite(new EventSite
			{
				Kind = kind,
				Type = type,
				Invocation = invocation,
				Model = model,
				Method = method,
				Context = ContextOf(invocation, model, cancellationToken),
				Api = info.Symbol is null ? EventApi.None : ApiOf(definition, isEmit),
			});
		}
	}

	private EventApi ApiOf(IMethodSymbol definition, bool isEmit)
	{
		var type = definition.ContainingType;
		if (isEmit)
		{
			if (definition.Name == "Emit" && definition.Parameters.Length == 1 && KnownSymbols.Is(type, _known.Events)) return EventApi.EventsEmit;
			if (definition.Name == "Emit" && definition.Parameters.Length == 1 && KnownSymbols.Is(type, _known.EventBus)) return EventApi.BusEmit;
			if (definition.Name == "Emit" && definition.Parameters.Length == 1 && definition.IsExtensionMethod && KnownSymbols.Is(type, _known.EventsExtensions)) return EventApi.ExtensionEmit;
			return EventApi.None;
		}

		if (definition.Name == "Reader" && definition.Parameters.Length == 0 && KnownSymbols.Is(type, _known.Events)) return EventApi.EventsReader;
		if (definition.Name == "Reader" && definition.Parameters.Length == 0 && KnownSymbols.Is(type, _known.EventBus)) return EventApi.BusReader;
		return EventApi.None;
	}

	private void AddSite(EventSite site)
	{
		var usage = Usage(site.Type);
		usage.Sites.Add(site);

		if (site.Kind == EventSiteKind.Emit)
		{
			usage.Own |= Emitted;
			if (site.Context.Stage is 3 or 4)
			{
				usage.Own |= EmittedPerFrame;
				if (site.Context.InLoop) usage.Own |= EmittedInLoop;
			}
		}
		else
		{
			usage.Own |= Read;
		}
	}

	/// <summary>
	/// The stage of the code around <paramref name="node"/>: the nearest enclosing method with a stage or scope attribute,
	/// or a lambda passed to a function step (<c>app.Update(...)</c>). Lambdas and local functions inside a method count
	/// as that method.
	/// </summary>
	private EventContext ContextOf(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
	{
		var inLoop = false;
		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			switch (current)
			{
				case ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
					inLoop = true;
					break;

				case AnonymousFunctionExpressionSyntax lambda:
					if (lambda.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } }
						&& model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol step
						&& (KnownSymbols.Is(step.ContainingType, _known.StageStepExtensions) || KnownSymbols.Is(step.ContainingType, _known.SceneStageStepExtensions)))
					{
						var stage = Array.IndexOf(KnownSymbols.StageNames, step.Name) + 1;
						if (stage > 0) return new EventContext(stage, $"the {step.Name} function step", inLoop);
					}

					break;

				case MethodDeclarationSyntax declaration:
				{
					var method = model.GetDeclaredSymbol(declaration, cancellationToken);
					var name = method is null ? declaration.Identifier.ValueText : method.ContainingType.Name + "." + method.Name;
					return new EventContext(method is null ? 0 : StageOf(method), name, inLoop);
				}

				case BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or BaseFieldDeclarationSyntax or BasePropertyDeclarationSyntax or BaseTypeDeclarationSyntax or GlobalStatementSyntax:
					return new EventContext(0, current is BaseTypeDeclarationSyntax type ? type.Identifier.ValueText : "", inLoop);
			}
		}

		return new EventContext(0, "", inLoop);
	}

	private int StageOf(IMethodSymbol method)
	{
		foreach (var attribute in method.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass) continue;
			if (_known.StageAttributes.TryGetValue(attributeClass, out var stage)) return stage;
			if ((KnownSymbols.Is(attributeClass, _known.BeginAttribute) || KnownSymbols.Is(attributeClass, _known.EndAttribute))
				&& attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int scopeStage)
			{
				return scopeStage;
			}
		}

		return 0;
	}

	private static string WhyManaged(ITypeSymbol type)
	{
		if (type.IsReferenceType) return $"'{type.Name}' is a {(type.TypeKind == TypeKind.Interface ? "interface" : "class")}";

		foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
		{
			if (field.IsStatic || field.IsConst) continue;
			if (!field.Type.IsUnmanagedType)
			{
				var name = field.AssociatedSymbol?.Name ?? field.Name;
				return $"its field '{name}' is a '{Display(field.Type)}'{(field.Type.IsReferenceType ? " (a reference)" : "")}";
			}
		}

		return $"'{type.Name}' contains references";
	}

	#endregion

	#region Readers in readonly storage (ION106)

	private void CheckField(FieldDeclarationSyntax field, SemanticModel model, CancellationToken cancellationToken)
	{
		if (!field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)) return;
		if (model.GetTypeInfo(field.Declaration.Type, cancellationToken).Type is not { } type || !KnownSymbols.Is(type, _known.EventReader)) return;

		foreach (var variable in field.Declaration.Variables)
		{
			_report(Diagnostics.ReadonlyReader, variable.Identifier.GetLocation(), $"'{variable.Identifier.ValueText}' is a readonly {Display(type)}: reading through it advances a copy of the reader, so it returns the same events every time. Remove 'readonly'.");
		}
	}

	private void CheckProperty(PropertyDeclarationSyntax property, SemanticModel model, CancellationToken cancellationToken)
	{
		if (property.Type is RefTypeSyntax) return;
		if (model.GetTypeInfo(property.Type, cancellationToken).Type is not { } type || !KnownSymbols.Is(type, _known.EventReader)) return;

		_report(Diagnostics.ReadonlyReader, property.Identifier.GetLocation(), $"'{property.Identifier.ValueText}' is a {Display(type)} property: every access returns a copy of the reader, so reading through it does not advance. Keep the reader in a field that is not readonly.");
	}

	#endregion

	#region Summaries of referenced assemblies

	private void ReadSummaries()
	{
		if (_known.EventUsageAttribute is null) return;

		foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
		{
			foreach (var attribute in assembly.GetAttributes())
			{
				if (!KnownSymbols.Is(attribute.AttributeClass, _known.EventUsageAttribute) || attribute.ConstructorArguments.Length != 2) continue;
				if (attribute.ConstructorArguments[0].Value is not ITypeSymbol type || type is IErrorTypeSymbol) continue;
				if (attribute.ConstructorArguments[1].Value is not int usage) continue;

				Usage(type).Referenced |= usage;
			}
		}
	}

	#endregion

	#region Diagnostics

	/// <summary>Reports ION101 to ION103 and ION105 (ION104 and ION106 are reported while analyzing).</summary>
	public void Report()
	{
		var isApplication = CreateBuilderCalls.Count > 0;

		foreach (var usage in Types)
		{
			var name = Display(usage.Type);

			if (isApplication && (usage.Own & Emitted) != 0 && (usage.All & Read) == 0)
			{
				foreach (var site in usage.Sites.Where(s => s.Kind == EventSiteKind.Emit))
				{
					_report(Diagnostics.EventNeverRead, site.Location, $"'{name}' is emitted here but never read: nothing in this application or the Ion assemblies it references reads it. Read it (a reader from IEvents.Reader<{name}>() created in a constructor), or stop emitting it.");
				}
			}

			if (isApplication && (usage.All & Emitted) == 0)
			{
				foreach (var site in usage.Sites.Where(s => s.Kind != EventSiteKind.Emit && !s.IsReaderMember))
				{
					_report(Diagnostics.EventNeverEmitted, site.Location, $"'{name}' is read here but never emitted: nothing in this application or the Ion assemblies it references emits it, so this reader never has events. Emit it, or remove the reader.");
				}
			}

			foreach (var site in usage.Sites.Where(s => s.Kind == EventSiteKind.CreateReader && s.IsPerFrameStage))
			{
				_report(Diagnostics.ReaderInStage, site.Location, $"{Capitalize(site.Context.Name)} creates a reader of '{name}' every time it runs ({KnownSymbols.StageName(site.Context.Stage)}). A new reader starts at the oldest visible event, so it sees the previous frame's events again. Create the reader once, in the constructor or a field initializer, and keep it in a field that is not readonly.");
			}

			ReportLatency(usage, name);
		}
	}

	private void ReportLatency(EventTypeUsage usage, string name)
	{
		var emits = usage.Sites.Where(s => s.Kind == EventSiteKind.Emit).ToList();
		if (emits.Count == 0 || (usage.Referenced & Emitted) != 0 || emits.Any(e => !e.IsPerFrameStage)) return;

		var first = emits.Min(e => e.Context.Stage);
		var emitters = string.Join(", ", emits.Select(e => e.Context.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
		var stages = string.Join(" and ", emits.Select(e => e.Context.Stage).Distinct().OrderBy(s => s).Select(KnownSymbols.StageName));
		var reported = new HashSet<string>();

		foreach (var site in usage.Sites.Where(s => s.Kind == EventSiteKind.Read && s.IsPerFrameStage && s.Context.Stage < first))
		{
			if (!reported.Add(site.Context.Name)) continue;
			_report(Diagnostics.ReadBeforeEmit, site.Location, $"{Capitalize(site.Context.Name)} reads '{name}' in {KnownSymbols.StageName(site.Context.Stage)}, but it is only emitted in {stages} ({emitters}), later in the frame, so it sees each event one frame after it was emitted. Read it in a later stage, or emit it earlier, if that latency is not intended.");
		}
	}

	#endregion

	private static string Capitalize(string value) => value.Length > 0 && char.IsLower(value[0]) ? char.ToUpperInvariant(value[0]) + value.Substring(1) : value;

	public static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

	public static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

	/// <summary>The initial capacity of the generated channel of a type used as <paramref name="usage"/>.</summary>
	public static int Capacity(int usage) =>
		(usage & EmittedInLoop) != 0 ? 256 :
		(usage & EmittedPerFrame) != 0 ? 64 :
		(usage & Emitted) != 0 ? 16 : 4;

	public static string DescribeUsage(int usage)
	{
		var parts = new List<string>();
		if ((usage & EmittedInLoop) != 0) parts.Add("emitted in a loop in FixedUpdate or Update");
		else if ((usage & EmittedPerFrame) != 0) parts.Add("emitted in FixedUpdate or Update");
		else if ((usage & Emitted) != 0) parts.Add("emitted");
		if ((usage & Read) != 0) parts.Add("read");
		return string.Join(", ", parts) + "; capacity " + Capacity(usage).ToString(CultureInfo.InvariantCulture);
	}
}
