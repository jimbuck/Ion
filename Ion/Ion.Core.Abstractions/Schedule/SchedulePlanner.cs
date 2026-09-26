using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

namespace Ion;

/// <summary>
/// Turns a <see cref="ScheduleModel"/> into a <see cref="SchedulePlan"/>: discovers steps and scopes by reflection,
/// validates them, and sorts every stage (Kahn's algorithm over the Before/After constraints, taking the ready item with
/// the lowest order, then scopes before steps, then registration order, then declaration order).
/// </summary>
internal static class SchedulePlanner
{
	private const BindingFlags MethodFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	public static SchedulePlan Plan(ScheduleModel model, IServiceProvider? services)
	{
		var context = new Context(model);
		var items = new List<StepPlan>();

		foreach (var entry in model.Entries)
		{
			switch (entry)
			{
				case SystemEntry { Generated: { } generated } system:
					DiscoverGenerated(context, system, generated, items);
					break;

				case SystemEntry system:
					DiscoverSystem(context, system, items);
					break;

				case FunctionEntry function:
					context.CheckStage(function.Stage, function.Name);
					items.Add(new StepPlan(function.Stage, StepKind.Function, function.Order, function.Name)
					{
						Function = function,
						RegistrationIndex = function.Index,
						After = function.After is { } after ? [.. after] : Targets(FunctionConstraints(function), before: false),
						Before = function.Before is { } before ? [.. before] : Targets(FunctionConstraints(function), before: true),
						Services = function.ServiceTypes,
					});
					break;

				case MiddlewareEntry middleware:
					context.CheckStage(middleware.Stage, middleware.Name);
					context.Warning(ScheduleDiagnosticCodes.LegacyMiddleware,
						$"'{middleware.Name}' in {middleware.Stage} is a legacy middleware delegate (next => dt => ...). Rewrite it as a function step, for example app.{middleware.Stage}((GameTime dt, IMyService service) => ...), and move code that ran after next(dt) into a later step or a [Begin]/[End] scope.");
					items.Add(new StepPlan(middleware.Stage, StepKind.Middleware, middleware.Order, middleware.Name)
					{
						Middleware = middleware,
						RegistrationIndex = middleware.Index,
					});
					break;
			}
		}

		CheckConstraintTargets(context, items);

		if (services is not null) CheckServices(context, items, services);

		var stages = new StagePlan[7];
		foreach (var stage in Enum.GetValues<Stage>())
		{
			var stageItems = items.Where(i => i.Stage == stage).ToList();
			stages[(int)stage - 1] = new StagePlan(stage, Sort(context, stage, stageItems));
		}

		var nested = new List<NestedSchedulePlan>();
		if (services is not null)
		{
			foreach (var entry in model.Nested)
			{
				try
				{
					nested.Add(new NestedSchedulePlan(entry.Name, entry.Owner, entry.Plan(services)));
				}
				catch (IonScheduleException ex)
				{
					context.Errors.AddRange(ex.Diagnostics);
				}
			}
		}

		if (context.Errors.Count > 0) throw new IonScheduleException(context.Errors);

		return new SchedulePlan(model.Name, model.IsRoot, stages, context.Warnings, nested);
	}

