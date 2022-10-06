namespace Kyber.ECS;

public abstract class DataBuffer {
    public IList Data;

    public int Length => Data.Count;

    public bool TryGet<T>([NotNullWhen(true)]out T[]? array)
    {
        array = Data as T[];
        return array is not null;
    }

    public ref T Get<T>(int index)
    {
        return ref ((T[])Data)[index];
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected DataBuffer() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public abstract void Resize(int newSize);

    public abstract void CopyTo(int index, DataBuffer dest, int newIndex);

    public static DataBuffer Create(Type type, int initialSize = 64)
    {
        if (Activator.CreateInstance(typeof(DataBuffer<>).MakeGenericType(type), initialSize) is not DataBuffer buffer) throw new Exception($"Failed to create {nameof(DataBuffer)} for '{type.FullName}'!");

        return buffer;
    }
}

public class DataBuffer<T> : DataBuffer
{
    private T?[] _data;

    public DataBuffer(int initialSize)
    {
        _data = new T[initialSize];
        Data = _data;
    }

    public override void Resize(int newSize)
    {
        Array.Resize(ref _data, newSize);
        Data = _data;
    }

    public override void CopyTo(int index, DataBuffer dest, int newIndex)
    {
        dest.Data[newIndex] = _data[index];
        _data[index] = default;
    }
}