namespace Kyber.ECS;

internal static class ByteExtensions
{
    public static uint High(this ulong value)
    {
        unchecked
        {
            return (uint)(value >> 32);
        }
    }

    public static uint Low(this ulong value)
    {
        unchecked
        {
            return (uint)(value & 0xffffffff);
        }
    }

    public static ushort LowFront(this ulong value)
    {
        unchecked
        {
            return (ushort)((value & 0xffff_ffff) >> 16);
        }
    }

    public static ulong Pack(uint high, uint low)
    {
        unchecked
        {
            ulong newHigh = high;
            newHigh = newHigh << 32;

            return newHigh + low;
        }
    }
}
