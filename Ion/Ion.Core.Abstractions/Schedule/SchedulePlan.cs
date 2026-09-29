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
}

/// <summary>
/// A validated, ordered schedule: for every stage the flat list of items in run order. A scope wraps every item that
/// follows it in their stage, so the nesting is implied by the order (see <see cref="StepPlan.Depth"/>).
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
	/// <c>[after ...; before ...]</c> constraints, and braces around the items each scope wraps (the closing line names
	/// its end method); then each nested schedule.
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
				AppendLine(builder, step.Order, open.Count, step.Name + (step.Kind == StepKind.Function ? " (function)" : "") + Constraints(step) + (step.Wraps ? " {" : ""));
				if (step.Wraps) open.Push(step);
			}

			while (open.Count > 0)
			{
				var step = open.Pop();
				AppendLine(builder, step.Order, open.Count, "} " + step.EndName);
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
/// One item of a stage: a step, a scope or a function step. This is the unit the source generator
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

	/// <summary>The system of a step or scope; null for functions.</summary>
	public SystemEntry? System { get; internal init; }

	/// <summary>The step method or the scope's begin method (null for generated systems, see <see cref="Generated"/>).</summary>
	public MethodInfo? Method { get; internal init; }

	/// <summary>The name of the step method or the scope's begin method; null for functions.</summary>
	public string? MethodName { get; internal init; }

	/// <summary>The compile-time description of the step, for systems registered by generated code.</summary>
	public GeneratedStep? Generated { get; internal init; }

	/// <summary>The attribute that binds the step (a <see cref="StepBinderAttribute"/>, such as the ECS module's <c>[Query]</c> on its runtime path), or null.</summary>
	public StepBinderAttribute? Binder { get; internal init; }

	/// <summary>Whether running the item needs the system instance (a non-static step, begin or end method).</summary>
	public bool NeedsInstance { get; internal init; }

	/// <summary>The services injected into the step (and the scope's end method), in parameter order.</summary>
	public IReadOnlyList<Type> Services { get; internal init; } = [];

	/// <summary>The services injected into a scope's end method.</summary>
	public IReadOnlyList<Type> EndServices { get; internal init; } = [];

	/// <summary>The scope's end method.</summary>
	public MethodInfo? EndMethod { get; internal init; }

	/// <summary>The scope name, when the scope was paired by name.</summary>
	public string? ScopeName { get; internal init; }

	/// <summary>The function registration of a function step.</summary>
	public FunctionEntry? Function { get; internal init; }

	/// <summary>The <see cref="AfterAttribute{T}"/> targets.</summary>
	public IReadOnlyList<Type> After { get; internal init; } = [];

	/// <summary>The <see cref="BeforeAttribute{T}"/> targets.</summary>
	public IReadOnlyList<Type> Before { get; internal init; } = [];

	/// <summary>The registration index of the system or function (first tie breaker).</summary>
	public int RegistrationIndex { get; internal init; }

	/// <summary>The declaration index of the method within its system (second tie breaker).</summary>
	public int DeclarationIndex { get; internal init; }

	/// <summary>The number of scopes wrapping this item.</summary>
	public int Depth { get; internal set; }

	/// <summary>Whether the item wraps every item after it (a scope).</summary>
	public bool Wraps => Kind == StepKind.Scope;

	/// <inheritdoc/>
	public override string ToString() => $"{Stage} {Order} {Name}";
}
