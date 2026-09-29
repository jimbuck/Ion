using Arch.Core;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Finds entities by their <see cref="EntityName"/> in one world (resolve it next to the world; the remote inspection
/// protocol uses it to address entities). The index is rebuilt from the world when a lookup misses or finds a stale
/// entry, so it needs no bookkeeping when names are added, changed or removed. With duplicate names the first entity in
/// query order wins.
/// </summary>
public sealed class NameRegistry
{
	private static readonly QueryDescription Named = new QueryDescription().WithAll<EntityName>();

	private readonly Dictionary<string, Entity> _byName = new(StringComparer.Ordinal);

	/// <summary>Creates a registry over <paramref name="world"/>.</summary>
	public NameRegistry(World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		World = world;
	}

	/// <summary>The world.</summary>
	public World World { get; }

	/// <summary>The number of distinct names found by the last rebuild.</summary>
	public int Count => _byName.Count;

	/// <summary>Finds the entity named <paramref name="name"/>.</summary>
	public bool TryFind(string name, out Entity entity)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (_byName.TryGetValue(name, out entity) && IsNamed(entity, name)) return true;

		Rebuild();
		return _byName.TryGetValue(name, out entity);
	}

	/// <summary>The entity named <paramref name="name"/>, or <see cref="Entity.Null"/>.</summary>
	public Entity Find(string name) => TryFind(name, out var entity) ? entity : Entity.Null;

	/// <summary>The name of <paramref name="entity"/>, or null when it has none (or is not alive).</summary>
	public string? NameOf(Entity entity) => World.IsAlive(entity) && World.TryGet<EntityName>(entity, out var name) ? name.Value : null;

	/// <summary>Every named entity, in query order (rebuilds the index).</summary>
	public IReadOnlyList<(string Name, Entity Entity)> Snapshot()
	{
		var result = new List<(string, Entity)>();
		foreach (ref var chunk in World.Query(in Named))
		{
			var names = chunk.GetArray<EntityName>();
			for (var i = 0; i < chunk.Count; i++) result.Add((names[i].Value, chunk.Entity(i)));
		}

		return result;
	}

	/// <summary>Rebuilds the index from the world.</summary>
	public void Rebuild()
	{
		_byName.Clear();
		foreach (ref var chunk in World.Query(in Named))
		{
			var names = chunk.GetArray<EntityName>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (names[i].Value is { } value) _byName.TryAdd(value, chunk.Entity(i));
			}
		}
	}

	private bool IsNamed(Entity entity, string name) => World.IsAlive(entity) && World.TryGet<EntityName>(entity, out var current) && current.Value == name;
}
