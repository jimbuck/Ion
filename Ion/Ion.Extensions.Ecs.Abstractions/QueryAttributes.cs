using System.Reflection;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Makes a step method an ECS query: the method runs once per entity that has every component of its <c>ref</c> and
/// <c>in</c> parameters (and of <see cref="AllAttribute{T0}"/>), at least one of <see cref="AnyAttribute{T0}"/> and none
/// of <see cref="NoneAttribute{T0}"/>, in the world of the schedule (the root world, or the scene's).
/// </summary>
/// <remarks>
/// <para>
/// With the Ion generator (<c>Ion.Generators</c>) and a <c>partial</c> class, the method is expanded at compile time
/// into a loop over Arch's chunks that the schedule calls directly (the fastest form, no delegate or boxing). Without
/// the generator the same method runs through a reflection binder (correct, but it boxes every component).
/// </para>
/// <para>
/// Parameters: <c>ref T</c> or <c>in T</c> for a component (a struct); <see cref="Arch.Core.Entity"/> for the entity;
/// <c>[Data] in float</c> (or <c>[Data] float</c>) for the frame's delta time and <see cref="GameTime"/> for the whole
/// time; <see cref="Commands"/> for structural changes, and <see cref="Arch.Core.World"/>. The method may be private. A
/// structural change made on the world directly while the query runs throws a <see cref="StructuralChangeException"/>
/// naming the step and the entity; record it with <see cref="Commands"/> instead.
/// </para>
/// <code>
/// public sealed partial class MoveSystem
/// {
///     [Update, Query, None&lt;Frozen&gt;]
///     private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt)
///         => transform.Position += velocity.Value * dt;
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class QueryAttribute : StepBinderAttribute
{
	/// <summary>
	/// Skips the structural-change check the generated loop makes after every entity (two comparisons: about 10 percent of
	/// a trivial per-entity body, nothing for a real one). A structural change on the world then corrupts the iteration
	/// silently instead of throwing. The reflection binder always checks.
	/// </summary>
	public bool Unchecked { get; init; }

	/// <inheritdoc/>
	public override string? Validate(MethodInfo method) => QueryMethod.Describe(method, out var reason) is null ? reason : null;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> GetServices(MethodInfo method) => QueryMethod.Describe(method, out _)?.Services ?? [];

	/// <inheritdoc/>
	public override GameLoopDelegate Bind(object? instance, MethodInfo method, IServiceProvider services, string name)
	{
		var query = QueryMethod.Describe(method, out var reason) ?? throw new InvalidOperationException($"'{name}' cannot run as a query: {reason}.");
		return new ReflectionQuery(query, instance, services, name).Run;
	}
}

/// <summary>Base class of the component filters of a <see cref="QueryAttribute"/> method.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class QueryFilterAttribute : Attribute
{
	/// <summary>How the components filter the entities.</summary>
	public abstract QueryFilterKind Kind { get; }

	/// <summary>The component types.</summary>
	public abstract IReadOnlyList<Type> Types { get; }
}

/// <summary>The kinds of <see cref="QueryFilterAttribute"/>.</summary>
public enum QueryFilterKind
{
	/// <summary>Entities have every component.</summary>
	All,
	/// <summary>Entities have at least one of the components.</summary>
	Any,
	/// <summary>Entities have none of the components.</summary>
	None,
}

/// <summary>The query's entities also have <typeparamref name="T0"/> (components of <c>ref</c>/<c>in</c> parameters are implied).</summary>
public sealed class AllAttribute<T0> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.All;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0)];
}

/// <summary>The query's entities also have <typeparamref name="T0"/> and <typeparamref name="T1"/>.</summary>
public sealed class AllAttribute<T0, T1> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.All;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1)];
}

/// <summary>The query's entities also have <typeparamref name="T0"/>, <typeparamref name="T1"/> and <typeparamref name="T2"/>.</summary>
public sealed class AllAttribute<T0, T1, T2> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.All;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2)];
}

/// <summary>The query's entities also have the four components.</summary>
public sealed class AllAttribute<T0, T1, T2, T3> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.All;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2), typeof(T3)];
}

/// <summary>The query's entities have <typeparamref name="T0"/>.</summary>
public sealed class AnyAttribute<T0> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.Any;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0)];
}

/// <summary>The query's entities have at least one of <typeparamref name="T0"/> and <typeparamref name="T1"/>.</summary>
public sealed class AnyAttribute<T0, T1> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.Any;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1)];
}

/// <summary>The query's entities have at least one of the three components.</summary>
public sealed class AnyAttribute<T0, T1, T2> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.Any;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2)];
}

/// <summary>The query's entities have at least one of the four components.</summary>
public sealed class AnyAttribute<T0, T1, T2, T3> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.Any;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2), typeof(T3)];
}

/// <summary>The query's entities do not have <typeparamref name="T0"/>.</summary>
public sealed class NoneAttribute<T0> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.None;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0)];
}

/// <summary>The query's entities have neither <typeparamref name="T0"/> nor <typeparamref name="T1"/>.</summary>
public sealed class NoneAttribute<T0, T1> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.None;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1)];
}

/// <summary>The query's entities have none of the three components.</summary>
public sealed class NoneAttribute<T0, T1, T2> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.None;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2)];
}

/// <summary>The query's entities have none of the four components.</summary>
public sealed class NoneAttribute<T0, T1, T2, T3> : QueryFilterAttribute
{
	/// <inheritdoc/>
	public override QueryFilterKind Kind => QueryFilterKind.None;

	/// <inheritdoc/>
	public override IReadOnlyList<Type> Types => [typeof(T0), typeof(T1), typeof(T2), typeof(T3)];
}

/// <summary>
/// Marks a parameter of a <see cref="QueryAttribute"/> method that is not a component but frame data: a <c>float</c>
/// receives the frame's delta time in seconds (<see cref="GameTime.Delta"/>), a <see cref="GameTime"/> the whole time.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class DataAttribute : Attribute
{
}
