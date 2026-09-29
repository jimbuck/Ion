// This file is compiled into Ion.Core.Abstractions (the runtime planner) and linked into Ion.Generators (the source
// generator), so that the schedule the generator emits at compile time is sorted by exactly the same rules as the one the
// runtime plans. Keep it free of runtime-only APIs: the generator targets netstandard2.0.

using System;
using System.Collections.Generic;

namespace Ion;

/// <summary>
/// The sort key of one item of a stage: lower runs first, compared field by field.
/// </summary>
/// <param name="Order">The step order (<c>Order</c> of the stage or scope attribute).</param>
/// <param name="Rank">0 for a scope, 1 for everything else: at equal order a scope opens before the steps, so it wraps them.</param>
/// <param name="Registration">The registration index of the system or function.</param>
/// <param name="Declaration">The declaration index of the method within its system.</param>
internal readonly record struct ScheduleSortKey(int Order, int Rank, int Registration, int Declaration);

/// <summary>
/// Sorts the items of one stage: Kahn's algorithm over the Before/After constraints, always taking the ready item with
/// the lowest <see cref="ScheduleSortKey"/> (then the lowest discovery index). Constraints therefore win over order.
/// </summary>
internal static class ScheduleSorter
{
	/// <summary>
	/// Sorts <paramref name="count"/> items.
	/// </summary>
	/// <param name="count">The number of items, identified by their discovery index.</param>
	/// <param name="sameSystem">Whether two items belong to the same system (constraints never apply between them).</param>
	/// <param name="runsAfter">Whether item <c>i</c> has an After constraint that matches item <c>j</c>.</param>
	/// <param name="runsBefore">Whether item <c>i</c> has a Before constraint that matches item <c>j</c>.</param>
	/// <param name="key">The sort key of an item.</param>
	/// <param name="cycle">When the constraints form a cycle: the items of one cycle, first item repeated at the end.</param>
	/// <returns>The discovery indexes in run order, or null when the constraints form a cycle.</returns>
	public static List<int>? Sort(int count, Func<int, int, bool> sameSystem, Func<int, int, bool> runsAfter, Func<int, int, bool> runsBefore, Func<int, ScheduleSortKey> key, out List<int>? cycle)
	{
		var successors = new List<int>[count];
		var predecessors = new List<int>[count];
		var inDegree = new int[count];
		var edges = new HashSet<(int, int)>();

		for (var i = 0; i < count; i++)
		{
			successors[i] = [];
			predecessors[i] = [];
		}

		void AddEdge(int from, int to)
		{
			if (!edges.Add((from, to))) return;
			successors[from].Add(to);
			predecessors[to].Add(from);
			inDegree[to]++;
		}

		for (var i = 0; i < count; i++)
		{
			for (var j = 0; j < count; j++)
			{
				if (i == j || sameSystem(i, j)) continue;
				if (runsAfter(i, j)) AddEdge(j, i);
				if (runsBefore(i, j)) AddEdge(i, j);
			}
		}

		var keys = new ScheduleSortKey[count];
		for (var i = 0; i < count; i++) keys[i] = key(i);

		var ready = new List<int>();
		for (var i = 0; i < count; i++) if (inDegree[i] == 0) ready.Add(i);

		var sorted = new List<int>(count);
		var done = new bool[count];
		while (ready.Count > 0)
		{
			var best = 0;
			for (var r = 1; r < ready.Count; r++)
			{
				if (Compare(keys[ready[r]], ready[r], keys[ready[best]], ready[best]) < 0) best = r;
			}

			var next = ready[best];
			ready.RemoveAt(best);
			done[next] = true;
			sorted.Add(next);

			foreach (var successor in successors[next])
			{
				if (--inDegree[successor] == 0) ready.Add(successor);
			}
		}

		if (sorted.Count < count)
		{
			cycle = FindCycle(predecessors, done);
			return null;
		}

		cycle = null;
		return sorted;
	}

	private static int Compare(ScheduleSortKey a, int aIndex, ScheduleSortKey b, int bIndex)
	{
		var c = a.Order.CompareTo(b.Order);
		if (c != 0) return c;
		c = a.Rank.CompareTo(b.Rank);
		if (c != 0) return c;
		c = a.Registration.CompareTo(b.Registration);
		if (c != 0) return c;
		c = a.Declaration.CompareTo(b.Declaration);
		if (c != 0) return c;
		return aIndex.CompareTo(bIndex);
	}

	private static List<int> FindCycle(List<int>[] predecessors, bool[] done)
	{
		var start = Array.IndexOf(done, false);
		var path = new List<int>();
		var current = start;

		while (!path.Contains(current))
		{
			path.Add(current);
			foreach (var p in predecessors[current])
			{
				if (!done[p])
				{
					current = p;
					break;
				}
			}
		}

		var first = path.IndexOf(current);
		var cycle = path.GetRange(first, path.Count - first);
		cycle.Reverse();
		cycle.Add(cycle[0]);
		return cycle;
	}
}
