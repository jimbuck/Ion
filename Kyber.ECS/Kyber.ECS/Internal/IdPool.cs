namespace Kyber.ECS;

/// <summary>
/// Create IDs that can be resused.
/// <para>Based on Doraku/DefaultEcs</para>
/// </summary>
internal sealed class IntPool
{
    private readonly ConcurrentStack<int> _ids = new();
    private int _next;

    public IntPool(int initialValue = 0)
    {
        _next = initialValue; 
    }

    public int Next()
    {
        if (_ids.TryPop(out var value)) return value;

        value = _next;
        Interlocked.Increment(ref _next);
        return value;
    }

    public void Recycle(int releasedInt) => _ids.Push(releasedInt);
}

internal sealed class UIntPool
{
    private readonly ConcurrentStack<uint> _ids = new();
    private uint _last = 0;

    public uint Next()
    {
        if (!_ids.TryPop(out var freeInt)) freeInt = Interlocked.Increment(ref _last);

        return freeInt;
    }

    public void Recycle(uint releasedUInt) => _ids.Push(releasedUInt);
}

internal sealed class LongPool
{
    private readonly ConcurrentStack<long> _ids = new();
    private long _last = 0;

    public long Next()
    {
        if (!_ids.TryPop(out var freeInt)) freeInt = Interlocked.Increment(ref _last);

        return freeInt;
    }

    public void Recycle(long releasedLong) => _ids.Push(releasedLong);
}

internal sealed class EntityIdPool
{
    private readonly ConcurrentStack<EntityId> _ids = new();
    private uint _last = 0;

    public EntityId Next()
    {
        if (!_ids.TryPop(out var freeEntityId))
        {
            Interlocked.Increment(ref _last);
            freeEntityId = new EntityId(_last, 0);
        }

        return freeEntityId;
    }

    public void Recycle(EntityId releasedLong)
    {
        _ids.Push(new EntityId(releasedLong.Id, (ushort)(releasedLong.Generation + 1)));
    }
}