	private static void DiscoverSystem(Context context, SystemEntry system, List<StepPlan> items)
	{
		var type = system.ImplementationType;
		var classConstraints = type.GetCustomAttributes<OrderingAttribute>(true).ToArray();
		var methods = GetMethodsInDeclarationOrder(type);
		var scopes = new Dictionary<(Stage Stage, string? Name), (List<(MethodInfo Method, ScopeAttribute Attribute, int Index)> Begins, List<(MethodInfo Method, ScopeAttribute Attribute, int Index)> Ends)>();
		var count = 0;

		// Methods a source generator expanded (a [Query] method and its generated loop): the expansion runs in their place.
		var expanded = new HashSet<string>(StringComparer.Ordinal);
		foreach (var method in methods)
		{
			if (method.GetCustomAttribute<ExpandedStepAttribute>(false) is { } expansion) expanded.Add(expansion.Method);
		}

		for (var index = 0; index < methods.Count; index++)
		{
			var method = methods[index];
			var stageAttributes = method.GetCustomAttributes<StageAttribute>(true).ToArray();
			var scopeAttributes = method.GetCustomAttributes<ScopeAttribute>(true).ToArray();
			if (stageAttributes.Length == 0 && scopeAttributes.Length == 0) continue;

			var binder = method.GetCustomAttribute<StepBinderAttribute>(true);
			var methodName = method.GetCustomAttribute<ExpandedStepAttribute>(false)?.Method ?? method.Name;
			if (binder is not null && methodName == method.Name && expanded.Contains(method.Name)) continue;

			var name = type.Name + "." + methodName;

			if (!method.IsPublic && binder is null)
			{
				context.Error(ScheduleDiagnosticCodes.UnreachableStep, $"'{name}' has a stage or scope attribute but is not public, so it can never run. Make it public.");
				continue;
			}

			var constraints = classConstraints.Concat(method.GetCustomAttributes<OrderingAttribute>(true)).ToArray();
			var after = Targets(constraints, before: false);
			var before = Targets(constraints, before: true);

			if (binder is not null)
			{
				// A step the attribute binds (the runtime path of [Query]): a leaf step whatever its parameters.
				var binderReason = scopeAttributes.Length > 0 ? "a step bound by an attribute cannot be a [Begin]/[End] scope" : binder.Validate(method);
				foreach (var attribute in stageAttributes)
				{
					if (!context.CheckStage(attribute.Stage, name)) continue;
					if (binderReason is not null)
					{
						context.Error(ScheduleDiagnosticCodes.InvalidSignature, $"'{name}' in {attribute.Stage} has an unsupported signature: {binderReason}.");
						continue;
					}

					items.Add(new StepPlan(attribute.Stage, StepKind.Step, attribute.Order, name)
					{
						System = system,
						Method = method,
						MethodName = methodName,
						Binder = binder,
						NeedsInstance = !method.IsStatic,
						Services = [.. binder.GetServices(method)],
						After = after,
						Before = before,
						RegistrationIndex = system.Index,
						DeclarationIndex = index,
					});
					count++;
				}

				if (stageAttributes.Length == 0) context.Error(ScheduleDiagnosticCodes.InvalidSignature, $"'{name}' has an unsupported signature: {binderReason ?? "a step bound by an attribute needs a stage attribute"}.");
				continue;
			}

			var signature = StepSignature.Classify(method, out var reason);

			foreach (var attribute in stageAttributes)
			{
				if (!context.CheckStage(attribute.Stage, name)) continue;

				switch (signature)
				{
					case SignatureKind.Async:
						context.Error(ScheduleDiagnosticCodes.AsyncStep, $"'{name}' in {attribute.Stage} is async or returns {method.ReturnType.Name}. Steps are synchronous: return void, and start background work from the step instead.");
						continue;

					case SignatureKind.Invalid:
						context.Error(ScheduleDiagnosticCodes.InvalidSignature, $"'{name}' in {attribute.Stage} has an unsupported signature: {reason}. Use void {method.Name}(GameTime dt) (extra parameters are injected services).");
						continue;

					case SignatureKind.LegacyVoidNext:
					case SignatureKind.LegacyFactory:
						context.Warning(ScheduleDiagnosticCodes.LegacyMiddleware,
							$"'{name}' in {attribute.Stage} uses the legacy middleware form (GameLoopDelegate next). Rewrite it as a leaf step, [{attribute.Stage}] public void {method.Name}(GameTime dt), without next(dt); move code that ran after next(dt) into a later step (a higher Order or [After<T>]) or a [Begin]/[End] scope.");
						items.Add(new StepPlan(attribute.Stage, StepKind.Middleware, attribute.Order, name)
						{
							System = system,
							Method = method,
							MethodName = methodName,
							NeedsInstance = !method.IsStatic,
							After = after,
							Before = before,
							RegistrationIndex = system.Index,
							DeclarationIndex = index,
						});
						count++;
						continue;

					default:
						items.Add(new StepPlan(attribute.Stage, StepKind.Step, attribute.Order, name)
						{
							System = system,
							Method = method,
							MethodName = methodName,
							NeedsInstance = !method.IsStatic,
							Services = [.. StepSignature.ServiceParameters(method)],
							After = after,
							Before = before,
							RegistrationIndex = system.Index,
							DeclarationIndex = index,
						});
						count++;
						continue;
				}
			}

			foreach (var attribute in scopeAttributes)
			{
				if (!context.CheckStage(attribute.Stage, name)) continue;

				if (signature == SignatureKind.Async)
				{
					context.Error(ScheduleDiagnosticCodes.AsyncStep, $"'{name}' ({(attribute is BeginAttribute ? "Begin" : "End")} {attribute.Stage}) is async or returns {method.ReturnType.Name}. Scope methods are synchronous.");
					continue;
				}

				if (signature is SignatureKind.Invalid or SignatureKind.LegacyFactory or SignatureKind.LegacyVoidNext)
				{
					context.Error(ScheduleDiagnosticCodes.InvalidSignature, $"'{name}' ({(attribute is BeginAttribute ? "Begin" : "End")} {attribute.Stage}) has an unsupported signature: {reason ?? "scope methods cannot take next"}. Use void {method.Name}(GameTime dt).");
					continue;
				}

				var key = (attribute.Stage, attribute.ScopeName);
				if (!scopes.TryGetValue(key, out var pair))
				{
					pair = ([], []);
					scopes[key] = pair;
				}

				(attribute is BeginAttribute ? pair.Begins : pair.Ends).Add((method, attribute, index));
			}
		}

		foreach (var ((stage, scopeName), (begins, ends)) in scopes)
		{
			var label = scopeName is null ? $"{type.Name} in {stage}" : $"{type.Name} scope '{scopeName}' in {stage}";

			if (begins.Count > 1 || ends.Count > 1)
			{
				context.Error(ScheduleDiagnosticCodes.AmbiguousScope, $"{label} has {begins.Count} [Begin] and {ends.Count} [End] methods ({string.Join(", ", begins.Concat(ends).Select(m => m.Method.Name))}). Give each pair a ScopeName.");
				continue;
			}

			if (begins.Count == 0)
			{
				context.Error(ScheduleDiagnosticCodes.UnpairedScope, $"{label}: [End] method '{ends[0].Method.Name}' has no matching [Begin({stage})]{(scopeName is null ? "" : $" with ScopeName \"{scopeName}\"")}.");
				continue;
			}

			if (ends.Count == 0)
			{
				context.Error(ScheduleDiagnosticCodes.UnpairedScope, $"{label}: [Begin] method '{begins[0].Method.Name}' has no matching [End({stage})]{(scopeName is null ? "" : $" with ScopeName \"{scopeName}\"")}.");
				continue;
			}

