using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Ion.Generators;

/// <summary>The kind of a <see cref="RegistrationOp"/>.</summary>
internal enum OpKind
{
	/// <summary>A system registration made by an intercepted <c>UseSystem</c> call.</summary>
	System,
	/// <summary>A function step registration made by an intercepted <c>app.Update(...)</c> (and friends).</summary>
	Function,
	/// <summary>A legacy middleware delegate registration made by an intercepted <c>UseUpdate(next => ...)</c> (and friends).</summary>
	Middleware,
	/// <summary>A call to a method of another assembly that receives the builder, expanded from that assembly's summary.</summary>
	Call,
	/// <summary>A call the generator cannot follow; it may register anything (the runtime checks).</summary>
	Opaque,
	/// <summary>A registration that is not intercepted (a <c>Type</c> only known at run time, a system that cannot be described): the generated schedule can never match.</summary>
	Unmatchable,
}

/// <summary>One registration (or call that may register) in a method body, in the order it runs.</summary>
internal sealed class RegistrationOp
{
	public OpKind Kind { get; init; }

	/// <summary>The generated call site (<c>Assembly#n</c>) of a System, Function or Middleware op.</summary>
	public string? Site { get; init; }

	/// <summary>Whether the op depends on a branch (if, loop, callback, conditional operator).</summary>
	public bool Conditional { get; init; }

	/// <summary>Where the op (or the call that led to it) is in this compilation, for diagnostics.</summary>
	public Location? Location { get; init; }

	public INamedTypeSymbol? Service { get; init; }
	public INamedTypeSymbol? Implementation { get; init; }

	public int Stage { get; init; }
	public int Order { get; init; }
	public string? Name { get; init; }
	public List<ITypeSymbol>? After { get; init; }
	public List<ITypeSymbol>? Before { get; init; }
	public List<ITypeSymbol> Services { get; init; } = [];

	/// <summary>For a function op of this compilation: the delegate type (<c>Action&lt;GameTime, ...&gt;</c>) the generated schedule invokes directly.</summary>
	public ITypeSymbol? DelegateType { get; init; }

	public IMethodSymbol? Target { get; init; }
	public int ParameterIndex { get; init; }
	public string? Reason { get; init; }

	public RegistrationOp Nested(bool conditional, Location? location) => new()
	{
		Kind = Kind,
		Site = Site,
		Conditional = Conditional || conditional,
		Location = location ?? Location,
		Service = Service,
		Implementation = Implementation,
		Stage = Stage,
		Order = Order,
		Name = Name,
		After = After,
		Before = Before,
		Services = Services,
		DelegateType = DelegateType,
		Target = Target,
		ParameterIndex = ParameterIndex,
		Reason = Reason,
	};

	public override string ToString() => Kind switch
	{
		OpKind.System => $"System {Implementation?.Name} @{Site}{(Conditional ? " (conditional)" : "")}",
		OpKind.Function => $"Function {Name} @{Site}{(Conditional ? " (conditional)" : "")}",
		OpKind.Middleware => $"Middleware {Name} @{Site}{(Conditional ? " (conditional)" : "")}",
		OpKind.Call => $"Call {Target?.Name}",
		_ => $"{Kind} {Reason}",
	};
}

/// <summary>The kind of call the generator intercepts.</summary>
internal enum CallKind
{
	UseSystem,
	Function,
	Middleware,
	DelegateServicesMiddleware,
	UseScene,
	Root,
}

/// <summary>A call the generator intercepts, with what the interceptor needs.</summary>
internal sealed class InterceptedCall
{
	public CallKind Kind { get; init; }
	public InvocationExpressionSyntax Invocation { get; init; } = null!;
	public IInvocationOperation Operation { get; init; } = null!;

	/// <summary>The invoked method, constructed and unreduced (an extension method's receiver is its first parameter).</summary>
	public IMethodSymbol Method => Operation.TargetMethod;
	public SemanticModel Model { get; init; } = null!;

	/// <summary>The index of the call among the intercepted calls of the compilation (by file, then position).</summary>
	public int Index { get; set; }

	/// <summary>The generated call site key (<c>Assembly#Index</c>).</summary>
	public string Site { get; set; } = "";

	/// <summary>The <c>[InterceptsLocation(...)]</c> attribute text.</summary>
	public string Attribute { get; set; } = "";

	public int Stage { get; init; }
	public SystemInfo? System { get; init; }
	public int Order { get; init; }
	public string? Name { get; init; }
	public List<ITypeSymbol>? After { get; init; }
	public List<ITypeSymbol>? Before { get; init; }
	public List<ITypeSymbol> Services { get; init; } = [];

	/// <summary>For a scene: the constant scene id, when known.</summary>
	public int? SceneId { get; init; }

	/// <summary>For a scene or a root: the schedule the generator emits for it (null when it cannot).</summary>
	public ScheduleCandidate? Candidate { get; set; }

	public RegistrationOp ToOp(bool conditional) => Kind switch
	{
		CallKind.UseSystem => new RegistrationOp
		{
			Kind = OpKind.System,
			Site = Site,
			Conditional = conditional,
			Location = Invocation.GetLocation(),
			Service = System!.Service,
			Implementation = System.Implementation,
		},
		CallKind.Function => new RegistrationOp
		{
			Kind = OpKind.Function,
			Site = Site,
			Conditional = conditional,
			Location = Invocation.GetLocation(),
			Stage = Stage,
			Order = Order,
			Name = Name,
			After = After,
			Before = Before,
			Services = Services,
			DelegateType = Method.Parameters[1].Type,
		},
		_ => new RegistrationOp
		{
			Kind = OpKind.Middleware,
			Site = Site,
			Conditional = conditional,
			Location = Invocation.GetLocation(),
			Stage = Stage,
			Order = 0,
			Name = Name,
		},
	};
}
