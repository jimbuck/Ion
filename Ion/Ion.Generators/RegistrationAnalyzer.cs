using System.Globalization;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Ion.Generators;

/// <summary>
/// Finds the registration calls of a compilation (the ones to intercept), and follows the registrations made on a
/// builder through a method body, the helpers it calls in this compilation, and the helpers of other assemblies through
/// their <c>ScheduleRegistrations</c> summaries.
/// </summary>
internal sealed class RegistrationAnalyzer
{
	private readonly KnownSymbols _known;
	private readonly SystemAnalyzer _systems;
	private readonly Compilation _compilation;
	private readonly bool _canIntercept;
	private readonly Dictionary<InvocationExpressionSyntax, InterceptedCall> _calls = [];
	private readonly Dictionary<InvocationExpressionSyntax, string> _notIntercepted = [];
	private readonly Dictionary<(IMethodSymbol, int, bool), List<RegistrationOp>> _programs = new(new MethodKeyComparer());
	private readonly HashSet<(IMethodSymbol, int, bool)> _inProgress = new(new MethodKeyComparer());
	private readonly Dictionary<IAssemblySymbol, Dictionary<string, (string Ops, ImmutableArrayWrapper Types)>> _summaries = new(SymbolEqualityComparer.Default);
	private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

	public RegistrationAnalyzer(KnownSymbols known, SystemAnalyzer systems, bool canIntercept)
	{
		_known = known;
		_systems = systems;
		_compilation = known.Compilation;
		_canIntercept = canIntercept;
	}

	public IReadOnlyCollection<InterceptedCall> Calls => _calls.Values;

	/// <summary>Every system passed to a <c>UseSystem</c> call of this compilation that the generator could analyze, with the call.</summary>
	public List<(SystemInfo System, Location Location)> SystemUses { get; } = [];

	/// <summary>Whether the compilation makes any call the generator would intercept.</summary>
	public bool HasRegistrations => _calls.Count > 0 || _notIntercepted.Count > 0 || SystemUses.Count > 0;

	public SemanticModel Model(SyntaxTree tree)
	{
		if (!_models.TryGetValue(tree, out var model))
		{
			model = _compilation.GetSemanticModel(tree);
			_models[tree] = model;
		}

		return model;
	}

	#region Finding calls

	/// <summary>Finds and classifies every registration call of the compilation.</summary>
	public void FindCalls(CancellationToken cancellationToken)
	{
		foreach (var tree in _compilation.SyntaxTrees)
		{
			var root = tree.GetRoot(cancellationToken);
			SemanticModel? model = null;

			foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsCandidateName(invocation)) continue;

				model ??= Model(tree);
				if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation) continue;

				var call = Classify(invocation, operation, model, out var notIntercepted);
				if (call is not null)
				{
					if (InterceptableLocations.AttributeFor(model, invocation, cancellationToken) is { } attribute)
					{
						call.Attribute = attribute;
						_calls[invocation] = call;
						continue;
					}

					notIntercepted = "the compiler cannot locate the call for interception";
				}

