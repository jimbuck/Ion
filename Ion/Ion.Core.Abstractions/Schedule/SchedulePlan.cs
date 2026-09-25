using System.Globalization;
using System.Reflection;
using System.Text;

namespace Ion;

/// <summary>The kind of a <see cref="StepPlan"/>.</summary>
public enum StepKind
{
	/// <summary>A leaf method of a system: runs and returns.</summary>
	Step,
	/// <summary>A <see cref="BeginAttribute"/>/<see cref="EndAttribute"/> pair of a system, wrapping every item after it.</summary>
	Scope,
	/// <summary>A function step (a delegate registered with <c>app.Update(...)</c> and friends).</summary>
	Function,
	/// <summary>A legacy middleware (a method or delegate taking <c>next</c>), wrapping every item after it.</summary>
	Middleware,
}

/// <summary>
/// A validated, ordered schedule: for every stage the flat list of items in run order. Scopes and middleware wrap every
/// item that follows them in their stage, so the nesting is implied by the order (see <see cref="StepPlan.Depth"/>).
/// It holds types and methods, not instances; <see cref="Schedule"/> binds it.
/// </summary>
public sealed class SchedulePlan
{
	internal SchedulePlan(string name, bool isRoot, StagePlan[] stages, IReadOnlyList<ScheduleDiagnostic> diagnostics, IReadOnlyList<NestedSchedulePlan> nested)
	{
		Name = name;
		IsRoot = isRoot;
		Stages = stages;
		Diagnostics = diagnostics;
		Nested = nested;
	}

	/// <summary>The schedule name.</summary>
	public string Name { get; }

	/// <summary>Whether the schedule is resolved from the root service provider.</summary>
	public bool IsRoot { get; }

	/// <summary>The seven stages, in <see cref="Stage"/> order.</summary>
	public IReadOnlyList<StagePlan> Stages { get; }

	/// <summary>The warnings found while planning (errors are thrown).</summary>
	public IReadOnlyList<ScheduleDiagnostic> Diagnostics { get; }

	/// <summary>The plans of the schedules run by this one (scenes), when planned with services.</summary>
	public IReadOnlyList<NestedSchedulePlan> Nested { get; }

	/// <summary>The plan of <paramref name="stage"/>.</summary>
	public StagePlan this[Stage stage] => Stages[(int)stage - 1];

	/// <summary>The warnings of this plan and of every nested plan.</summary>
	public IEnumerable<ScheduleDiagnostic> AllDiagnostics() => Diagnostics.Concat(Nested.SelectMany(n => n.Plan.AllDiagnostics()));

	/// <summary>
	/// The schedule as text: for each stage the items in run order with their order, <c>System.Method</c>, their
	/// <c>[after ...; before ...]</c> constraints, and braces around the items each scope or middleware wraps (a scope's
	/// closing line names its end method); then each nested schedule.
	/// </summary>
	public string Print()
	{
		var builder = new StringBuilder();
		Print(builder, null);
		return builder.ToString();
	}

	/// <inheritdoc/>
	public override string ToString() => Print();

	private void Print(StringBuilder builder, string? owner)
	{
		builder.Append("Schedule ").Append(Name);
		if (owner is not null) builder.Append(" (run by ").Append(owner).Append(')');
		builder.Append('\n');

		foreach (var stage in Stages)
		{
			builder.Append("  ").Append(stage.Stage).Append('\n');

			if (stage.Steps.Count == 0)
			{
				builder.Append("    (empty)\n");
				continue;
			}

			var open = new Stack<StepPlan>();
			foreach (var step in stage.Steps)
			{
				AppendLine(builder, step.Order, open.Count, step.Name + Suffix(step.Kind) + Constraints(step) + (step.Wraps ? " {" : ""));
				if (step.Wraps) open.Push(step);
			}

			while (open.Count > 0)
			{
				var step = open.Pop();
				AppendLine(builder, step.Kind == StepKind.Scope ? step.Order : null, open.Count, "} " + (step.EndName ?? step.Name));
			}
		}

		foreach (var nested in Nested)
		{
			nested.Plan.Print(builder, nested.Owner.Name);
		}
	}

