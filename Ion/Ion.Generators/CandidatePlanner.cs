using System.Globalization;

using Microsoft.CodeAnalysis;

namespace Ion.Generators;

/// <summary>
/// A schedule the generator emits: the registrations it saw for one application (before a <c>Build()</c>/<c>Run()</c>
/// call) or one scene (its configure callback), and their plan.
/// </summary>
internal sealed class ScheduleCandidate
{
	/// <summary>A name for logs and the generated class.</summary>
	public string Name { get; init; } = "";

	/// <summary>The schedule name the runtime uses (<c>root</c>, or <c>Scene N</c>), for diagnostic prefixes.</summary>
	public string ScheduleName { get; init; } = "root";

	public bool IsRoot { get; init; }

	/// <summary>The System, Function and Middleware registrations, in registration order.</summary>
	public List<RegistrationOp> Entries { get; } = [];

	/// <summary>The systems of the System entries.</summary>
	public List<SystemInfo?> Systems { get; } = [];

	/// <summary>Whether a call the generator could not follow may add registrations (the runtime then decides).</summary>
	public bool Incomplete { get; set; }

	/// <summary>Why no schedule can be emitted, or null.</summary>
	public string? Invalid { get; set; }

	/// <summary>The items of each stage in run order.</summary>
	public List<CandidateItem>[] Stages { get; } = [.. Enumerable.Range(0, 7).Select(_ => new List<CandidateItem>())];

	/// <summary>The index of the generated class.</summary>
	public int ClassIndex { get; set; }

	/// <summary>Where the candidate is (the Build/Run or UseScene call), for diagnostics.</summary>
	public Location? Location { get; init; }

	public string Prefix => IsRoot ? "" : $"[{ScheduleName}] ";
}

/// <summary>One item of a stage of a <see cref="ScheduleCandidate"/>: a system step, scope or middleware, a function or a middleware delegate.</summary>
internal sealed class CandidateItem
{
	public int Entry { get; init; }
	public RegistrationOp Op { get; init; } = null!;
	public SystemInfo? System { get; init; }
	public SystemStep? Step { get; init; }
	public int Stage { get; init; }
	public string KindCode { get; init; } = "";
	public int Order { get; init; }
	public int Declaration { get; init; }
	public List<ITypeSymbol> After { get; init; } = [];
	public List<ITypeSymbol> Before { get; init; } = [];
	public string Name { get; init; } = "";

	public bool IsScope => KindCode == "B";

	public bool Wraps => KindCode is "B" or "M" or "D";

