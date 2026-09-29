using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Thrown when a query step changes the structure of its world (creates or destroys an entity, or adds or removes a
/// component) while its query runs, which would invalidate the chunks being iterated. Record the change with
/// <see cref="Commands"/>, which the ECS module plays back at the end of the stage.
/// </summary>
public sealed class StructuralChangeException : InvalidOperationException
{
	/// <summary>Creates the exception for <paramref name="step"/> at <paramref name="entity"/>.</summary>
	public StructuralChangeException(string step, Entity entity)
		: base($"'{step}' changed the structure of the world (created or destroyed an entity, or added or removed a component) while its query was running, at {entity}. That invalidates the chunks the query iterates: inject Commands and record the change there (commands.Add, Remove, Create, Destroy, SetParent); the ECS module plays commands back at the end of the stage.")
	{
		Step = step;
		Entity = entity;
	}

	/// <summary>The step (<c>System.Method</c>) whose query was running.</summary>
	public string Step { get; }

	/// <summary>The entity the step was processing.</summary>
	public Entity Entity { get; }
}

/// <summary>Helpers called by the loops the Ion generator emits for <see cref="QueryAttribute"/> methods.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class QueryGuard
{
	/// <summary>Throws the <see cref="StructuralChangeException"/> of <paramref name="step"/> at <paramref name="entity"/>.</summary>
	[DoesNotReturn]
	[StackTraceHidden]
	public static void ThrowStructuralChange(string step, Entity entity) => throw new StructuralChangeException(step, entity);
}

/// <summary>What a <see cref="QueryAttribute"/> parameter receives.</summary>
internal enum QueryParameterKind
{
	Component,
	Entity,
	Delta,
	Time,
	Commands,
	World,
}

internal sealed record QueryParameter(QueryParameterKind Kind, Type Type, bool IsReadOnly, int Component);

/// <summary>A <see cref="QueryAttribute"/> method as the reflection binder sees it (the generator applies the same rules at compile time).</summary>
internal sealed class QueryMethod
{
	public required MethodInfo Method { get; init; }
	public required QueryParameter[] Parameters { get; init; }
	public required Type[] Components { get; init; }
	public required Type[] All { get; init; }
	public required Type[] Any { get; init; }
	public required Type[] None { get; init; }
	public required Type[] Services { get; init; }

	public static QueryMethod? Describe(MethodInfo method, out string? reason)
	{
		reason = null;
		if (method.ReturnType != typeof(void))
		{
			reason = $"a query method returns void, and it returns {method.ReturnType.Name}";
			return null;
		}

		if (method.ContainsGenericParameters)
		{
			reason = "a query method cannot be generic";
			return null;
		}

		var parameters = method.GetParameters();
		var result = new QueryParameter[parameters.Length];
		var components = new List<Type>();
		var usesCommands = false;

		for (var i = 0; i < parameters.Length; i++)
		{
			var parameter = parameters[i];
			var type = parameter.ParameterType;
			var element = type.IsByRef ? type.GetElementType()! : type;

			if (parameter.IsOut)
			{
				reason = $"parameter '{parameter.Name}' is an out parameter";
				return null;
			}

			if (parameter.IsDefined(typeof(DataAttribute), true))
			{
				if (element == typeof(float)) result[i] = new QueryParameter(QueryParameterKind.Delta, element, true, -1);
				else if (element == typeof(GameTime)) result[i] = new QueryParameter(QueryParameterKind.Time, element, true, -1);
				else
				{
					reason = $"parameter '{parameter.Name}' is marked [Data] but is a {element.Name}; [Data] parameters are a float (the delta time) or a GameTime";
					return null;
				}

				continue;
			}

			if (element == typeof(Entity)) result[i] = new QueryParameter(QueryParameterKind.Entity, element, true, -1);
			else if (!type.IsByRef && type == typeof(GameTime)) result[i] = new QueryParameter(QueryParameterKind.Time, type, true, -1);
			else if (!type.IsByRef && type == typeof(Commands))
			{
				result[i] = new QueryParameter(QueryParameterKind.Commands, type, true, -1);
				usesCommands = true;
			}
			else if (!type.IsByRef && type == typeof(World)) result[i] = new QueryParameter(QueryParameterKind.World, type, true, -1);
			else if (type.IsByRef)
			{
				if (!element.IsValueType)
				{
					reason = $"parameter '{parameter.Name}' is a {element.Name}, which is not a struct; query components are structs";
					return null;
				}

				if (components.Contains(element))
				{
					reason = $"component {element.Name} is a parameter more than once";
					return null;
				}

				result[i] = new QueryParameter(QueryParameterKind.Component, element, parameter.IsIn, components.Count);
				components.Add(element);
			}
			else if (type.IsValueType)
			{
				reason = $"component parameter '{parameter.Name}' ({type.Name}) is passed by value; pass it by ref (or in, to read it)";
				return null;
			}
			else
			{
				reason = $"parameter '{parameter.Name}' ({type.Name}) is not a component (ref or in), Entity, [Data] float, GameTime, Commands or World";
				return null;
			}
		}

		var filters = method.GetCustomAttributes<QueryFilterAttribute>(true).ToArray();
		var all = components.Concat(filters.Where(f => f.Kind == QueryFilterKind.All).SelectMany(f => f.Types)).Distinct().ToArray();
		var any = filters.Where(f => f.Kind == QueryFilterKind.Any).SelectMany(f => f.Types).Distinct().ToArray();
		var none = filters.Where(f => f.Kind == QueryFilterKind.None).SelectMany(f => f.Types).Distinct().ToArray();

		if (all.FirstOrDefault(none.Contains) is { } overlap)
		{
			reason = $"component {overlap.Name} is both required (a parameter or All) and excluded (None)";
			return null;
		}

		return new QueryMethod
		{
			Method = method,
			Parameters = result,
			Components = [.. components],
			All = all,
			Any = any,
			None = none,
			Services = usesCommands ? [typeof(World), typeof(Commands)] : [typeof(World)],
		};
	}

	public QueryDescription CreateDescription() => new(
		all: Signature(All),
		any: Any.Length == 0 ? (Signature?)null : Signature(Any),
		none: None.Length == 0 ? (Signature?)null : Signature(None));

	private static Signature Signature(Type[] types)
	{
		var componentTypes = new ComponentType[types.Length];
		for (var i = 0; i < types.Length; i++) componentTypes[i] = ComponentTypeOf(types[i]);
		return new Signature(componentTypes);
	}

	/// <summary>
	/// The Arch component type of <paramref name="type"/>. Registering an unknown type from a <see cref="Type"/> makes Arch
	/// call <c>MakeGenericMethod</c>, which NativeAOT cannot do: there the type must have been registered up front (the
	/// generator's module initializer, <see cref="EcsComponents.Register{T}"/>, or a component the world already stored).
	/// </summary>
	public static ComponentType ComponentTypeOf(Type type)
	{
		if (ComponentRegistry.TryGet(type, out var componentType)) return componentType;
		if (RuntimeFeature.IsDynamicCodeSupported) return type;
		throw new NotSupportedException($"Component {type.Name} is not registered with Arch, and NativeAOT cannot register it from a Type. Compile the game with Ion.Generators (which expands [Query] methods and registers their components) or call EcsComponents.Register<{type.Name}>() at startup.");
	}
}

/// <summary>
/// The runtime path of a <see cref="QueryAttribute"/> method (no generator): Arch's query over the method's description,
/// and the method invoked per entity through reflection, with the components boxed in and written back.
/// </summary>
internal sealed class ReflectionQuery
{
	private readonly QueryMethod _query;
	private readonly object? _target;
	private readonly World _world;
	private readonly Commands? _commands;
	private readonly string _name;
	private readonly QueryDescription _description;
	private readonly ComponentType[] _types;
	private readonly MethodInvoker _invoker;
	private readonly object?[] _arguments;
	private readonly Array[] _arrays;

	public ReflectionQuery(QueryMethod query, object? target, IServiceProvider services, string name)
	{
		_query = query;
		_target = target;
		_name = name;
		_world = services.GetRequiredService<World>();
		if (query.Services.Contains(typeof(Commands))) _commands = services.GetRequiredService<Commands>();
		_description = query.CreateDescription();
		_types = [.. query.Components.Select(QueryMethod.ComponentTypeOf)];
		_invoker = MethodInvoker.Create(query.Method);
		_arguments = new object?[query.Parameters.Length];
		_arrays = new Array[_types.Length];

		for (var i = 0; i < query.Parameters.Length; i++)
		{
			switch (query.Parameters[i].Kind)
			{
				case QueryParameterKind.Commands: _arguments[i] = _commands; break;
				case QueryParameterKind.World: _arguments[i] = _world; break;
			}
		}
	}

	[StackTraceHidden]
	public void Run(GameTime dt)
	{
		var parameters = _query.Parameters;
		var arguments = _arguments;
		object? delta = null;

		foreach (ref var chunk in _world.Query(in _description))
		{
			var count = chunk.Count;
			var size = _world.Size;
			for (var k = 0; k < _types.Length; k++) _arrays[k] = chunk.GetArray(_types[k]);

			// Arch's order: last to first within a chunk, like World.Query and the generated loop.
			for (var i = count - 1; i >= 0; i--)
			{
				var entity = chunk.Entity(i);
				for (var p = 0; p < parameters.Length; p++)
				{
					var parameter = parameters[p];
					switch (parameter.Kind)
					{
						case QueryParameterKind.Component: arguments[p] = _arrays[parameter.Component].GetValue(i); break;
						case QueryParameterKind.Entity: arguments[p] = entity; break;
						case QueryParameterKind.Delta: arguments[p] = delta ??= dt.Delta; break;
						case QueryParameterKind.Time: arguments[p] = dt; break;
					}
				}

				_invoker.Invoke(_target, arguments.AsSpan());

				if (chunk.Count != count || _world.Size != size) QueryGuard.ThrowStructuralChange(_name, entity);

				for (var p = 0; p < parameters.Length; p++)
				{
					var parameter = parameters[p];
					if (parameter.Kind == QueryParameterKind.Component && !parameter.IsReadOnly) _arrays[parameter.Component].SetValue(arguments[p], i);
				}
			}
		}
	}
}
