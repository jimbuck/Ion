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