				if (notIntercepted is not null) _notIntercepted[invocation] = notIntercepted;
			}
		}

		var index = 0;
		foreach (var call in _calls.Values.OrderBy(c => c.Invocation.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(c => c.Invocation.Span.End))
		{
			call.Index = index++;
			call.Site = $"{_compilation.AssemblyName}#{call.Index.ToString(CultureInfo.InvariantCulture)}";
		}
	}

	public static bool IsCandidateName(InvocationExpressionSyntax invocation)
	{
		var name = invocation.Expression switch
		{
			MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
			SimpleNameSyntax simple => simple.Identifier.ValueText,
			MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
			_ => null,
		};

		return name is not null && IsRegistrationName(name);
	}

	public static bool IsRegistrationName(string name) => name switch
	{
		"UseSystem" or "UseScene" or "Run" or "RunFrames" or "Build" or "BuildSchedule" => true,
		"Init" or "First" or "FixedUpdate" or "Update" or "Render" or "Last" or "Destroy" => true,
		"UseInit" or "UseFirst" or "UseFixedUpdate" or "UseUpdate" or "UseRender" or "UseLast" or "UseDestroy" => true,
		_ => false,
	};

	private enum Api
	{
		None,
		UseSystem,
		Function,
		Middleware,
		DelegateServicesMiddleware,
		UseScene,
		Root,
		BuilderMember,
	}

	private Api ApiOf(IMethodSymbol method, out int stage)
	{
		stage = 0;
		var definition = method.OriginalDefinition;
		var type = definition.ContainingType;
		if (type is null) return Api.None;

		if (KnownSymbols.Is(type, _known.UseSystemExtensions) || KnownSymbols.Is(type, _known.SceneUseSystemExtensions))
		{
			return definition.Name == "UseSystem" ? Api.UseSystem : Api.None;
		}

		if (KnownSymbols.Is(type, _known.StageStepExtensions) || KnownSymbols.Is(type, _known.SceneStageStepExtensions))
		{
			stage = Array.IndexOf(KnownSymbols.StageNames, definition.Name) + 1;
			return stage > 0 ? Api.Function : Api.None;
		}

		if (KnownSymbols.Is(type, _known.UseDelegateServiceExtensions))
		{
			stage = StageOfUse(definition.Name);
			return stage > 0 ? Api.DelegateServicesMiddleware : Api.None;
		}

		if (KnownSymbols.Is(type, _known.ScenesBuilderExtensions))
		{
			return definition.Name == "UseScene" && definition.Parameters.Length == 3 ? Api.UseScene : Api.None;
		}

		if (KnownSymbols.Is(type, _known.IonApplicationInterface) || KnownSymbols.Is(type, _known.IonApplicationClass) || KnownSymbols.Is(type, _known.SceneBuilderInterface) || KnownSymbols.Is(type, _known.ScheduleBuilderInterface))
		{
			if (definition.IsStatic) return Api.None;
			stage = StageOfUse(definition.Name);
			if (stage > 0 && definition.Parameters.Length == 1) return Api.Middleware;
			if (definition.Name is "Run" or "RunFrames" || (definition.Name is "Build" or "BuildSchedule" && KnownSymbols.Is(type, _known.IonApplicationClass) && definition.Parameters.Length == 0)) return Api.Root;
			return Api.BuilderMember;
		}

		return Api.None;
	}

	private static int StageOfUse(string name) => name.StartsWith("Use", StringComparison.Ordinal) ? Array.IndexOf(KnownSymbols.StageNames, name.Substring(3)) + 1 : 0;

	private InterceptedCall? Classify(InvocationExpressionSyntax invocation, IInvocationOperation operation, SemanticModel model, out string? notIntercepted)
	{
		notIntercepted = null;
		var method = operation.TargetMethod;
		var api = ApiOf(method, out var stage);

		switch (api)
		{
			case Api.UseSystem:
			{
				var (service, implementation) = SystemTypes(operation);
				if (service is null || implementation is null)
				{
					notIntercepted = "the system type is not known at compile time";
					return null;
				}

				if (!IsAccessible(service) || !IsAccessible(implementation))
				{
					notIntercepted = $"'{implementation.Name}' is not accessible to generated code";
					return null;
				}

				var system = _systems.Analyze(service, implementation);
				SystemUses.Add((system, invocation.GetLocation()));
				if (system.NotDescribable is { } why)
				{
					notIntercepted = why;
					return null;
				}

				if (!_canIntercept) return Unintercepted(out notIntercepted);
				return new InterceptedCall { Kind = CallKind.UseSystem, Invocation = invocation, Operation = operation, Model = model, System = system };
			}

			case Api.Function:
			{
				var services = method.TypeArguments.ToList<ITypeSymbol>();
				if (method.Parameters.Length != 4 || services.Any(t => !IsAccessible(t)))
				{
					notIntercepted = "a service type is not accessible to generated code";
					return null;
				}

				if (!_canIntercept) return Unintercepted(out notIntercepted);

				var step = Argument(operation, 1);
				var (name, after, before) = DescribeFunction(step, services, lambdaServices: true);
				return new InterceptedCall
				{
					Kind = CallKind.Function,
					Invocation = invocation,
					Operation = operation,
					Model = model,
					Stage = stage,
					Order = Argument(operation, 2)?.ConstantValue is { HasValue: true, Value: int order } ? order : 0,
					Name = name,
					After = after,
					Before = before,
					Services = services,
				};
			}

			case Api.Middleware:
			{
				if (!_canIntercept) return Unintercepted(out notIntercepted);
				var (name, _, _) = DescribeFunction(Argument(operation, 0), [], lambdaServices: false);
				return new InterceptedCall { Kind = CallKind.Middleware, Invocation = invocation, Operation = operation, Model = model, Stage = stage, Name = name };
			}

			case Api.DelegateServicesMiddleware:
			{
				var services = method.TypeArguments.ToList<ITypeSymbol>();
				if (services.Any(t => !IsAccessible(t)))
				{
					notIntercepted = "a service type is not accessible to generated code";
					return null;
				}

				if (!_canIntercept) return Unintercepted(out notIntercepted);
				return new InterceptedCall { Kind = CallKind.DelegateServicesMiddleware, Invocation = invocation, Operation = operation, Model = model, Stage = stage, Name = "UseDelegateServiceExtensions.lambda", Services = services };
			}

			case Api.UseScene:
			{
				if (!_canIntercept) return null;
				var id = Argument(operation, 1);
				int? sceneId = id?.ConstantValue is { HasValue: true, Value: { } value } ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
				if (method.Parameters[1].Type is { } idType && !IsAccessible(idType)) return null;
				return new InterceptedCall { Kind = CallKind.UseScene, Invocation = invocation, Operation = operation, Model = model, SceneId = sceneId };
			}

			case Api.Root:
			{
				if (!_canIntercept) return null;
				return new InterceptedCall { Kind = CallKind.Root, Invocation = invocation, Operation = operation, Model = model };
			}

			default:
				return null;
		}
	}

	private static InterceptedCall? Unintercepted(out string? notIntercepted)
	{
		notIntercepted = "interceptors are not available";
		return null;
	}

	private (INamedTypeSymbol? Service, INamedTypeSymbol? Implementation) SystemTypes(IInvocationOperation operation)
	{
		var method = operation.TargetMethod;
		if (method.TypeArguments.Length == 1) return (method.TypeArguments[0] as INamedTypeSymbol, method.TypeArguments[0] as INamedTypeSymbol).Closed();
		if (method.TypeArguments.Length == 2) return (method.TypeArguments[0] as INamedTypeSymbol, method.TypeArguments[1] as INamedTypeSymbol).Closed();

		var types = operation.Arguments.Where(a => a.Parameter?.Ordinal > 0).OrderBy(a => a.Parameter!.Ordinal).Select(a => Unwrap(a.Value) is ITypeOfOperation typeOf ? typeOf.TypeOperand as INamedTypeSymbol : null).ToList();
		if (types.Count == 1) return (types[0], types[0]).Closed();
		if (types.Count == 2) return (types[0], types[1]).Closed();
		return (null, null);
	}

	/// <summary>
	/// The printed name and ordering constraints of a delegate argument, as <c>ScheduleModel.DescribeDelegate</c> would
	/// compute them at run time; nulls when the delegate is not a lambda or method group (the runtime then reads them).
	/// </summary>
	private (string? Name, List<ITypeSymbol>? After, List<ITypeSymbol>? Before) DescribeFunction(IOperation? argument, List<ITypeSymbol> services, bool lambdaServices)
	{
		var value = Unwrap(argument);
		if (value is IDelegateCreationOperation creation) value = Unwrap(creation.Target);

		IMethodSymbol? method = value switch
		{
			IAnonymousFunctionOperation lambda => lambda.Symbol,
			IMethodReferenceOperation reference => reference.Method,
			_ => null,
		};

		if (method is null) return (null, null, null);

		string name;
		var typeName = method.ContainingType is { } containing ? containing.MetadataName + "." : "";
		if (method.MethodKind == MethodKind.AnonymousFunction)
		{
			name = typeName + "lambda";
			if (lambdaServices) name += "(" + string.Join(", ", services.Select(SystemAnalyzer.RuntimeName)) + ")";
		}
		else
		{
			name = typeName + method.Name;
		}

		var constraints = _systems.OrderingOf(method);
		if (constraints is null || constraints.Value.After.Concat(constraints.Value.Before).Any(t => !IsAccessible(t))) return (name, null, null);
		return (name, constraints.Value.After, constraints.Value.Before);
	}

	private static IOperation? Argument(IInvocationOperation operation, int ordinal) =>
		operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal)?.Value;

	private static IOperation? Unwrap(IOperation? operation)
	{
		while (true)
		{
			switch (operation)
			{
				case IConversionOperation conversion:
					operation = conversion.Operand;
					continue;
				case IParenthesizedOperation parenthesized:
					operation = parenthesized.Operand;
					continue;
				default:
					return operation;
			}
		}
	}

	/// <summary>Whether generated code (a top-level type in this compilation) can name <paramref name="type"/>.</summary>
	public bool IsAccessible(ITypeSymbol type)
	{
		switch (type)
		{
			case IArrayTypeSymbol array:
				return IsAccessible(array.ElementType);
			case ITypeParameterSymbol:
			case IErrorTypeSymbol:
			case IPointerTypeSymbol:
				return false;
			case INamedTypeSymbol named:
				if (named.IsAnonymousType || named.IsUnboundGenericType) return false;
				for (var t = named; t is not null; t = t.ContainingType)
				{
					if (IsFileLocal(t)) return false;
				}

				if (!_compilation.IsSymbolAccessibleWithin(named, _compilation.Assembly)) return false;
				return named.TypeArguments.All(IsAccessible);
			default:
				return false;
		}
	}

	private static bool IsFileLocal(INamedTypeSymbol type) =>
		type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is BaseTypeDeclarationSyntax declaration && declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.FileKeyword)));

	#endregion

	#region Programs

	/// <summary>
	/// The registrations made on <paramref name="builder"/> by the invocations of <paramref name="bodies"/> that start before
	/// <paramref name="before"/>, in the order they run, with the calls to helpers expanded.
	/// </summary>
	public List<RegistrationOp> ProgramOf(IReadOnlyList<SyntaxNode> bodies, ISymbol builder, int before, bool expandMetadata)
	{
		var ops = new List<RegistrationOp>();

		var invocations = bodies
			.SelectMany(b => b.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
			.Where(i => i.SpanStart < before)
			.OrderBy(i => i.Span.End)
			.ToList();

		foreach (var invocation in invocations)
		{
			var model = Model(invocation.SyntaxTree);
			if (model.GetOperation(invocation) is not IInvocationOperation operation) continue;

			var parameter = BuilderArgument(operation, builder);
			if (parameter is null) continue;

			var conditional = IsConditional(invocation, bodies);
			var location = invocation.GetLocation();

			if (_calls.TryGetValue(invocation, out var call))
			{
				switch (call.Kind)
				{
					case CallKind.UseSystem:
					case CallKind.Function:
					case CallKind.Middleware:
					case CallKind.DelegateServicesMiddleware:
						ops.Add(call.ToOp(conditional));
						continue;
					case CallKind.UseScene:
						AddCall(ops, operation.TargetMethod, parameter.Value, conditional, location, expandMetadata);
						continue;
					default:
						continue;
				}
			}

			if (_notIntercepted.TryGetValue(invocation, out var reason))
			{
				ops.Add(new RegistrationOp { Kind = OpKind.Unmatchable, Conditional = conditional, Location = location, Reason = reason });
				continue;
			}

			switch (ApiOf(operation.TargetMethod, out _))
			{
				case Api.None:
					if (parameter.Value < 0)
					{
						// An instance method of a user type that receives the builder as its receiver cannot exist (the
						// builder types are Ion's), so this is a member we do not know: treat it as opaque.
						ops.Add(new RegistrationOp { Kind = OpKind.Opaque, Conditional = conditional, Location = location, Reason = operation.TargetMethod.Name });
						continue;
					}

					AddCall(ops, operation.TargetMethod, parameter.Value, conditional, location, expandMetadata);
					continue;

				case Api.UseSystem:
				case Api.Function:
				case Api.Middleware:
				case Api.DelegateServicesMiddleware:
				case Api.UseScene:
					ops.Add(new RegistrationOp { Kind = OpKind.Unmatchable, Conditional = conditional, Location = location, Reason = "interceptors are not available" });
					continue;

				default:
					continue;
			}
		}

		return ops;
	}

	private void AddCall(List<RegistrationOp> ops, IMethodSymbol target, int parameter, bool conditional, Location location, bool expandMetadata)
	{
		var definition = target.OriginalDefinition;

		if (SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, _compilation.Assembly))
		{
			if (definition.IsVirtual || definition.IsAbstract || definition.IsOverride || definition.ContainingType?.TypeKind == TypeKind.Interface)
			{
				ops.Add(new RegistrationOp { Kind = OpKind.Opaque, Conditional = conditional, Location = location, Reason = $"'{definition.Name}' is virtual" });
				return;
			}

			foreach (var op in ProgramOfMethod(definition, parameter, expandMetadata)) ops.Add(op.Nested(conditional, location));
			return;
		}

		if (!expandMetadata)
		{
			ops.Add(new RegistrationOp { Kind = OpKind.Call, Conditional = conditional, Location = location, Target = definition, ParameterIndex = parameter });
			return;
		}

		foreach (var op in ExpandSummary(definition, parameter)) ops.Add(op.Nested(conditional, location));
	}

	/// <summary>The registrations a method of this compilation makes on its parameter <paramref name="parameter"/>.</summary>
	public List<RegistrationOp> ProgramOfMethod(IMethodSymbol method, int parameter, bool expandMetadata)
	{
		var key = (method, parameter, expandMetadata);
		if (_programs.TryGetValue(key, out var cached)) return cached;
		if (!_inProgress.Add(key)) return [new RegistrationOp { Kind = OpKind.Opaque, Reason = $"'{method.Name}' is recursive" }];

		var bodies = new List<SyntaxNode>();
		foreach (var reference in method.DeclaringSyntaxReferences)
		{
			switch (reference.GetSyntax())
			{
				case BaseMethodDeclarationSyntax declaration:
					if (declaration.Body is { } body) bodies.Add(body);
					if (declaration.ExpressionBody is { } expression) bodies.Add(expression);
					break;
				case LocalFunctionStatementSyntax local:
					if (local.Body is { } localBody) bodies.Add(localBody);
					if (local.ExpressionBody is { } localExpression) bodies.Add(localExpression);
					break;
			}
		}

		List<RegistrationOp> ops = bodies.Count == 0 || parameter < 0 || parameter >= method.Parameters.Length
			? [new RegistrationOp { Kind = OpKind.Opaque, Reason = $"'{method.Name}' has no body" }]
			: ProgramOf(bodies, method.Parameters[parameter], int.MaxValue, expandMetadata);

		_inProgress.Remove(key);
		_programs[key] = ops;
		return ops;
	}

	/// <summary>
	/// The parameter ordinal through which <paramref name="operation"/> receives the builder (-1 for its receiver), or null
	/// when it does not.
	/// </summary>
	private int? BuilderArgument(IInvocationOperation operation, ISymbol builder)
	{
		if (operation.Instance is { } instance && IsBuilder(instance, builder)) return -1;

		foreach (var argument in operation.Arguments)
		{
			if (argument.Parameter is { } parameter && _known.IsBuilderType(parameter.Type) && IsBuilder(argument.Value, builder)) return parameter.Ordinal;
		}

		return null;
	}

	private bool IsBuilder(IOperation value, ISymbol builder)
	{
		switch (Unwrap(value))
		{
			case ILocalReferenceOperation local:
				return SymbolEqualityComparer.Default.Equals(local.Local, builder);
			case IParameterReferenceOperation parameter:
				return SymbolEqualityComparer.Default.Equals(parameter.Parameter, builder);
			case IInvocationOperation invocation:
				return _known.IsBuilderType(invocation.Type) && BuilderArgument(invocation, builder) is not null;
			default:
				return false;
		}
	}

	private static bool IsConditional(SyntaxNode node, IReadOnlyList<SyntaxNode> bodies)
	{
		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			if (bodies.Contains(current)) return false;

			switch (current)
			{
				case IfStatementSyntax:
				case ElseClauseSyntax:
				case SwitchStatementSyntax:
				case SwitchExpressionSyntax:
				case ForStatementSyntax:
				case CommonForEachStatementSyntax:
				case WhileStatementSyntax:
				case DoStatementSyntax:
				case CatchClauseSyntax:
				case FinallyClauseSyntax:
				case ConditionalExpressionSyntax:
				case ConditionalAccessExpressionSyntax:
				case AnonymousFunctionExpressionSyntax:
				case LocalFunctionStatementSyntax:
					return true;
				case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.CoalesceExpression):
					return true;
				case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
					return true;
			}
		}

		return false;
	}

	#endregion

	#region Roots and scenes

	/// <summary>
	/// The body and builder of a <c>Build()</c>/<c>Run()</c> call: the registrations made on the builder before it in the
	/// enclosing method (or top-level statements), when the builder is a local or parameter of that method.
	/// </summary>
	public List<RegistrationOp>? RootProgram(InterceptedCall call)
	{
		if (call.Operation.Instance is not { } instance) return null;

		ISymbol? builder = Unwrap(instance) switch
		{
			ILocalReferenceOperation local => local.Local,
			IParameterReferenceOperation parameter => parameter.Parameter,
			_ => null,
		};

		if (builder is null) return null;

		var (bodies, owner) = EnclosingBody(call.Invocation);
		if (bodies.Count == 0) return null;

		var declared = builder switch
		{
			ILocalSymbol local => local.DeclaringSyntaxReferences.Any(r => bodies.Any(b => b.Span.Contains(r.Span) && b.SyntaxTree == r.SyntaxTree)),
			IParameterSymbol parameter => owner is not null && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, owner),
			_ => false,
		};

		if (!declared) return null;
		return ProgramOf(bodies, builder, call.Invocation.SpanStart, expandMetadata: true);
	}

	private (List<SyntaxNode> Bodies, ISymbol? Owner) EnclosingBody(SyntaxNode node)
	{
		var model = Model(node.SyntaxTree);
		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			switch (current)
			{
				case AnonymousFunctionExpressionSyntax lambda:
					return ([lambda.Body], model.GetSymbolInfo(lambda).Symbol);
				case LocalFunctionStatementSyntax local:
					return (Bodies(local.Body, local.ExpressionBody), model.GetDeclaredSymbol(local));
				case BaseMethodDeclarationSyntax method:
					return (Bodies(method.Body, method.ExpressionBody), model.GetDeclaredSymbol(method));
				case AccessorDeclarationSyntax accessor:
					return (Bodies(accessor.Body, accessor.ExpressionBody), model.GetDeclaredSymbol(accessor));
				case GlobalStatementSyntax global:
					var unit = (CompilationUnitSyntax)global.Parent!;
					return (unit.Members.OfType<GlobalStatementSyntax>().Cast<SyntaxNode>().ToList(), null);
			}
		}

		return ([], null);
	}

	private static List<SyntaxNode> Bodies(SyntaxNode? body, SyntaxNode? expression)
	{
		var list = new List<SyntaxNode>();
		if (body is not null) list.Add(body);
		if (expression is not null) list.Add(expression);
		return list;
	}

	/// <summary>The registrations the configure callback of a <c>UseScene</c> call makes on the scene builder.</summary>
	public List<RegistrationOp>? SceneProgram(InterceptedCall call)
	{
		var configure = Unwrap(Argument(call.Operation, 2));
		if (configure is IDelegateCreationOperation creation) configure = Unwrap(creation.Target);

		switch (configure)
		{
			case IAnonymousFunctionOperation lambda when lambda.Symbol.Parameters.Length == 1 && lambda.Syntax is AnonymousFunctionExpressionSyntax syntax:
				return ProgramOf([syntax.Body], lambda.Symbol.Parameters[0], int.MaxValue, expandMetadata: true);

			case IMethodReferenceOperation reference when reference.Method.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(reference.Method.ContainingAssembly, _compilation.Assembly) && !reference.Method.IsVirtual && !reference.Method.IsAbstract:
				return ProgramOfMethod(reference.Method.OriginalDefinition, 0, expandMetadata: true);

			default:
				return null;
		}
	}

	#endregion

	#region Summaries

	/// <summary>
	/// The registrations summary of every method of this compilation that takes a builder, for the
	/// <c>ScheduleRegistrations</c> assembly attributes: key (<c>docId#parameter</c>), encoded ops, and the types they use.
	/// </summary>
	public List<(string Key, string Ops, List<ITypeSymbol> Types)> Summaries(CancellationToken cancellationToken)
	{
		var result = new List<(string, string, List<ITypeSymbol>)>();

		foreach (var tree in _compilation.SyntaxTrees)
		{
			var model = Model(tree);
			foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (declaration.Body is null && declaration.ExpressionBody is null) continue;
				if (!declaration.ParameterList.Parameters.Any()) continue;
				if (model.GetDeclaredSymbol(declaration, cancellationToken) is not { } method) continue;
				if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal or Accessibility.Protected)) continue;
				if (method.IsVirtual || method.IsAbstract || method.IsOverride) continue;
				if (method.GetDocumentationCommentId() is not { } id) continue;

				for (var p = 0; p < method.Parameters.Length; p++)
				{
					if (!_known.IsBuilderType(method.Parameters[p].Type)) continue;
					var ops = ProgramOfMethod(method, p, expandMetadata: false);
					var types = new List<ITypeSymbol>();
					result.Add((id + "#" + p.ToString(CultureInfo.InvariantCulture), Encode(ops, types), types));
				}
			}
		}

		return result;
	}

	private string Encode(List<RegistrationOp> ops, List<ITypeSymbol> types)
	{
		int TypeIndex(ITypeSymbol type)
		{
			var index = types.FindIndex(t => SymbolEqualityComparer.Default.Equals(t, type));
			if (index >= 0) return index;
			types.Add(type);
			return types.Count - 1;
		}

		string TypeList(List<ITypeSymbol>? list) => list is null ? "?" : string.Join(",", list.Select(t => TypeIndex(t).ToString(CultureInfo.InvariantCulture)));

		static string Flag(bool conditional) => conditional ? "1" : "0";
		static string Name(string? name) => name is null ? "" : "=" + name;

		var builder = new StringBuilder();
		foreach (var op in ops)
		{
			if (builder.Length > 0) builder.Append('\n');
			switch (op.Kind)
			{
				case OpKind.System:
					builder.Append($"S|{op.Site}|{Flag(op.Conditional)}|{TypeIndex(op.Service!)}|{TypeIndex(op.Implementation!)}");
					break;
				case OpKind.Function:
					builder.Append($"F|{op.Site}|{Flag(op.Conditional)}|{op.Stage}|{op.Order.ToString(CultureInfo.InvariantCulture)}|{Name(op.Name)}|{TypeList(op.After)}|{TypeList(op.Before)}|{TypeList(op.Services)}");
					break;
				case OpKind.Middleware:
					builder.Append($"D|{op.Site}|{Flag(op.Conditional)}|{op.Stage}|{op.Order.ToString(CultureInfo.InvariantCulture)}|{Name(op.Name)}");
					break;
				case OpKind.Call:
					builder.Append($"C|{Flag(op.Conditional)}|{op.Target!.GetDocumentationCommentId()}|{op.ParameterIndex.ToString(CultureInfo.InvariantCulture)}");
					break;
				case OpKind.Opaque:
					builder.Append($"O|{Flag(op.Conditional)}");
					break;
				default:
					builder.Append($"U|{Flag(op.Conditional)}");
					break;
			}
		}

		return builder.ToString();
	}

	private List<RegistrationOp> ExpandSummary(IMethodSymbol method, int parameter)
	{
		var key = (method, parameter, true);
		if (_programs.TryGetValue(key, out var cached)) return cached;
		if (!_inProgress.Add(key)) return [new RegistrationOp { Kind = OpKind.Opaque, Reason = $"'{method.Name}' is recursive" }];

		var ops = new List<RegistrationOp>();
		var id = method.GetDocumentationCommentId();
		var summaries = SummariesOf(method.ContainingAssembly);

		if (id is null || !summaries.TryGetValue(id + "#" + parameter.ToString(CultureInfo.InvariantCulture), out var summary))
		{
			ops.Add(new RegistrationOp { Kind = OpKind.Opaque, Reason = $"'{method.ContainingType?.Name}.{method.Name}' has no registration summary (its assembly was compiled without the Ion generator)" });
		}
		else
		{
			ops.AddRange(Decode(summary.Ops, summary.Types));
		}

		_inProgress.Remove(key);
		_programs[key] = ops;
		return ops;
	}

	private IEnumerable<RegistrationOp> Decode(string encoded, ImmutableArrayWrapper types)
	{
		if (encoded.Length == 0) yield break;

		ITypeSymbol? Type(string index) => int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < types.Count ? types[i] : null;

		List<ITypeSymbol>? TypeList(string list)
		{
			if (list == "?") return null;
			if (list.Length == 0) return [];
			var result = new List<ITypeSymbol>();
			foreach (var part in list.Split(','))
			{
				if (Type(part) is { } type) result.Add(type);
			}

			return result;
		}

		static int Int(string value) => int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
		static string? Name(string value) => value.Length == 0 ? null : value.Substring(1);

		foreach (var line in encoded.Split('\n'))
		{
			var parts = line.Split('|');
			switch (parts[0])
			{
				case "S" when parts.Length == 5:
					yield return new RegistrationOp { Kind = OpKind.System, Site = parts[1], Conditional = parts[2] == "1", Service = Type(parts[3]) as INamedTypeSymbol, Implementation = Type(parts[4]) as INamedTypeSymbol };
					break;
				case "F" when parts.Length == 9:
					yield return new RegistrationOp { Kind = OpKind.Function, Site = parts[1], Conditional = parts[2] == "1", Stage = Int(parts[3]), Order = Int(parts[4]), Name = Name(parts[5]), After = TypeList(parts[6]), Before = TypeList(parts[7]), Services = TypeList(parts[8]) ?? [] };
					break;
				case "D" when parts.Length == 6:
					yield return new RegistrationOp { Kind = OpKind.Middleware, Site = parts[1], Conditional = parts[2] == "1", Stage = Int(parts[3]), Order = Int(parts[4]), Name = Name(parts[5]) };
					break;
				case "C" when parts.Length == 4:
					var conditional = parts[1] == "1";
					if (DocumentationCommentId.GetFirstSymbolForDeclarationId(parts[2], _compilation) is IMethodSymbol target)
					{
						foreach (var op in ExpandSummary(target, Int(parts[3]))) yield return op.Nested(conditional, null);
					}
					else
					{
						yield return new RegistrationOp { Kind = OpKind.Opaque, Conditional = conditional, Reason = $"'{parts[2]}' was not found" };
					}

					break;
				case "O":
					yield return new RegistrationOp { Kind = OpKind.Opaque, Conditional = parts.Length > 1 && parts[1] == "1", Reason = "an opaque call in a referenced assembly" };
					break;
				default:
					yield return new RegistrationOp { Kind = OpKind.Unmatchable, Conditional = parts.Length > 1 && parts[1] == "1", Reason = "a registration in a referenced assembly that was not intercepted" };
					break;
			}
		}
	}

	private Dictionary<string, (string Ops, ImmutableArrayWrapper Types)> SummariesOf(IAssemblySymbol assembly)
	{
		if (_summaries.TryGetValue(assembly, out var summaries)) return summaries;

		summaries = [];
		foreach (var attribute in assembly.GetAttributes())
		{
			if (!KnownSymbols.Is(attribute.AttributeClass, _known.ScheduleRegistrationsAttribute) || attribute.ConstructorArguments.Length != 3) continue;
			if (attribute.ConstructorArguments[0].Value is not string key || attribute.ConstructorArguments[1].Value is not string ops) continue;

			var types = attribute.ConstructorArguments[2].Kind == TypedConstantKind.Array
				? attribute.ConstructorArguments[2].Values.Select(v => v.Value as ITypeSymbol).ToList()
				: [];
			summaries[key] = (ops, new ImmutableArrayWrapper(types));
		}

		_summaries[assembly] = summaries;
		return summaries;
	}

	#endregion

	/// <summary>A list of possibly unresolved types from an attribute.</summary>
	private sealed class ImmutableArrayWrapper(List<ITypeSymbol?> types)
	{
		public int Count => types.Count;
		public ITypeSymbol? this[int index] => types[index];
	}

	private sealed class MethodKeyComparer : IEqualityComparer<(IMethodSymbol, int, bool)>
	{
		public bool Equals((IMethodSymbol, int, bool) x, (IMethodSymbol, int, bool) y) =>
			SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && x.Item2 == y.Item2 && x.Item3 == y.Item3;

		public int GetHashCode((IMethodSymbol, int, bool) obj) =>
			(SymbolEqualityComparer.Default.GetHashCode(obj.Item1) * 31 + obj.Item2) * 2 + (obj.Item3 ? 1 : 0);
	}
}

internal static class SymbolPairExtensions
{
	/// <summary>Null when either type is not a closed, constructed type.</summary>
	public static (INamedTypeSymbol? Service, INamedTypeSymbol? Implementation) Closed(this (INamedTypeSymbol? Service, INamedTypeSymbol? Implementation) pair)
	{
		static bool IsClosed(INamedTypeSymbol? type) => type is not null && !type.IsUnboundGenericType && type.TypeKind != TypeKind.Error && !type.TypeArguments.Any(t => t is ITypeParameterSymbol);
		return IsClosed(pair.Service) && IsClosed(pair.Implementation) ? pair : (null, null);
	}
}
