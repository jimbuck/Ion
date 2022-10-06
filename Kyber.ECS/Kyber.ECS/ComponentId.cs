namespace Kyber.ECS;


[WrapperValueObject(typeof(ulong), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct ComponentId
{
    private readonly static UIntPool _componentIds = new();
    internal readonly static Dictionary<Type, ComponentId> ComponentIndex = new(64);
    internal readonly static Dictionary<ComponentId, Type> TypeIndex = new(64);

    public uint Id => _value.High();
    public uint Relation => _value.Low();

    public Type Type => TypeIndex[this];

    public ComponentId(uint id, uint relation = 0)
    {
        _value = ((ulong)id << 32) + relation;
    }

    public override string ToString() => $"c{Id:X}.{Relation:X} ({Type.Name})";

    public static ComponentId From(Type type)
    {
        if (ComponentIndex.TryGetValue(type, out var componentId)) return componentId;

        componentId = new ComponentId(_componentIds.Next());
        ComponentIndex[type] = componentId;
        TypeIndex[componentId] = type;

        return componentId;
    }

    public static ComponentId From<T>()
    {
        return From(typeof(T));        
    }
}
