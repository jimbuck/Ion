using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ion;

/// <summary>
/// The registrations of one schedule (the application's root schedule, or one scene's): systems, function steps and
/// legacy middleware, in registration order. It is a description only; <see cref="Plan(IServiceProvider?)"/> turns it into
/// a validated, ordered <see cref="SchedulePlan"/> and <see cref="Build"/> binds that plan to instances as a runnable
/// <see cref="Ion.Schedule"/>.
/// </summary>
/// <remarks>
/// Registration order only breaks ties between steps of equal <see cref="StageAttribute.Order"/>, so engine systems and
/// user systems can be registered in any order.
/// </remarks>
public sealed class ScheduleModel
{
	private readonly List<ScheduleEntry> _entries = [];
	private readonly List<NestedScheduleEntry> _nested = [];
	private bool _warningsLogged;

	/// <summary>Creates an empty model.</summary>
	/// <param name="name">The name printed for the schedule (<c>root</c> for the application).</param>
	/// <param name="isRoot">Whether the schedule is resolved from the root service provider (scoped services are then an error, ION006).</param>
	public ScheduleModel(string name = "root", bool isRoot = true)
	{
		Name = name;
		IsRoot = isRoot;
	}

	/// <summary>The name printed for the schedule.</summary>
	public string Name { get; }

	/// <summary>Whether the schedule is resolved from the root service provider.</summary>
	public bool IsRoot { get; }

	/// <summary>The registrations, in registration order.</summary>
	public IReadOnlyList<ScheduleEntry> Entries => _entries;

	/// <summary>Schedules run by a step of this one (the scenes), planned and printed with it.</summary>
	public IReadOnlyList<NestedScheduleEntry> Nested => _nested;

	/// <summary>
	/// Whether the model has been built and no longer accepts registrations (scene models, once their scene is loaded).
	/// Registering on a frozen model throws ION004.
	/// </summary>
	public bool IsFrozen { get; private set; }

