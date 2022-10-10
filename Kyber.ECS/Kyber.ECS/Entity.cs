namespace Kyber.ECS;

[WrapperValueObject(typeof(ulong))]//, GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct EntityId
{
    public readonly uint Id => _value.High();
    public readonly ushort Generation => _value.LowFront();
    public readonly ushort Flags => (ushort)(_value & 0xffff);

    public EntityId(uint entity, ushort generation = 0, ushort flags = 0)
    {
        _value = ((ulong)entity << 32) + ((ulong)generation << 16) + flags;
    }

    public EntityId NextGen()
    {
        unchecked
        {
            return new(Id, (ushort)(Generation + 1));
        }

    }

    public override string ToString() => $"e{Id:X}.{Generation:X}";
}

public readonly struct Entity : IEquatable<Entity>
{
    public readonly EntityId Id;
    public readonly WorldId WorldId;

    public Entity(EntityId id, WorldId worldId)
    {
        Id = id;
        WorldId = worldId;
    }

    public static bool operator ==(Entity a, Entity b) => a.Id == b.Id && a.WorldId == b.WorldId;
    public static bool operator !=(Entity a, Entity b) => !(a == b);

    public override bool Equals(object? obj) => obj is Entity e && e == this;

    public bool Equals(Entity other) => this == other;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 41;
            hash += Id.GetHashCode() * 23;
            hash += WorldId.GetHashCode() * 51;
            return hash;
        }
    }

    public Archetype? Archetype => World.All[WorldId]?.ArchetypeByEntityId[Id];

    public bool IsAlive => World.All[WorldId]?.IsAlive(Id) ?? false;

    public bool Has<T>()
    {
        return World.All[WorldId]?.HasComponent<T>(Id) ?? false;
    }

    public bool Tagged<T>()
    {
        return World.All[WorldId]?.HasTag<T>(Id) ?? false;
    }

    public Entity Tag<T>()
    {
        World.All[WorldId]?.Tag(Id, typeof(T));
        return this;
    }

    public Entity Untag<T>()
    {
        World.All[WorldId]?.Untag(Id, typeof(T));
        return this;
    }

    public ref T Get<T>()
    {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        return ref World.All[WorldId].GetComponent<T>(Id);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
    }

    public Entity Set<T>(T value)
    {
        World.All[WorldId]?.Set(Id, value);
        return this;
    }

    public Entity Unset<T>()
    {
        World.All[WorldId]?.Unset<T>(Id);
        return this;
    }

    public void Destroy()
    {
        World.All[WorldId]?.DestroyEntity(Id);
    }
}
