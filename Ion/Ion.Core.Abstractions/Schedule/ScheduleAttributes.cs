namespace Ion;

/// <summary>
/// Base class of the stage attributes (<see cref="InitAttribute"/>, <see cref="UpdateAttribute"/>, ...). A public
/// method of a system that carries one is a step of that stage: it runs once per stage invocation, in
/// <see cref="Order"/>, and returns. Supported signatures: <c>void M(GameTime dt)</c>, <c>void M()</c>, and
/// <c>void M(GameTime dt, TService1 s1, ...)</c> where every other parameter is a service resolved once when the schedule
/// is built.
/// </summary>
/// <remarks>
/// The legacy middleware forms <c>void M(GameTime dt, GameLoopDelegate next)</c> and
/// <c>GameLoopDelegate M(GameLoopDelegate next)</c> still work for one release: they wrap every step that sorts after
/// them in the stage, and building the schedule logs warning ION010.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class StageAttribute : Attribute
{
	/// <summary>The stage the step runs in.</summary>
	public abstract Stage Stage { get; }

	/// <summary>
	/// The position of the step in its stage: lower runs first. Defaults to <see cref="StageOrder.Default"/> (0). Ties are
	/// broken by registration order, then by declaration order within the system. See <see cref="StageOrder"/> for the
	/// bands reserved for engine steps.
	/// </summary>
	public int Order { get; init; }
}

/// <summary>Marks a step of the <see cref="Stage.Init"/> stage.</summary>
public sealed class InitAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.Init;
}

/// <summary>Marks a step of the <see cref="Stage.First"/> stage.</summary>
public sealed class FirstAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.First;
}

/// <summary>Marks a step of the <see cref="Stage.FixedUpdate"/> stage.</summary>
public sealed class FixedUpdateAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.FixedUpdate;
}

/// <summary>Marks a step of the <see cref="Stage.Update"/> stage.</summary>
public sealed class UpdateAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.Update;
}

/// <summary>Marks a step of the <see cref="Stage.Render"/> stage.</summary>
public sealed class RenderAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.Render;
}

/// <summary>Marks a step of the <see cref="Stage.Last"/> stage.</summary>
public sealed class LastAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.Last;
}

/// <summary>Marks a step of the <see cref="Stage.Destroy"/> stage.</summary>
public sealed class DestroyAttribute : StageAttribute
{
	/// <inheritdoc/>
	public override Stage Stage => Stage.Destroy;
}

/// <summary>
/// Base class of <see cref="BeginAttribute"/> and <see cref="EndAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class ScopeAttribute(Stage stage) : Attribute
{
	private readonly int _order;

	/// <summary>The stage the scope wraps.</summary>
	public Stage Stage { get; } = stage;

	/// <summary>
	/// The order of the scope. The scope opens at this position in the stage and wraps every step and scope that sorts
	/// after it: every item with a greater order, and items of the same order that sort later (at equal order a scope
	/// opens before the steps). On <see cref="EndAttribute"/> it is optional; when set it must equal the
	/// <see cref="BeginAttribute"/> order (ION011).
	/// </summary>
	public int Order
	{
		get => _order;
		init
		{
			_order = value;
			HasOrder = true;
		}
	}

	/// <summary>Whether <see cref="Order"/> was set explicitly.</summary>
	public bool HasOrder { get; private init; }

	/// <summary>
	/// Pairs a <see cref="BeginAttribute"/> with the <see cref="EndAttribute"/> of the same name on the same system.
	/// Only needed when a system has more than one scope in a stage.
	/// </summary>
	public string? ScopeName { get; init; }
}

/// <summary>
/// Marks the method that opens a scope in <see cref="ScopeAttribute.Stage"/>. It is paired with the method marked
/// <see cref="EndAttribute"/> for the same stage on the same system; the end method always runs (in a <c>finally</c>)
/// after every step the scope wraps, even when one throws. Scopes express what middleware used to do around
/// <c>next(dt)</c>: frame begin/end, sprite batch begin/end, profiling. A method can open scopes in several stages.
/// </summary>
/// <example><code>
/// [Begin(Stage.Render, Order = -900)] public void BeginFrame(GameTime dt) { ... }
/// [End(Stage.Render, Order = -900)] public void EndFrame(GameTime dt) { ... }
/// </code></example>
public sealed class BeginAttribute(Stage stage) : ScopeAttribute(stage)
{
}

/// <summary>
/// Marks the method that closes the scope opened by the <see cref="BeginAttribute"/> method of the same stage (and
/// <see cref="ScopeAttribute.ScopeName"/>) on the same system.
/// </summary>
public sealed class EndAttribute(Stage stage) : ScopeAttribute(stage)
{
}

/// <summary>
/// Base class of <see cref="AfterAttribute{T}"/> and <see cref="BeforeAttribute{T}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class OrderingAttribute : Attribute
{
	/// <summary>
	/// The system type the constraint refers to. It matches a registered system whose service or implementation type is,
	/// derives from or implements it.
	/// </summary>
	public abstract Type Target { get; }

	/// <summary>True for <see cref="BeforeAttribute{T}"/>, false for <see cref="AfterAttribute{T}"/>.</summary>
	public abstract bool IsBefore { get; }
}

/// <summary>
/// Runs the step (or, on a class, every step of the system) after every step and scope opening of system
/// <typeparamref name="T"/> in the same stage of the same schedule. Constraints take precedence over
/// <see cref="StageAttribute.Order"/>; a cycle is error ION002.
/// </summary>
public sealed class AfterAttribute<T> : OrderingAttribute
{
	/// <inheritdoc/>
	public override Type Target => typeof(T);

	/// <inheritdoc/>
	public override bool IsBefore => false;
}

/// <summary>
/// Runs the step (or, on a class, every step of the system) before every step and scope opening of system
/// <typeparamref name="T"/> in the same stage of the same schedule. Constraints take precedence over
/// <see cref="StageAttribute.Order"/>; a cycle is error ION002.
/// </summary>
public sealed class BeforeAttribute<T> : OrderingAttribute
{
	/// <inheritdoc/>
	public override Type Target => typeof(T);

	/// <inheritdoc/>
	public override bool IsBefore => true;
}
