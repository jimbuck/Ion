namespace Kyber.ECS;

[WrapperValueObject(typeof(ulong), GenerateImplicitConversionToPrimitive = true)]
public readonly partial struct TypeId
{
    public override string ToString() => "t" + Value.ToString();

    public static readonly TypeId Empty = 0;

    public static TypeId Create(ICollection<Type> components, ICollection<Type> tags)
    {
        if (components.Count == 0 && tags.Count == 0) return Empty;

        unchecked
        {
            ulong hash = 19;
            ulong index = 1;
            foreach(var component in components.OrderBy(t => t.FullName)) hash = hash * 31 + ((ulong)component.GetHashCode() * index++);
            foreach (var tag in tags.OrderBy(t => t.FullName)) hash = hash * 31 + ((ulong)tag.GetHashCode() * index++);

            Console.WriteLine($"t{hash} ({string.Join('|', components.Select(t => t.Name))};{string.Join('|', tags.Select(t => t.Name))})");

            return hash;
        }
    }
}