	/// <summary>
	/// Adds a system: every public method of <paramref name="implementationType"/> with a stage attribute becomes a step,
	/// and every <see cref="BeginAttribute"/>/<see cref="EndAttribute"/> pair a scope. The instance is resolved as
	/// <paramref name="serviceType"/> when the schedule is built.
	/// </summary>
	public SystemEntry AddSystem(Type serviceType, [DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] Type implementationType)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		ArgumentNullException.ThrowIfNull(implementationType);
		return Add(new SystemEntry(serviceType, implementationType));
	}

	/// <summary>
	/// Adds a system described at compile time by the Ion source generator: planned like
	/// <see cref="AddSystem(Type, Type)"/>, without reflection, and bound through the generated delegates.
	/// </summary>
	/// <param name="system">The generated description.</param>
	/// <param name="site">The generated call site (see <see cref="ScheduleEntry.Site"/>).</param>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public SystemEntry AddSystem(GeneratedSystem system, string? site = null)
	{
		ArgumentNullException.ThrowIfNull(system);
		return Add(new SystemEntry(system), site);
	}

	/// <summary>
	/// Adds a function step: <paramref name="bind"/> is called once when the schedule is built and returns the per-frame
	/// delegate. <paramref name="serviceTypes"/> lists the services it resolves, for validation.
	/// </summary>
	/// <param name="stage">The stage to run in.</param>
	/// <param name="function">The user delegate (used for its name and its <see cref="OrderingAttribute"/>s).</param>
	/// <param name="serviceTypes">The services <paramref name="bind"/> resolves.</param>
	/// <param name="bind">Creates the per-frame delegate from the schedule's services.</param>
	/// <param name="order">The step order.</param>
	/// <param name="name">The printed name; derived from <paramref name="function"/> when null.</param>
	public FunctionEntry AddFunction(Stage stage, Delegate function, IReadOnlyList<Type> serviceTypes, Func<IServiceProvider, GameLoopDelegate> bind, int order = StageOrder.Default, string? name = null)
	{
		ArgumentNullException.ThrowIfNull(function);
		ArgumentNullException.ThrowIfNull(serviceTypes);
		ArgumentNullException.ThrowIfNull(bind);
		return Add(new FunctionEntry(stage, order, name ?? DescribeFunction(function, serviceTypes), function, serviceTypes, bind));
	}

	/// <summary>
	/// Adds a function step whose name and ordering constraints were computed at compile time by the Ion source generator
	/// (no reflection over the delegate).
	/// </summary>
	/// <param name="stage">The stage to run in.</param>
	/// <param name="function">The user delegate.</param>
	/// <param name="serviceTypes">The services <paramref name="bind"/> resolves.</param>
	/// <param name="bind">Creates the per-frame delegate from the schedule's services.</param>
	/// <param name="order">The step order.</param>
	/// <param name="name">The printed name; derived from <paramref name="function"/> when null.</param>
	/// <param name="after">The <see cref="AfterAttribute{T}"/> targets; read from the delegate's method when null.</param>
	/// <param name="before">The <see cref="BeforeAttribute{T}"/> targets; read from the delegate's method when null.</param>
	/// <param name="site">The generated call site (see <see cref="ScheduleEntry.Site"/>).</param>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public FunctionEntry AddFunction(Stage stage, Delegate function, IReadOnlyList<Type> serviceTypes, Func<IServiceProvider, GameLoopDelegate> bind, int order, string? name, IReadOnlyList<Type>? after, IReadOnlyList<Type>? before, string? site)
	{
		ArgumentNullException.ThrowIfNull(function);
		ArgumentNullException.ThrowIfNull(serviceTypes);
		ArgumentNullException.ThrowIfNull(bind);
		return Add(new FunctionEntry(stage, order, name ?? DescribeFunction(function, serviceTypes), function, serviceTypes, bind) { After = after, Before = before }, site);
	}

	/// <summary>
	/// Adds a legacy middleware (<c>next =&gt; dt =&gt; { ...; next(dt); }</c>). It wraps every step that sorts after it in
	/// the stage, and building the schedule logs warning ION010.
	/// </summary>
	public MiddlewareEntry AddMiddleware(Stage stage, Func<GameLoopDelegate, GameLoopDelegate> middleware, int order = StageOrder.Default, string? name = null)
	{
		ArgumentNullException.ThrowIfNull(middleware);
		return Add(new MiddlewareEntry(stage, order, name ?? DescribeDelegate(middleware), middleware));
	}

	/// <summary>
	/// Adds a legacy middleware registered through a generated call site (see <see cref="AddMiddleware(Stage, Func{GameLoopDelegate, GameLoopDelegate}, int, string?)"/>).
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public MiddlewareEntry AddMiddleware(Stage stage, Func<GameLoopDelegate, GameLoopDelegate> middleware, int order, string? name, string? site)
	{
		ArgumentNullException.ThrowIfNull(middleware);
		return Add(new MiddlewareEntry(stage, order, name ?? DescribeDelegate(middleware), middleware), site);
	}

	/// <summary>
	/// The schedule the Ion source generator emitted for this model, set by generated code before the schedule is built
	/// (see <see cref="UseGenerated"/>).
	/// </summary>
	public GeneratedScheduleFactory? Generated { get; private set; }

	/// <summary>
	/// Uses <paramref name="factory"/> when the schedule is built, if the registrations made at run time are the ones the
	/// generator saw; otherwise the runtime binds the plan as usual. Called by generated code.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public void UseGenerated(GeneratedScheduleFactory? factory) => Generated = factory;

	/// <summary>
	/// Declares a schedule run by a step of this one (a scene run by the scene system), so that it is validated when this
	/// schedule is planned and printed with it.
	/// </summary>
	/// <param name="name">The printed name (for example <c>Scene 1</c>).</param>
	/// <param name="owner">The system whose steps run the nested schedule.</param>
	/// <param name="plan">Plans the nested schedule from this schedule's services.</param>
	public NestedScheduleEntry AddNested(string name, Type owner, Func<IServiceProvider?, SchedulePlan> plan)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(plan);
		EnsureNotFrozen(name);
		var entry = new NestedScheduleEntry(name, owner, plan);
		_nested.RemoveAll(n => n.Name == name);
		_nested.Add(entry);
		return entry;
	}

	/// <summary>
	/// Plans the schedule: discovers the steps and scopes, validates them and sorts every stage. With
	/// <paramref name="services"/>, also checks that systems and injected services are registered with a valid lifetime,
	/// and plans the nested schedules.
	/// </summary>
	/// <exception cref="IonScheduleException">The schedule has errors.</exception>
	public SchedulePlan Plan(IServiceProvider? services = null) => SchedulePlanner.Plan(this, services);

	/// <summary>
	/// Plans the schedule (see <see cref="Plan"/>), logs its warnings (once per model, category <c>Ion.Schedule</c>) and
	/// binds it: resolves every system and injected service from <paramref name="services"/> and creates the per-stage
	/// runners.
	/// </summary>
	/// <param name="services">The services to resolve systems and injected parameters from.</param>
	/// <param name="logWarnings">Whether to log the plan's warnings (the first time this model is built).</param>
	/// <exception cref="IonScheduleException">The schedule has errors.</exception>
	public Schedule Build(IServiceProvider services, bool logWarnings = true)
	{
		ArgumentNullException.ThrowIfNull(services);

		var plan = Plan(services);

		if (logWarnings && !_warningsLogged)
		{
			_warningsLogged = true;
			LogWarnings(plan, services.GetService<ILoggerFactory>()?.CreateLogger("Ion.Schedule"));
		}

		if (Generated is { } generated)
		{
			var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Ion.Schedule");
			if (generated.TryCreate(this, plan, services, out var schedule, out var mismatch))
			{
				logger?.LogDebug("Schedule '{Schedule}' runs the generated schedule of {Name}.", Name, generated.Name);
				return new Schedule(plan, schedule);
			}

			logger?.LogDebug("The generated schedule {Name} is not used for schedule '{Schedule}': {Reason} The runtime binds it instead.", generated.Name, Name, mismatch);
		}

		return new Schedule(plan, services);
	}

	/// <summary>Stops accepting registrations (see <see cref="IsFrozen"/>).</summary>
	public void Freeze() => IsFrozen = true;

	private static void LogWarnings(SchedulePlan plan, ILogger? logger)
	{
		if (logger is null) return;

		foreach (var diagnostic in plan.AllDiagnostics())
		{
			if (diagnostic.Severity == ScheduleDiagnosticSeverity.Warning) logger.LogWarning("{Diagnostic}", diagnostic.ToString());
		}
	}

	private T Add<T>(T entry, string? site = null) where T : ScheduleEntry
	{
		EnsureNotFrozen(entry.ToString());
		entry.Index = _entries.Count;
		entry.Site = site;
		_entries.Add(entry);
		return entry;
	}

	private void EnsureNotFrozen(string? what)
	{
		if (!IsFrozen) return;
		throw new IonScheduleException([new ScheduleDiagnostic(ScheduleDiagnosticCodes.UnreachableStep, ScheduleDiagnosticSeverity.Error,
			$"'{what}' was registered on schedule '{Name}' after it was built, so it can never run. Register steps inside the configure callback.")]);
	}

	/// <summary>
	/// Returns a <see cref="GameLoopDelegate"/> that calls the same method as <paramref name="step"/> directly (no extra
	/// delegate hop), or one that invokes <paramref name="step"/> when the method cannot be rebound.
	/// </summary>
	public static GameLoopDelegate AsGameLoopDelegate(Action<GameTime> step)
	{
		ArgumentNullException.ThrowIfNull(step);

		if (step.HasSingleTarget && TryGetMethod(step) is { } method)
		{
			try
			{
				return method.CreateDelegate<GameLoopDelegate>(step.Target);
			}
			catch (Exception ex) when (ex is ArgumentException or MemberAccessException or NotSupportedException or InvalidOperationException)
			{
				// Fall through to the wrapper.
			}
		}

		return dt => step(dt);
	}

	/// <summary>
	/// The method of <paramref name="function"/>, or null when the runtime cannot provide it (reflection metadata removed
	/// by NativeAOT).
	/// </summary>
	internal static MethodInfo? TryGetMethod(Delegate function)
	{
		try
		{
			return function.Method;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or MemberAccessException)
		{
			return null;
		}
	}

	// Lambdas have no name of their own: list the services they inject to tell them apart.
	private static string DescribeFunction(Delegate function, IReadOnlyList<Type> serviceTypes)
	{
		var name = DescribeDelegate(function);
		return name.EndsWith("lambda", StringComparison.Ordinal) ? $"{name}({string.Join(", ", serviceTypes.Select(t => t.Name))})" : name;
	}

	/// <summary>
	/// A readable name for a delegate: <c>Type.Method</c>, <c>Type.LocalFunction</c>, or <c>Type.lambda</c> for a lambda.
	/// </summary>
	public static string DescribeDelegate(Delegate function)
	{
		ArgumentNullException.ThrowIfNull(function);

		if (TryGetMethod(function) is not { } method) return "function";

		var type = method.DeclaringType;
		while (type is not null && type.Name.StartsWith('<') && type.DeclaringType is not null) type = type.DeclaringType;
		var typeName = type is null ? "" : (type.Name.StartsWith('<') ? "" : type.Name + ".");

		var name = method.Name;
		if (!name.Contains('<')) return typeName + name;

		// Local function: <Outer>g__Name|0_0
		var local = name.IndexOf(">g__", StringComparison.Ordinal);
		if (local >= 0)
		{
			var start = local + 4;
			var end = name.IndexOf('|', start);
			if (end > start) return typeName + name[start..end];
		}

		return typeName + "lambda";
	}
}

