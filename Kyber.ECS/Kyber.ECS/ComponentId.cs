namespace Kyber.ECS;

[WrapperValueObject(typeof(ulong), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct ComponentId
{
    internal readonly static ConcurrentDictionary<uint, Type> TypeIndex = new();

    public uint Id => _value.High();
    public uint Relation => _value.Low();

    public Type Type => TypeIndex[Id];

    public ComponentId(uint id, uint relation = 0)
    {
        _value = ((ulong)id << 32) + relation;
    }

    public override string ToString() => Relation == 0 ? Type.Name : $"{Type.Name}->{TypeIndex[Relation]}";

    public static ComponentId From(Type type)
    {
        var componentId = new ComponentId((uint)type.GetHashCode());
        TypeIndex[componentId.Id] = type;
        return componentId;
    }

    public static ComponentId From<T>()
    {
        return From(typeof(T));        
    }
}
