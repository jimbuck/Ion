using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Ion;

/// <summary>The kind of a <see cref="GeneratedStep"/>.</summary>
public enum GeneratedStepKind
{
	/// <summary>A leaf step.</summary>
	Step,
	/// <summary>A <see cref="BeginAttribute"/>/<see cref="EndAttribute"/> pair.</summary>
	Scope,
	/// <summary>A legacy middleware method (<c>(GameTime dt, GameLoopDelegate next)</c> or <c>(GameLoopDelegate next)</c>).</summary>
	Middleware,
}

/// <summary>
/// A system described at compile time by the Ion source generator (<c>Ion.Generators</c>): its steps, scopes and
/// diagnostics, and delegates that bind each step to an instance with direct calls. <see cref="ScheduleModel.AddSystem(GeneratedSystem, string?)"/>
/// plans it exactly like a system discovered by reflection, without reflection.
/// </summary>
/// <remarks>Emitted by the generator; not meant to be written by hand.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedSystem
{
	/// <summary>Creates the description.</summary>
	/// <param name="serviceType">The type the instance is resolved as.</param>
	/// <param name="implementationType">The type whose methods are the steps.</param>
	/// <param name="name">The printed name of the implementation type (its <see cref="System.Reflection.MemberInfo.Name"/>).</param>
	/// <param name="steps">The steps, scopes and legacy middleware, in discovery order.</param>
	/// <param name="diagnostics">The problems found in the system (ION001, ION003 to ION005, ION007, ION010, ION011, ION013), in discovery order.</param>
	public GeneratedSystem(Type serviceType, [DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] Type implementationType, string name, GeneratedStep[] steps, ScheduleDiagnostic[] diagnostics)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		ArgumentNullException.ThrowIfNull(implementationType);
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(steps);
		ArgumentNullException.ThrowIfNull(diagnostics);

		ServiceType = serviceType;
		ImplementationType = implementationType;
		Name = name;
		Steps = steps;
		Diagnostics = diagnostics;
	}

	/// <summary>The type the instance is resolved as.</summary>
	public Type ServiceType { get; }

	/// <summary>The type whose methods are the steps.</summary>
	[DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)]
	public Type ImplementationType { get; }

	/// <summary>The printed name of the implementation type.</summary>
	public string Name { get; }

	/// <summary>The steps, scopes and legacy middleware, in discovery order.</summary>
	public IReadOnlyList<GeneratedStep> Steps { get; }

	/// <summary>The problems found in the system, without the schedule name prefix.</summary>
	public IReadOnlyList<ScheduleDiagnostic> Diagnostics { get; }

	/// <summary>The step of <paramref name="stage"/> and <paramref name="kind"/> declared by <paramref name="method"/> at <paramref name="declarationIndex"/>.</summary>
	/// <exception cref="InvalidOperationException">There is no such step.</exception>
	public GeneratedStep GetStep(Stage stage, GeneratedStepKind kind, string method, int declarationIndex)
	{
		foreach (var step in Steps)
		{
			if (step.Stage == stage && step.Kind == kind && step.Method == method && step.DeclarationIndex == declarationIndex) return step;
		}

		throw new InvalidOperationException($"System '{Name}' has no {kind} '{method}' in {stage}.");
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>
/// One step, scope or legacy middleware of a <see cref="GeneratedSystem"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedStep
{
	/// <summary>Creates the description.</summary>
	/// <param name="stage">The stage.</param>
	/// <param name="kind">The kind of step.</param>
	/// <param name="order">The order (for a scope, the <see cref="BeginAttribute"/> order).</param>
	/// <param name="method">The method name (for a scope, the begin method).</param>
	/// <param name="declarationIndex">The position of the method among the system's methods (a tie breaker).</param>
	public GeneratedStep(Stage stage, GeneratedStepKind kind, int order, string method, int declarationIndex)
	{
		ArgumentNullException.ThrowIfNull(method);

		Stage = stage;
		Kind = kind;
		Order = order;
		Method = method;
		DeclarationIndex = declarationIndex;
	}

	/// <summary>The stage.</summary>
	public Stage Stage { get; }

	/// <summary>The kind of step.</summary>
	public GeneratedStepKind Kind { get; }

	/// <summary>The order (for a scope, the <see cref="BeginAttribute"/> order).</summary>
	public int Order { get; }

	/// <summary>The method name (for a scope, the begin method).</summary>
	public string Method { get; }

	/// <summary>The position of the method among the system's methods.</summary>
	public int DeclarationIndex { get; }

	/// <summary>Whether the method is static (no instance is resolved for it).</summary>
	public bool IsStatic { get; init; }

	/// <summary>The <see cref="AfterAttribute{T}"/> targets (class and method), without duplicates.</summary>
	public Type[] After { get; init; } = [];

	/// <summary>The <see cref="BeforeAttribute{T}"/> targets (class and method), without duplicates.</summary>
	public Type[] Before { get; init; } = [];

	/// <summary>The services injected into the method's parameters.</summary>
	public Type[] Services { get; init; } = [];

	/// <summary>Binds the step (or the scope's begin method) to the system instance (null for a static method).</summary>
	public Func<object?, IServiceProvider, GameLoopDelegate>? Bind { get; init; }

	/// <summary>For a scope: the end method name.</summary>
	public string? EndMethod { get; init; }

	/// <summary>For a scope: whether the end method is static.</summary>
	public bool EndIsStatic { get; init; }

	/// <summary>For a scope: the services injected into the end method's parameters.</summary>
	public Type[] EndServices { get; init; } = [];

	/// <summary>For a scope: binds the end method.</summary>
	public Func<object?, IServiceProvider, GameLoopDelegate>? BindEnd { get; init; }

	/// <summary>For a scope: its <see cref="ScopeAttribute.ScopeName"/>.</summary>
	public string? ScopeName { get; init; }

	/// <summary>For a legacy middleware: binds the method as a middleware factory.</summary>
	public Func<object?, IServiceProvider, Func<GameLoopDelegate, GameLoopDelegate>>? BindMiddleware { get; init; }

	/// <inheritdoc/>
	public override string ToString() => $"{Stage} {Order} {Method}";
}

/// <summary>
/// Adapters from step shapes to <see cref="GameLoopDelegate"/> that do not show in stack traces. Used by the function
/// step extensions and by generated code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class StepAdapters
{
	/// <summary>A step without parameters.</summary>
	public static GameLoopDelegate FromAction(Action step) => new ActionStep(step).Invoke;

	/// <summary>A function step without injected services.</summary>
	public static GameLoopDelegate FromFunction(Action<GameTime> step) => new FunctionStep(step).Invoke;

	/// <summary>A function step with one injected service.</summary>
	public static GameLoopDelegate FromFunction<T0>(Action<GameTime, T0> step, T0 s0) => new FunctionStep<T0>(step, s0).Invoke;

	/// <summary>A function step with two injected services.</summary>
	public static GameLoopDelegate FromFunction<T0, T1>(Action<GameTime, T0, T1> step, T0 s0, T1 s1) => new FunctionStep<T0, T1>(step, s0, s1).Invoke;

	/// <summary>A function step with three injected services.</summary>
	public static GameLoopDelegate FromFunction<T0, T1, T2>(Action<GameTime, T0, T1, T2> step, T0 s0, T1 s1, T2 s2) => new FunctionStep<T0, T1, T2>(step, s0, s1, s2).Invoke;

	/// <summary>A function step with four injected services.</summary>
	public static GameLoopDelegate FromFunction<T0, T1, T2, T3>(Action<GameTime, T0, T1, T2, T3> step, T0 s0, T1 s1, T2 s2, T3 s3) => new FunctionStep<T0, T1, T2, T3>(step, s0, s1, s2, s3).Invoke;

	/// <summary>A legacy <c>(GameTime dt, GameLoopDelegate next)</c> middleware as a middleware factory.</summary>
	public static Func<GameLoopDelegate, GameLoopDelegate> FromMiddleware(Action<GameTime, GameLoopDelegate> middleware) => next => new MiddlewareStep(middleware, next).Invoke;

	[StackTraceHidden]
	private sealed class ActionStep(Action step)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step();
	}

	[StackTraceHidden]
	private sealed class FunctionStep(Action<GameTime> step)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step(dt);
	}

	[StackTraceHidden]
	private sealed class FunctionStep<T0>(Action<GameTime, T0> step, T0 s0)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step(dt, s0);
	}

	[StackTraceHidden]
	private sealed class FunctionStep<T0, T1>(Action<GameTime, T0, T1> step, T0 s0, T1 s1)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step(dt, s0, s1);
	}

	[StackTraceHidden]
	private sealed class FunctionStep<T0, T1, T2>(Action<GameTime, T0, T1, T2> step, T0 s0, T1 s1, T2 s2)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step(dt, s0, s1, s2);
	}

	[StackTraceHidden]
	private sealed class FunctionStep<T0, T1, T2, T3>(Action<GameTime, T0, T1, T2, T3> step, T0 s0, T1 s1, T2 s2, T3 s3)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => step(dt, s0, s1, s2, s3);
	}

	[StackTraceHidden]
	private sealed class MiddlewareStep(Action<GameTime, GameLoopDelegate> middleware, GameLoopDelegate next)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt) => middleware(dt, next);
	}
}