/// <summary>A registration in a <see cref="ScheduleModel"/>.</summary>
public abstract class ScheduleEntry
{
	/// <summary>The registration index (position in <see cref="ScheduleModel.Entries"/>), used to break order ties.</summary>
	public int Index { get; internal set; }

	/// <summary>
	/// The call site that made the registration, when it was made by code the Ion source generator intercepted
	/// (<c>Assembly#n</c>); null for registrations made by reflection-bound calls.
	/// </summary>
	public string? Site { get; internal set; }
}

/// <summary>A system registered with <c>UseSystem</c>.</summary>
public sealed class SystemEntry : ScheduleEntry
{
	internal SystemEntry(Type serviceType, [DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] Type implementationType)
	{
		ServiceType = serviceType;
		ImplementationType = implementationType;
	}

	internal SystemEntry(GeneratedSystem generated)
	{
		ServiceType = generated.ServiceType;
		ImplementationType = generated.ImplementationType;
		Generated = generated;
	}

	/// <summary>The compile-time description, for systems registered by generated code; null for reflection-bound ones.</summary>
	public GeneratedSystem? Generated { get; }

	/// <summary>The type the instance is resolved as.</summary>
	public Type ServiceType { get; }

	/// <summary>The type whose methods are the system's steps.</summary>
	[DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)]
	public Type ImplementationType { get; }

	/// <inheritdoc/>
	public override string ToString() => Generated?.Name ?? ImplementationType.Name;
}

