﻿namespace Kyber.ECS;

[WrapperValueObject(typeof(int), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct WorldId {
    public override string ToString() => $"w{_value}";
}

public record class ArchetypeGraphEntry(Archetype Prev, Archetype Next, ComponentId ComponentId);

public partial class World : IDisposable, IEnumerable<Archetype>
{
    internal static World?[] All = new World?[2];
    private static readonly object _lockObject = new();
    private static readonly IntPool _worldIds = new();

    private bool _disposed;

    internal WorldId WorldId { get; }

    private readonly UIntPool _archetypeIds = new();

    internal readonly EntityIdPool EntityIds = new();
    internal readonly Dictionary<TypeId, Archetype> ArchetypeByTypeId = new();
    internal readonly Dictionary<EntityId, Archetype> ArchetypeByEntityId = new();
    internal readonly Dictionary<ComponentId, HashSet<Archetype>> ArchetypesByComponentId = new();

    public string Name { get; }

    public int EntityCount => ArchetypeByEntityId.Count;

    public World(string? name = null)
    {
        lock (_lockObject)
        {
            WorldId = _worldIds.Next();
            if (WorldId >= All.Length) Array.Resize(ref All, All.Length * 2);

            All[WorldId] = this;
        }

        Name = name ?? WorldId.ToString();
        ArchetypeByTypeId[TypeId.Empty] = Archetype.Empty(this);
    }

    public EntityId CreateEntity()
    {
        var archetype = ArchetypeByTypeId[TypeId.Empty];

        var entityId = archetype.CreateEntity();
        ArchetypeByEntityId[entityId] = archetype;

        return entityId;
    }

    public void DestroyEntity(EntityId entityId)
    {
        if (!ArchetypeByEntityId.TryGetValue(entityId, out var archetype)) return;
        archetype.DestroyEntity(entityId);
        ArchetypeByEntityId.Remove(entityId);
    }

    public bool IsAlive(EntityId entityId)
    {
        if (!ArchetypeByEntityId.TryGetValue(entityId, out var archetype)) return false;
        return archetype.IsAlive(entityId);
    }

    public bool HasComponent(EntityId entityId, ComponentId componentId)
    {
        if (!ArchetypeByEntityId.TryGetValue(entityId, out var archetype) || !ArchetypesByComponentId.TryGetValue(componentId, out var applicableArchetypes)) return false;
        return applicableArchetypes.Contains(archetype);
    }

    public bool TryGetComponent<T>(EntityId entityId, ref T value)
    {
        return ArchetypeByEntityId.TryGetValue(entityId, out var archetype) && archetype.TryGetComponent(entityId, ref value);
    }

    public World Add(EntityId entityId, params Type[] tagTypes)
    {
        var srcArchetype = ArchetypeByEntityId[entityId];

        var destArchetype = GetOrCreateNewArchetypeWith(srcArchetype, Array.Empty<Type>(), tagTypes);

        if (srcArchetype != destArchetype) srcArchetype.Move(destArchetype, entityId);
        ArchetypeByEntityId[entityId] = destArchetype;

        return this;
    }

    public World Remove(EntityId entityId, params Type[] tagTypes)
    {
        var srcArchetype = ArchetypeByEntityId[entityId];

        var destArchetype = GetOrCreateNewArchetypeWithout(srcArchetype, Array.Empty<Type>(), tagTypes);

        if (srcArchetype != destArchetype)
        {
            srcArchetype.Move(destArchetype, entityId);
            ArchetypeByEntityId[entityId] = destArchetype;
            foreach(var type in tagTypes)
            {
                var componentId = ComponentId.From(type);
                srcArchetype.Edges[componentId].Remove = destArchetype;
                destArchetype.Edges[componentId].Add = srcArchetype;
            }
        }

        return this;
    }

    public World Set<T>(EntityId entityId, T value)
    {
        var componentId = ComponentId.From<T>();
        var srcArchetype = ArchetypeByEntityId[entityId];

        var destArchetype = GetOrCreateNewArchetypeWith(srcArchetype, new[] {typeof(T)}, Array.Empty<Type>());

        if (srcArchetype != destArchetype)
        {
            srcArchetype.Move(destArchetype, entityId);
            ArchetypeByEntityId[entityId] = destArchetype;

            srcArchetype.Edges.TryAdd(componentId, new());
            srcArchetype.Edges[componentId].Add = destArchetype;
            destArchetype.Edges.TryAdd(componentId, new());
            destArchetype.Edges[componentId].Remove = srcArchetype;
        }

        destArchetype.SetComponent(entityId, value);

        return this;
    }

    public Query CreateQuery()
    {
        return new Query(this);
    }

    internal Archetype GetOrCreateNewArchetypeWith(Archetype srcArchetype, Type[] withComponents, Type[] withTags)
    {
        var newCompTypes = new HashSet<Type>(srcArchetype.ComponentTypes);
        foreach (var compType in withComponents) newCompTypes.Add(compType);

        var newTagTypes = new HashSet<Type>(srcArchetype.TagTypes);
        foreach (var tagType in withTags) newTagTypes.Add(tagType);

        return EnsureArchetypeExists(newCompTypes, newTagTypes);
    }

    internal Archetype GetOrCreateNewArchetypeWithout(Archetype srcArchetype, Type[] withoutComponents, Type[] withoutTags)
    {
        var newCompTypes = new HashSet<Type>(srcArchetype.ComponentTypes);
        foreach (var compType in withoutComponents) newCompTypes.Remove(compType);

        var newTagTypes = new HashSet<Type>(srcArchetype.TagTypes);
        foreach (var tagType in withoutTags) newTagTypes.Remove(tagType);

        return EnsureArchetypeExists(newCompTypes, newTagTypes);
    }

    internal Archetype EnsureArchetypeExists(HashSet<Type> newCompTypes, HashSet<Type> newTagTypes)
    {
        var newTypeId = TypeId.Create(newCompTypes, newTagTypes);
        ArchetypeByTypeId.TryGetValue(newTypeId, out var destArchetype);
        destArchetype ??= ArchetypeByTypeId[newTypeId] = new Archetype(this, _archetypeIds.Next(), newCompTypes, newTagTypes);

        foreach (var componentId in destArchetype.Components.Keys)
        {
            ArchetypesByComponentId.TryAdd(componentId, new());
            ArchetypesByComponentId[componentId].Add(destArchetype);
        }

        return destArchetype;
    }

    /// <summary>
    /// Traverse the Archetype graph.
    /// </summary>
    /// <returns>The archetype edges, breadth first.</returns>
    public IEnumerable<ArchetypeGraphEntry> ArchetypeGraph()
    {
        var archetypes = new Queue<Archetype>(new[] { ArchetypeByTypeId[TypeId.Empty] });

        while (archetypes.Count > 0)
        {
            var prev = archetypes.Dequeue();
            foreach (var edge in prev.Edges)
            {
                if (edge.Value.Add != null)
                {
                    yield return new ArchetypeGraphEntry(prev, edge.Value.Add, edge.Key);
                    archetypes.Enqueue(edge.Value.Add);
                }
            }
        }
    }

    /// <summary>
    /// Iterate all archetypes that match the provided with/without conditions.
    /// </summary>
    /// <param name="with">Matching archetypes must include these types.</param>
    /// <param name="without">Matching archetypes must exclude these types.</param>
    /// <returns>The matching archetypes, breadth first.</returns>
    public IEnumerable<Archetype> Archetypes(IEnumerable<ComponentId> with, IEnumerable<ComponentId>? without = null)
    {
        var archetypes = new Queue<Archetype>(new[] { ArchetypeByTypeId[TypeId.Empty] });

        var withSet = new HashSet<ComponentId>(with);
        var withoutSet = new HashSet<ComponentId>(without ?? Array.Empty<ComponentId>());
        var visited = new HashSet<Archetype>();

        while (archetypes.Count > 0)
        {
            var prev = archetypes.Dequeue();
            foreach (var edge in prev.Edges)
            {
                if (!withoutSet.Contains(edge.Key) && edge.Value.Add != null && !visited.Contains(edge.Value.Add))
                {
                    if (edge.Value.Add.ComponentIds.IsSupersetOf(withSet)) yield return edge.Value.Add;

                    archetypes.Enqueue(edge.Value.Add);
                    visited.Add(edge.Value.Add);
                }
            }
        }
    }

    public override string ToString()
    {
        return Name ?? WorldId.ToString();
    }

    public string ToGraphString() {
        var graphString = new System.Text.StringBuilder();
        foreach (var link in ArchetypeGraph())
        {
            graphString.AppendLine($"{link.Prev} {link.Next} {link.ComponentId.Type.Name}");
        }
        return graphString.ToString();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: set large fields to null
            All[WorldId] = null;
            _worldIds.Recycle(WorldId);
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public IEnumerator<Archetype> GetEnumerator()
    {
        return ArchetypeByTypeId.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