	public string Key => Entry.ToString(CultureInfo.InvariantCulture) + "|" + KindCode + "|" + (Step?.Name ?? "") + "|" + Declaration.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Plans a <see cref="ScheduleCandidate"/> with the runtime's rules (<c>SchedulePlanner</c>, sharing its sort) and reports
/// the schedule-level diagnostics that are certain at compile time.
/// </summary>
internal sealed class CandidatePlanner(SystemAnalyzer systems, Action<DiagnosticDescriptor, Location?, string> report)
{
	public void Plan(ScheduleCandidate candidate, List<RegistrationOp> ops)
	{
		foreach (var op in ops)
		{
			switch (op.Kind)
			{
				case OpKind.System:
				case OpKind.Function:
				case OpKind.Middleware:
					candidate.Entries.Add(op);
					break;
				case OpKind.Unmatchable:
					candidate.Invalid ??= op.Reason ?? "a registration is not intercepted";
					break;
				default:
					candidate.Incomplete = true;
					break;
			}
		}

		var items = new List<CandidateItem>();
		for (var e = 0; e < candidate.Entries.Count; e++)
		{
			var op = candidate.Entries[e];
			switch (op.Kind)
			{
				case OpKind.System:
					if (op.Service is null || op.Implementation is null)
					{
						candidate.Systems.Add(null);
						candidate.Invalid ??= "a system type from a referenced assembly was not found";
						continue;
					}

					var system = systems.Analyze(op.Service, op.Implementation);
					candidate.Systems.Add(system);
					if (system.NotDescribable is not null) candidate.Invalid ??= system.NotDescribable;
					if (system.HasErrors) candidate.Invalid ??= $"system '{system.Name}' has errors";

					foreach (var step in system.Steps)
					{
						items.Add(new CandidateItem
						{
							Entry = e,
							Op = op,
							System = system,
							Step = step,
							Stage = step.Stage,
							KindCode = step.KindCode,
							Order = step.Order,
							Declaration = step.DeclarationIndex,
							After = step.After,
							Before = step.Before,
							Name = system.Name + "." + step.Name,
						});
					}

					break;

				case OpKind.Function:
					candidate.Systems.Add(null);
					items.Add(new CandidateItem
					{
						Entry = e,
						Op = op,
						Stage = op.Stage,
						KindCode = "F",
						Order = op.Order,
						After = op.After ?? [],
						Before = op.Before ?? [],
						Name = op.Name ?? "function",
					});
					break;

				default:
					candidate.Systems.Add(null);
					items.Add(new CandidateItem { Entry = e, Op = op, Stage = op.Stage, KindCode = "D", Order = op.Order, Name = op.Name ?? "function" });
					break;
			}
		}

		if (!candidate.Incomplete && candidate.Invalid is null) ReportUnmatchedConstraints(candidate, items);

		for (var stage = 1; stage <= 7; stage++)
		{
			var stageItems = items.Where(i => i.Stage == stage).ToList();
			var sorted = Sort(stageItems, out var cycle);
			if (sorted is null)
			{
				candidate.Invalid ??= "the constraints form a cycle";

				// Report only a cycle that runs whatever the branches: one among unconditional registrations.
				var certain = stageItems.Where(i => !i.Op.Conditional).ToList();
				if (Sort(certain, out var certainCycle) is null)
				{
					var location = certainCycle!.Select(i => certain[i].Op.Location).FirstOrDefault(l => l is not null) ?? candidate.Location;
					report(Diagnostics.OrderingCycle, location, $"{candidate.Prefix}The [Before]/[After] constraints in {KnownSymbols.StageName(stage)} form a cycle: {string.Join(" -> ", certainCycle!.Select(i => certain[i].Name))}.");
				}

				continue;
			}

			candidate.Stages[stage - 1].AddRange(sorted.Select(i => stageItems[i]));
		}
	}

	private static List<int>? Sort(List<CandidateItem> items, out List<int>? cycle) => ScheduleSorter.Sort(
		items.Count,
		(i, j) => items[i].System is not null && items[i].Entry == items[j].Entry,
		(i, j) => items[i].After.Any(target => Matches(target, items[j])),
		(i, j) => items[i].Before.Any(target => Matches(target, items[j])),
		i => new ScheduleSortKey(items[i].Order, items[i].IsScope ? 0 : 1, items[i].Entry, items[i].Declaration),
		out cycle);

	private static bool Matches(ITypeSymbol target, CandidateItem item) =>
		item.System is { } system && (IsAssignable(target, system.Implementation) || IsAssignable(target, system.Service));

	private static bool IsAssignable(ITypeSymbol target, ITypeSymbol type)
	{
		for (var t = type; t is not null; t = t.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(t, target)) return true;
		}

		return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, target));
	}

	private void ReportUnmatchedConstraints(ScheduleCandidate candidate, List<CandidateItem> items)
	{
		var systemTypes = candidate.Systems.Where(s => s is not null).Select(s => s!).ToList();
		var reported = new HashSet<string>();

		foreach (var item in items)
		{
			foreach (var target in item.After.Concat(item.Before))
			{
				if (systemTypes.Any(s => IsAssignable(target, s.Implementation) || IsAssignable(target, s.Service))) continue;

				var source = item.System?.Name ?? item.Name;
				if (!reported.Add(source + "|" + target.ToDisplayString())) continue;

				report(Diagnostics.UnmatchedConstraint, item.Op.Location ?? candidate.Location,
					$"{candidate.Prefix}'{item.Name}' is ordered relative to '{SystemAnalyzer.RuntimeName(target)}', which is not a system of schedule '{candidate.ScheduleName}'; the constraint has no effect.");
			}
		}
	}
}