/// <summary>A function step (a delegate with injected parameters).</summary>
public sealed class FunctionEntry : ScheduleEntry
{
	internal FunctionEntry(Stage stage, int order, string name, Delegate function, IReadOnlyList<Type> serviceTypes, Func<IServiceProvider, GameLoopDelegate> bind)
	{
		Stage = stage;
		Order = order;
		Name = name;
		Function = function;
		ServiceTypes = serviceTypes;
		Bind = bind;
	}

	/// <summary>The stage the step runs in.</summary>
	public Stage Stage { get; }

	/// <summary>The step order.</summary>
	public int Order { get; }

	/// <summary>The printed name.</summary>
	public string Name { get; }

	/// <summary>The user delegate.</summary>
	public Delegate Function { get; }

	/// <summary>The injected service types.</summary>
	public IReadOnlyList<Type> ServiceTypes { get; }

	/// <summary>Creates the per-frame delegate from the schedule's services.</summary>
	public Func<IServiceProvider, GameLoopDelegate> Bind { get; }

	/// <summary>The <see cref="AfterAttribute{T}"/> targets computed at compile time; null to read them from the delegate.</summary>
	public IReadOnlyList<Type>? After { get; internal init; }

	/// <summary>The <see cref="BeforeAttribute{T}"/> targets computed at compile time; null to read them from the delegate.</summary>
	public IReadOnlyList<Type>? Before { get; internal init; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>A legacy middleware registered with <c>UseInit</c>, <c>UseUpdate</c>, ... (a delegate taking <c>next</c>).</summary>
public sealed class MiddlewareEntry : ScheduleEntry
{
	internal MiddlewareEntry(Stage stage, int order, string name, Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		Stage = stage;
		Order = order;
		Name = name;
		Middleware = middleware;
	}

	/// <summary>The stage the middleware runs in.</summary>
	public Stage Stage { get; }

	/// <summary>The middleware order.</summary>
	public int Order { get; }

	/// <summary>The printed name.</summary>
	public string Name { get; }

	/// <summary>The middleware factory, called once per build with the rest of the stage as <c>next</c>.</summary>
	public Func<GameLoopDelegate, GameLoopDelegate> Middleware { get; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>A schedule run by a step of another one (a scene).</summary>
/// <param name="Name">The printed name.</param>
/// <param name="Owner">The system whose steps run it.</param>
/// <param name="Plan">Plans it from the owner schedule's services (null to plan without service checks).</param>
public sealed record NestedScheduleEntry(string Name, Type Owner, Func<IServiceProvider?, SchedulePlan> Plan);