	private static string Constraints(StepPlan step)
	{
		if (step.After.Count == 0 && step.Before.Count == 0) return "";

		var parts = new List<string>(2);
		if (step.After.Count > 0) parts.Add("after " + string.Join(", ", step.After.Select(t => t.Name)));
		if (step.Before.Count > 0) parts.Add("before " + string.Join(", ", step.Before.Select(t => t.Name)));
		return " [" + string.Join("; ", parts) + "]";
	}

	private static string Suffix(StepKind kind) => kind switch
	{
		StepKind.Function => " (function)",
		StepKind.Middleware => " (middleware)",
		_ => "",
	};

	private static void AppendLine(StringBuilder builder, int? order, int depth, string text)
	{
		builder.Append("    ");
		builder.Append((order?.ToString(CultureInfo.InvariantCulture) ?? "").PadLeft(6));
		builder.Append("  ");
		builder.Append(' ', depth * 2);
		builder.Append(text);
		builder.Append('\n');
	}
}

/// <summary>A nested schedule and the system that runs it.</summary>
/// <param name="Name">The nested schedule name.</param>
/// <param name="Owner">The system whose steps run it.</param>
/// <param name="Plan">Its plan.</param>
public sealed record NestedSchedulePlan(string Name, Type Owner, SchedulePlan Plan);

/// <summary>The ordered items of one stage.</summary>
public sealed class StagePlan
{
	internal StagePlan(Stage stage, IReadOnlyList<StepPlan> steps)
	{
		Stage = stage;
		Steps = steps;
	}

	/// <summary>The stage.</summary>
	public Stage Stage { get; }

	/// <summary>The items in run order.</summary>
	public IReadOnlyList<StepPlan> Steps { get; }
}

/// <summary>
/// One item of a stage: a step, a scope, a function step or a legacy middleware. This is the unit the source generator
/// emits a call (or a <c>try/finally</c>) for.
/// </summary>
public sealed class StepPlan
{
	internal StepPlan(Stage stage, StepKind kind, int order, string name)
	{
		Stage = stage;
		Kind = kind;
		Order = order;
		Name = name;
	}

	/// <summary>The stage.</summary>
	public Stage Stage { get; }

	/// <summary>The kind of item.</summary>
	public StepKind Kind { get; }

	/// <summary>The order (for a scope, the <see cref="BeginAttribute"/> order).</summary>
	public int Order { get; }

	/// <summary>The printed name: <c>System.Method</c> (for a scope, the begin method).</summary>
	public string Name { get; }

	/// <summary>For a scope, the printed name of the end method.</summary>
	public string? EndName { get; internal init; }

	/// <summary>The system of a step, scope or middleware method; null for functions and middleware delegates.</summary>
	public SystemEntry? System { get; internal init; }

	/// <summary>The step method, the scope's begin method, or the middleware method.</summary>
	public MethodInfo? Method { get; internal init; }

	/// <summary>The scope's end method.</summary>
	public MethodInfo? EndMethod { get; internal init; }

	/// <summary>The scope name, when the scope was paired by name.</summary>
	public string? ScopeName { get; internal init; }

	/// <summary>The function registration of a function step.</summary>
	public FunctionEntry? Function { get; internal init; }

	/// <summary>The middleware registration of a delegate middleware.</summary>
	public MiddlewareEntry? Middleware { get; internal init; }

	/// <summary>The <see cref="AfterAttribute{T}"/> targets.</summary>
	public IReadOnlyList<Type> After { get; internal init; } = [];

	/// <summary>The <see cref="BeforeAttribute{T}"/> targets.</summary>
	public IReadOnlyList<Type> Before { get; internal init; } = [];

	/// <summary>The registration index of the system or function (first tie breaker).</summary>
	public int RegistrationIndex { get; internal init; }

	/// <summary>The declaration index of the method within its system (second tie breaker).</summary>
	public int DeclarationIndex { get; internal init; }

	/// <summary>The number of scopes and middleware wrapping this item.</summary>
	public int Depth { get; internal set; }

	/// <summary>Whether the item wraps every item after it (a scope or a middleware).</summary>
	public bool Wraps => Kind is StepKind.Scope or StepKind.Middleware;

	/// <inheritdoc/>
	public override string ToString() => $"{Stage} {Order} {Name}";
}