			var begin = begins[0];
			var end = ends[0];

			if (end.Attribute.HasOrder && end.Attribute.Order != begin.Attribute.Order)
			{
				context.Error(ScheduleDiagnosticCodes.AmbiguousScope, $"{label}: [End] '{end.Method.Name}' has Order {end.Attribute.Order} but [Begin] '{begin.Method.Name}' has Order {begin.Attribute.Order}. A scope has one order; set the same value or omit it on [End].");
				continue;
			}

			var constraints = classConstraints.Concat(begin.Method.GetCustomAttributes<OrderingAttribute>(true)).ToArray();
			items.Add(new StepPlan(stage, StepKind.Scope, begin.Attribute.Order, type.Name + "." + begin.Method.Name)
			{
				EndName = type.Name + "." + end.Method.Name,
				System = system,
				Method = begin.Method,
				EndMethod = end.Method,
				MethodName = begin.Method.Name,
				NeedsInstance = !begin.Method.IsStatic || !end.Method.IsStatic,
				Services = [.. StepSignature.ServiceParameters(begin.Method)],
				EndServices = [.. StepSignature.ServiceParameters(end.Method)],
				ScopeName = scopeName,
				After = Targets(constraints, before: false),
				Before = Targets(constraints, before: true),
				RegistrationIndex = system.Index,
				DeclarationIndex = begin.Index,
			});
			count++;
		}

		if (count == 0)
		{
			context.Warning(ScheduleDiagnosticCodes.SystemWithoutSteps, $"System '{type.Name}' has no public method with a stage attribute ([Init], [Update], ...) or [Begin]/[End], so it never runs.");
		}
	}

	private static void DiscoverGenerated(Context context, SystemEntry system, GeneratedSystem generated, List<StepPlan> items)
	{
		// The generator already validated the system with the same rules as DiscoverSystem; replay its diagnostics (in
		// discovery order) so that the runtime reports exactly what reflection would.
		foreach (var diagnostic in generated.Diagnostics)
		{
			if (diagnostic.Severity == ScheduleDiagnosticSeverity.Error) context.Error(diagnostic.Code, diagnostic.Message);
			else context.Warning(diagnostic.Code, diagnostic.Message);
		}

		var type = generated.Name;
		foreach (var step in generated.Steps)
		{
			var kind = step.Kind switch
			{
				GeneratedStepKind.Scope => StepKind.Scope,
				GeneratedStepKind.Middleware => StepKind.Middleware,
				_ => StepKind.Step,
			};

			items.Add(new StepPlan(step.Stage, kind, step.Order, type + "." + step.Method)
			{
				EndName = step.EndMethod is null ? null : type + "." + step.EndMethod,
				System = system,
				Generated = step,
				MethodName = step.Method,
				NeedsInstance = !step.IsStatic || (step.EndMethod is not null && !step.EndIsStatic),
				Services = step.Services,
				EndServices = step.EndServices,
				ScopeName = step.ScopeName,
				After = step.After,
				Before = step.Before,
				RegistrationIndex = system.Index,
				DeclarationIndex = step.DeclarationIndex,
			});
		}
	}

	private static List<MethodInfo> GetMethodsInDeclarationOrder([DynamicallyAccessedMembers(SystemMiddlewareBinder.MiddlewareAccessibility)] Type type)
	{
		// Base type methods first, then metadata (declaration) order. NativeAOT has no metadata tokens; there reflection
		// already returns methods in declaration order, so the index is the key (OrderBy is stable).
		return type.GetMethods(MethodFlags)
			.Where(m => m.DeclaringType != typeof(object))
			.Select((m, index) => (Method: m, Key: DeclarationKey(m, index)))
			.OrderBy(m => InheritanceDepth(m.Method.DeclaringType))
			.ThenBy(m => m.Key)
			.Select(m => m.Method)
			.ToList();
	}

	private static int DeclarationKey(MethodInfo method, int index)
	{
		try
		{
			return method.MetadataToken;
		}
		catch (InvalidOperationException)
		{
			return index;
		}
	}

	private static int InheritanceDepth(Type? type)
	{
		var depth = 0;
		for (var t = type?.BaseType; t is not null; t = t.BaseType) depth++;
		return depth;
	}

	private static IEnumerable<OrderingAttribute> FunctionConstraints(FunctionEntry function) =>
		ScheduleModel.TryGetMethod(function.Function)?.GetCustomAttributes<OrderingAttribute>(true) ?? [];

	private static Type[] Targets(IEnumerable<OrderingAttribute> constraints, bool before) => constraints.Where(c => c.IsBefore == before).Select(c => c.Target).Distinct().ToArray();

	private static bool Matches(Type target, StepPlan item) =>
		item.System is { } system && (target.IsAssignableFrom(system.ImplementationType) || target.IsAssignableFrom(system.ServiceType));

	private static void CheckConstraintTargets(Context context, List<StepPlan> items)
	{
		var systems = context.Model.Entries.OfType<SystemEntry>().ToArray();
		var reported = new HashSet<(string, Type)>();

		foreach (var item in items)
		{
			foreach (var target in item.After.Concat(item.Before))
			{
				if (systems.Any(s => target.IsAssignableFrom(s.ImplementationType) || target.IsAssignableFrom(s.ServiceType))) continue;

				var source = item.System?.ImplementationType.Name ?? item.Name;
				if (!reported.Add((source, target))) continue;

				context.Warning(ScheduleDiagnosticCodes.UnmatchedConstraint, $"'{item.Name}' is ordered relative to '{target.Name}', which is not a system of schedule '{context.Model.Name}'; the constraint has no effect.");
			}
		}
	}

	private static void CheckServices(Context context, List<StepPlan> items, IServiceProvider services)
	{
		var isService = services.GetService<IServiceProviderIsService>();
		var lifetimes = services.GetService<ServiceLifetimeIndex>();
		var root = context.Model.IsRoot;
		var checkedSystems = new HashSet<SystemEntry>();

		foreach (var item in items)
		{
			if (item.System is { } system && checkedSystems.Add(system) && items.Any(i => i.System == system && NeedsInstance(i)))
			{
				if (isService is not null && !isService.IsService(system.ServiceType))
				{
					context.Error(ScheduleDiagnosticCodes.UnregisteredSystem, $"System '{system.ServiceType.Name}' is not registered in the service collection. Register it (for example services.AddSingleton<{system.ServiceType.Name}>()) before building.");
				}
				else if (root && lifetimes?.IsScoped(system.ServiceType) == true)
				{
					context.Error(ScheduleDiagnosticCodes.ScopedServiceInRoot, $"System '{system.ServiceType.Name}' is registered as scoped but is used by the root schedule, which resolves from the root provider. Register it as a singleton, or use it inside a scene (UseScene).");
				}
			}

			foreach (var (serviceType, owner) in InjectedServices(item))
			{
				if (serviceType == typeof(IServiceProvider)) continue;

				if (isService is not null && !isService.IsService(serviceType))
				{
					context.Error(ScheduleDiagnosticCodes.UnresolvableParameter, $"'{owner}' injects '{serviceType.Name}', which is not registered in the service collection.");
				}
				else if (root && lifetimes?.IsScoped(serviceType) == true)
				{
					context.Error(ScheduleDiagnosticCodes.ScopedServiceInRoot, $"'{owner}' injects scoped service '{serviceType.Name}' into a step of the root schedule, which resolves from the root provider. Inject it in a scene system, or register it as a singleton.");
				}
			}
		}
	}

	private static bool NeedsInstance(StepPlan item) => item.NeedsInstance;

	private static IEnumerable<(Type Type, string Owner)> InjectedServices(StepPlan item)
	{
		foreach (var type in item.Services) yield return (type, item.Name);
		foreach (var type in item.EndServices) yield return (type, item.EndName ?? item.Name);
	}

	private static List<StepPlan> Sort(Context context, Stage stage, List<StepPlan> items)
	{
		var order = ScheduleSorter.Sort(
			items.Count,
			(i, j) => items[i].System is not null && items[j].System == items[i].System,
			(i, j) => items[i].After.Any(target => Matches(target, items[j])),
			(i, j) => items[i].Before.Any(target => Matches(target, items[j])),
			i => new ScheduleSortKey(items[i].Order, items[i].Kind == StepKind.Scope ? 0 : 1, items[i].RegistrationIndex, items[i].DeclarationIndex),
			out var cycle);

		if (order is null)
		{
			context.Error(ScheduleDiagnosticCodes.OrderingCycle, $"The [Before]/[After] constraints in {stage} form a cycle: {string.Join(" -> ", cycle!.Select(i => items[i].Name))}.");
			return items;
		}

		var sorted = order.Select(i => items[i]).ToList();
		var depth = 0;
		foreach (var item in sorted)
		{
			item.Depth = depth;
			if (item.Wraps) depth++;
		}

		return sorted;
	}

	private sealed class Context(ScheduleModel model)
	{
		public ScheduleModel Model { get; } = model;
		public List<ScheduleDiagnostic> Errors { get; } = [];
		public List<ScheduleDiagnostic> Warnings { get; } = [];

		private string Prefix => Model.IsRoot ? "" : $"[{Model.Name}] ";

		public void Error(string code, string message) => Errors.Add(new ScheduleDiagnostic(code, ScheduleDiagnosticSeverity.Error, Prefix + message));

		public void Warning(string code, string message) => Warnings.Add(new ScheduleDiagnostic(code, ScheduleDiagnosticSeverity.Warning, Prefix + message));

		public bool CheckStage(Stage stage, string name)
		{
			if (Enum.IsDefined(stage)) return true;
			Error(ScheduleDiagnosticCodes.UnknownStage, $"'{name}' names stage {(int)stage}, which is not a Stage (Init, First, FixedUpdate, Update, Render, Last, Destroy).");
			return false;
		}
	}
}

