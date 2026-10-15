using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// The parent of an entity in the transform hierarchy. Maintain it with <see cref="HierarchyExtensions.SetParent(World, Entity, Entity)"/> (or
/// <see cref="Commands.SetParent(Entity, Entity)"/> inside a query), which also keeps the parent's <see cref="Children"/> in step.
/// </summary>
/// <param name="Value">The parent entity.</param>
public readonly record struct Parent(Entity Value);

/// <summary>
/// The children of an entity, in sibling order: the order the transform propagation visits them and the serializers save
/// them. A child is appended when parented, unless an index is given (<see cref="HierarchyExtensions.SetParent(World, Entity, Entity, int)"/>),
/// and can be moved among its siblings (<see cref="HierarchyExtensions.SetSiblingIndex"/>). Maintained by those and
/// <see cref="HierarchyExtensions.RemoveParent"/>.
/// </summary>
public struct Children
{
	internal Entity[]? Items;
	internal int Length;

	/// <summary>The number of children.</summary>
	public readonly int Count => Length;

	/// <summary>The child at <paramref name="index"/>.</summary>
	public readonly Entity this[int index] => (uint)index < (uint)Length ? Items![index] : throw new ArgumentOutOfRangeException(nameof(index));

	/// <summary>The children as a span (valid until the hierarchy changes).</summary>
	public readonly ReadOnlySpan<Entity> AsSpan() => Items is null ? default : Items.AsSpan(0, Length);

	internal void Add(Entity child)
	{
		Items ??= new Entity[4];
		if (Length == Items.Length) Array.Resize(ref Items, Length * 2);
		Items[Length++] = child;
	}

	/// <summary>Inserts <paramref name="child"/> at <paramref name="index"/> (clamped to the ends).</summary>
	internal void Insert(int index, Entity child)
	{
		Add(child);
		var at = Math.Clamp(index, 0, Length - 1);
		if (at == Length - 1) return;
		Array.Copy(Items!, at, Items!, at + 1, Length - 1 - at);
		Items![at] = child;
	}

	/// <summary>The index of <paramref name="child"/>, or -1.</summary>
	internal readonly int IndexOf(Entity child) => Items is null ? -1 : Array.IndexOf(Items, child, 0, Length);

	/// <summary>Moves <paramref name="child"/> to <paramref name="index"/> (clamped to the ends). Returns false when it is not a child.</summary>
	internal bool Move(Entity child, int index)
	{
		var from = IndexOf(child);
		if (from < 0) return false;
		var to = Math.Clamp(index, 0, Length - 1);
		if (from == to) return true;
		if (from < to) Array.Copy(Items!, from + 1, Items!, from, to - from);
		else Array.Copy(Items!, to, Items!, to + 1, from - to);
		Items![to] = child;
		return true;
	}

	internal bool Remove(Entity child)
	{
		if (Items is null) return false;
		var index = Array.IndexOf(Items, child, 0, Length);
		if (index < 0) return false;
		Array.Copy(Items, index + 1, Items, index, Length - index - 1);
		Items[--Length] = default;
		return true;
	}

	/// <summary>Creates the component from a list of children (serialization).</summary>
	public static Children From(ReadOnlySpan<Entity> children)
	{
		var result = new Children();
		foreach (var child in children) result.Add(child);
		return result;
	}

	/// <inheritdoc/>
	public override readonly string ToString() => $"Children {{ Count = {Length} }}";
}

/// <summary>A parent (and sibling index, -1 to append) recorded by <see cref="Commands.SetParent(Entity, Entity)"/> for an entity the same commands create; applied at playback.</summary>
internal readonly record struct PendingParent(Entity Value, int Index = -1);

/// <summary>
/// Hierarchy operations on an Arch <see cref="World"/>. They are structural changes (they add or remove
/// <see cref="Parent"/> and <see cref="Children"/>): inside a query, use the <see cref="Commands"/> equivalents.
/// </summary>
public static class HierarchyExtensions
{
	/// <summary>
	/// Makes <paramref name="child"/> a child of <paramref name="parent"/> (moving it from its previous parent), appending
	/// it to the parent's <see cref="Children"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">The operation would create a cycle, or an entity is not alive.</exception>
	public static void SetParent(this World world, Entity child, Entity parent) => SetParent(world, child, parent, -1);

