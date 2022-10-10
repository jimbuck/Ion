namespace Kyber.ECS;

[WrapperValueObject(typeof(uint), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct ArchetypeId { }

public class ArchetypeEdge
{
    public Archetype? Add;
    public Archetype? Remove;
}

public class Archetype
{
    private readonly World _world;
    private readonly IntPool _rowIds = new();

    public ArchetypeId Id { get; init; }

    public Type[] ComponentTypes { get; init; }
    public Type[] TagTypes { get; init; }

    public TypeId TypeId { get; init; }

    public HashSet<ComponentId> ComponentIds { get; }

    public readonly Dictionary<EntityId, int> RowIndex = new(64);
    public Dictionary<ComponentId, DataBuffer> Components { get; init; }
    public Dictionary<ComponentId, ArchetypeEdge> Edges { get; init; }

    public Archetype(World world, ArchetypeId id, ICollection<Type>? components = null, ICollection<Type>? tags = null)
    {
        _world = world;
        Id = id;
        ComponentTypes = components?.ToArray() ?? Array.Empty<Type>();
        TagTypes = tags?.ToArray() ?? Array.Empty<Type>();

        TypeId = TypeId.Create(ComponentTypes, TagTypes);
        Components = new (ComponentTypes.Length);
        Edges = new Dictionary<ComponentId, ArchetypeEdge>();

        ComponentIds = new HashSet<ComponentId>(ComponentTypes.Concat(TagTypes).Select(t => ComponentId.From(t)));

        foreach (var type in ComponentTypes)
        {
            var componentId = ComponentId.From(type);
            Components[componentId] = DataBuffer.Create(type);
            Edges[componentId] = new();
        }
    }

    public static Archetype Empty(World world)
    {
        return new(world, 0);
    }

    public EntityId CreateEntity()
    {
        var entityId = _world.EntityIds.Next();
        return CreateEntity(entityId);
    }

    internal EntityId CreateEntity(EntityId entityId)
    {
        RowIndex[entityId] = _rowIds.Next();
        return entityId;
    }

    public bool IsAlive(EntityId entityId)
    {
        return RowIndex.ContainsKey(entityId);
    }

    public ref T GetComponent<T>(EntityId entityId)
    {
        return ref Components[ComponentId.From<T>()].Get<T>(RowIndex[entityId]);
    }

    public bool TryGetComponent<T>(EntityId entityId, ref T value)
    {
        var componentId = ComponentId.From<T>();
        if (RowIndex.TryGetValue(entityId, out var index) && Components.TryGetValue(componentId, out var buffer))
        {
            value = ref buffer.Get<T>(index);
            return true;
        }

        return false;
    }

    public void SetComponent<T>(EntityId entityId, T value)
    {
        if (!IsAlive(entityId)) return;

        var componentId = ComponentId.From<T>();
        Components[componentId].Data[RowIndex[entityId]] = value;
        // TODO: Trigger event emitter?
    }

    public void DestroyEntity(EntityId entityId)
    {
        if (!IsAlive(entityId)) return;

        _world.EntityIds.Recycle(entityId);
        RowIndex.Remove(entityId);
    }

    public override string ToString()
    {
        return $"[{string.Join(string.Empty, ComponentTypes.Select(t => t.Name)) + (TagTypes.Length > 0 ? $".{string.Join(string.Empty, TagTypes.Select(t => t.Name))}" : string.Empty)}]";
    }

    public static void Move(Archetype src, Archetype dest, EntityId entityId)
    {
        var srcRow = src.RowIndex[entityId];
        var destRow = dest.RowIndex[entityId] = dest._rowIds.Next();

        foreach (var componentId in src.Components.Keys)
        {
            if (!dest.Components.ContainsKey(componentId)) continue;

            src.Components[componentId].CopyTo(srcRow, dest.Components[componentId], destRow);
        }

        src.RowIndex.Remove(entityId);
    }
}

public static class ArchetypeExtensions
{
    public static void Move(this Archetype src, Archetype dest, EntityId entityId)
    {
        Archetype.Move(src, dest, entityId);
    }
}