internal enum SignatureKind
{
	Invalid,
	Async,
	NoArguments,
	GameTime,
	Injected,
	LegacyVoidNext,
	LegacyFactory,
}

internal static class StepSignature
{
	public static SignatureKind Classify(MethodInfo method, out string? reason)
	{
		reason = null;
		var returnType = method.ReturnType;
		var parameters = method.GetParameters();

		if (method.IsDefined(typeof(AsyncStateMachineAttribute), false) || IsTaskLike(returnType)) return SignatureKind.Async;

		if (method.ContainsGenericParameters)
		{
			reason = "it is generic";
			return SignatureKind.Invalid;
		}

		if (returnType == typeof(GameLoopDelegate) && parameters.Length == 1 && parameters[0].ParameterType == typeof(GameLoopDelegate)) return SignatureKind.LegacyFactory;
		if (returnType == typeof(void) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GameTime) && parameters[1].ParameterType == typeof(GameLoopDelegate)) return SignatureKind.LegacyVoidNext;

		if (returnType != typeof(void))
		{
			reason = $"it returns {returnType.Name}";
			return SignatureKind.Invalid;
		}

		var gameTimes = 0;
		foreach (var parameter in parameters)
		{
			var type = parameter.ParameterType;
			if (type.IsByRef || type.IsPointer || parameter.IsOut)
			{
				reason = $"parameter '{parameter.Name}' is passed by reference";
				return SignatureKind.Invalid;
			}

			if (type == typeof(GameLoopDelegate))
			{
				reason = $"parameter '{parameter.Name}' is a GameLoopDelegate outside the legacy forms (GameTime dt, GameLoopDelegate next) and (GameLoopDelegate next)";
				return SignatureKind.Invalid;
			}

			if (type == typeof(GameTime)) gameTimes++;
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

	public static IEnumerable<Type> ServiceParameters(MethodInfo method) =>
		Classify(method, out _) == SignatureKind.Injected
			? method.GetParameters().Select(p => p.ParameterType).Where(t => t != typeof(GameTime))
			: [];

	private static bool IsTaskLike(Type type)
	{
		if (type == typeof(Task) || type == typeof(ValueTask)) return true;
		if (!type.IsGenericType) return false;
		var definition = type.GetGenericTypeDefinition();
		return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
	}
}

/// <summary>
/// The lifetimes of the registered services, captured when the application is built, so that root schedules can reject
/// scoped systems and scoped step parameters (ION006).
/// </summary>
internal sealed class ServiceLifetimeIndex
{
	private readonly Dictionary<Type, ServiceLifetime> _lifetimes = [];

	public ServiceLifetimeIndex(IEnumerable<ServiceDescriptor> descriptors)
	{
		foreach (var descriptor in descriptors) _lifetimes[descriptor.ServiceType] = descriptor.Lifetime;
	}

	public bool IsScoped(Type type)
	{
		if (_lifetimes.TryGetValue(type, out var lifetime)) return lifetime == ServiceLifetime.Scoped;
		if (type.IsConstructedGenericType && _lifetimes.TryGetValue(type.GetGenericTypeDefinition(), out lifetime)) return lifetime == ServiceLifetime.Scoped;
		return false;
	}
}
