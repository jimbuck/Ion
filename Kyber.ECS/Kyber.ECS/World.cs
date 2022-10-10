namespace Kyber.ECS;

[WrapperValueObject(typeof(int), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct WorldId {
    public override string ToString() => $"w{_value}";
}

public partial class World : IDisposable
{
    internal static World?[] All = new World?[2];
    private static readonly object _worldLock = new();
    private static readonly IntPool _worldIds = new();

    private bool _disposed;

    internal WorldId WorldId { get; }

    private readonly UIntPool _archetypeIds = new();

    internal readonly EntityIdPool EntityIds = new();
    internal readonly Dictionary<TypeId, Archetype> ArchetypeByTypeId = new();
    internal readonly Dictionary<EntityId, Archetype> ArchetypeByEntityId = new();
    internal readonly Dictionary<ComponentId, HashSet<Archetype>> ArchetypesByComponentId = new();
    internal readonly Dictionary<Type, HashSet<Archetype>> ArchetypesByTagType = new();

    public string Name { get; }

    public int EntityCount => ArchetypeByEntityId.Count;

    public World(string? name = null)
    {
        lock (_worldLock)
        {
            WorldId = _worldIds.Next();
            if (WorldId >= All.Length) Array.Resize(ref All, All.Length * 2);

            All[WorldId] = this;
        }

        Name = name ?? WorldId.ToString();
        ArchetypeByTypeId[TypeId.Empty] = Archetype.Empty(this);
    }

    public EntityId CreateEntityId()
    {
        var archetype = ArchetypeByTypeId[TypeId.Empty];

        var entityId = archetype.CreateEntity();
        ArchetypeByEntityId[entityId] = archetype;

        return entityId;
    }

    public Entity CreateEntity()
    {
        return new Entity(CreateEntityId(), WorldId);
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

    public bool HasComponent<T>(EntityId entityId)
    {
        if (!ArchetypeByEntityId.TryGetValue(entityId, out var archetype) || !ArchetypesByComponentId.TryGetValue(ComponentId.From<T>(), out var applicableArchetypes)) return false;
        return applicableArchetypes.Contains(archetype);
    }

    public bool HasTag<T>(EntityId entityId)
    {
        return ArchetypeByEntityId.TryGetValue(entityId, out var archetype) && archetype.TagTypes.Contains(typeof(T));
    }

    public ref T GetComponent<T>(EntityId entityId)
    {
        return ref ArchetypeByEntityId[entityId].GetComponent<T>(entityId);
    }

    public bool TryGetComponent<T>(EntityId entityId, ref T value)
    {
        return ArchetypeByEntityId.TryGetValue(entityId, out var archetype) && archetype.TryGetComponent(entityId, ref value);
    }

    public World Tag(EntityId entityId, params Type[] tagTypes)
    {
        var srcArchetype = ArchetypeByEntityId[entityId];

        var destArchetype = GetOrCreateNewArchetypeWith(srcArchetype, Array.Empty<Type>(), tagTypes);

        if (srcArchetype != destArchetype) srcArchetype.Move(destArchetype, entityId);
        ArchetypeByEntityId[entityId] = destArchetype;

        return this;
    }

    public World Untag(EntityId entityId, params Type[] tagTypes)
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
                srcArchetype.Edges.TryAdd(componentId, new());
                srcArchetype.Edges[componentId].Remove = destArchetype;
                destArchetype.Edges.TryAdd(componentId, new());
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

    public World Unset<T>(EntityId entityId)
    {
        var componentId = ComponentId.From<T>();
        var srcArchetype = ArchetypeByEntityId[entityId];

        var destArchetype = GetOrCreateNewArchetypeWithout(srcArchetype, new[] { typeof(T) }, Array.Empty<Type>());

        if (srcArchetype != destArchetype)
        {
            srcArchetype.Move(destArchetype, entityId);
            ArchetypeByEntityId[entityId] = destArchetype;

            srcArchetype.Edges.TryAdd(componentId, new());
            srcArchetype.Edges[componentId].Remove = destArchetype;
            destArchetype.Edges.TryAdd(componentId, new());
            destArchetype.Edges[componentId].Add = srcArchetype;
        }

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

        foreach(var tag in destArchetype.TagTypes)
        {
            ArchetypesByTagType.TryAdd(tag, new());
            ArchetypesByTagType[tag].Add(destArchetype);
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
                    yield return new ArchetypeGraphEntry(prev, edge.Key, edge.Value.Add);
                    archetypes.Enqueue(edge.Value.Add);
                }
            }
        }
    }

    /// <summary>
    /// Iterates through every archetype in the world.
    /// </summary>
    /// <returns>The enumerator for archetypes.</returns>
    public IEnumerable<Archetype> Archetypes()
    {
        return ArchetypeByTypeId.Values;
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
            graphString.AppendLine($"{link.Prev} {link.Next} {link.ComponentId}");
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
            lock (_worldLock)
            {
                All[WorldId] = null;
                _worldIds.Recycle(WorldId);
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public readonly struct ArchetypeGraphEntry : IEquatable<ArchetypeGraphEntry>
{
    public readonly Archetype Prev;
    public readonly ComponentId ComponentId;
    public readonly Archetype Next;

    public ArchetypeGraphEntry(Archetype prev, ComponentId componentId, Archetype next)
    {
        Prev = prev;
        ComponentId = componentId;
        Next = next;
    }

    public bool Equals(ArchetypeGraphEntry other)
    {
        return Prev == other.Prev && ComponentId == other.ComponentId && Next == other.Next;
    }

    public static bool operator ==(ArchetypeGraphEntry a, ArchetypeGraphEntry b) => a.Prev == b.Prev && a.ComponentId == b.ComponentId && a.Next == b.Next;
    public static bool operator !=(ArchetypeGraphEntry a, ArchetypeGraphEntry b) => !(a == b);

    public override bool Equals(object? obj) => obj is ArchetypeGraphEntry entry && this == entry;

    public override int GetHashCode()
    {
        return Prev.GetHashCode() * 7 + ComponentId.GetHashCode() * 13 + Next.GetHashCode() * 31;
    }
}