	/// <summary>
	/// Makes <paramref name="child"/> a child of <paramref name="parent"/> at sibling <paramref name="index"/> of the
	/// parent's <see cref="Children"/> (clamped; a negative index appends), moving it from its previous parent. Already a
	/// child of <paramref name="parent"/>, it moves to <paramref name="index"/> among its siblings.
	/// </summary>
	/// <exception cref="InvalidOperationException">The operation would create a cycle, or an entity is not alive.</exception>
	public static void SetParent(this World world, Entity child, Entity parent, int index)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(child)) throw new InvalidOperationException($"Cannot parent {child}: it is not alive.");
		if (!world.IsAlive(parent)) throw new InvalidOperationException($"Cannot parent {child} to {parent}: the parent is not alive.");

		for (var ancestor = parent; ; )
		{
			if (ancestor == child) throw new InvalidOperationException($"Cannot parent {child} to {parent}: {child} is {(parent == child ? "the parent itself" : "an ancestor of the parent")}, which would make a cycle.");
			if (!world.TryGet<Parent>(ancestor, out var next) || !world.IsAlive(next.Value)) break;
			ancestor = next.Value;
		}

		if (world.TryGet<Parent>(child, out var previous))
		{
			if (previous.Value == parent)
			{
				if (index >= 0 && world.Has<Children>(parent)) world.Get<Children>(parent).Move(child, index);
				return;
			}

			if (world.IsAlive(previous.Value) && world.Has<Children>(previous.Value)) world.Get<Children>(previous.Value).Remove(child);
			world.Set(child, new Parent(parent));
		}
		else
		{
			world.Add(child, new Parent(parent));
		}

		if (!world.Has<Children>(parent)) world.Add(parent, new Children());
		ref var children = ref world.Get<Children>(parent);
		if (index < 0) children.Add(child);
		else children.Insert(index, child);
		Invalidate(world, child);
	}

	/// <summary>
	/// Moves <paramref name="child"/> to <paramref name="index"/> among its siblings (clamped to the ends; 0 is first),
	/// keeping its parent. Does nothing for a root. Not a structural change: no component is added or removed.
	/// </summary>
	public static void SetSiblingIndex(this World world, Entity child, int index)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(child) || !world.TryGet<Parent>(child, out var parent) || !world.IsAlive(parent.Value) || !world.Has<Children>(parent.Value)) return;
		world.Get<Children>(parent.Value).Move(child, index);
	}

	/// <summary>The index of <paramref name="child"/> among its siblings, or -1 for a root.</summary>
	public static int GetSiblingIndex(this World world, Entity child)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(child) || !world.TryGet<Parent>(child, out var parent) || !world.IsAlive(parent.Value) || !world.Has<Children>(parent.Value)) return -1;
		return world.Get<Children>(parent.Value).IndexOf(child);
	}

	/// <summary>Makes the transform propagation recompute <paramref name="entity"/> (and so its descendants) on its next run.</summary>
	internal static void Invalidate(World world, Entity entity)
	{
		ref var global2D = ref world.TryGetRef<GlobalTransform2D>(entity, out var has2D);
		if (has2D) global2D.Local.Position = new System.Numerics.Vector2(float.NaN);
		ref var global3D = ref world.TryGetRef<GlobalTransform>(entity, out var has3D);
		if (has3D) global3D.Local.Position = new System.Numerics.Vector3(float.NaN);
	}

	/// <summary>Detaches <paramref name="child"/> from its parent (it becomes a root). Does nothing for a root.</summary>
	public static void RemoveParent(this World world, Entity child)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(child) || !world.TryGet<Parent>(child, out var parent)) return;
		if (world.IsAlive(parent.Value) && world.Has<Children>(parent.Value)) world.Get<Children>(parent.Value).Remove(child);
		world.Remove<Parent>(child);
		Invalidate(world, child);
	}

	/// <summary>The parent of <paramref name="entity"/>, or false for a root.</summary>
	public static bool TryGetParent(this World world, Entity entity, out Entity parent)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (world.IsAlive(entity) && world.TryGet<Parent>(entity, out var value))
		{
			parent = value.Value;
			return true;
		}

		parent = Entity.Null;
		return false;
	}

	/// <summary>The children of <paramref name="entity"/> (empty when it has none), valid until the hierarchy changes.</summary>
	public static ReadOnlySpan<Entity> GetChildren(this World world, Entity entity)
	{
		ArgumentNullException.ThrowIfNull(world);
		return world.IsAlive(entity) && world.Has<Children>(entity) ? world.Get<Children>(entity).AsSpan() : default;
	}

	/// <summary>Destroys <paramref name="entity"/> and all its descendants, detaching it from its parent first.</summary>
	public static void DestroyRecursive(this World world, Entity entity)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(entity)) return;
		world.RemoveParent(entity);
		DestroyTree(world, entity);
	}

	private static void DestroyTree(World world, Entity entity)
	{
		if (world.Has<Children>(entity))
		{
			// Copy: destroying children does not touch this list, but keep the traversal independent of it.
			var children = world.Get<Children>(entity).AsSpan().ToArray();
			foreach (var child in children)
			{
				if (world.IsAlive(child)) DestroyTree(world, child);
			}
		}

		world.Destroy(entity);
	}
}
