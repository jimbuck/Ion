namespace Kyber.ECS.Tests;

public class ByteExtensionsTests
{
    [Theory]
    [InlineData((ulong)0x0000_0000_0000_0000, (uint)0x0000_0000)]
    [InlineData((ulong)0xffff_ffff_ffff_ffff, (uint)0xffff_ffff)]
    [InlineData((ulong)0x00ff_ff00_ffff_ffff, (uint)0x00ff_ff00)]
    [InlineData((ulong)0x0123_4567_89ab_cdef, (uint)0x0123_4567)]
    public void High(ulong src, uint expected)
    {
        Assert.Equal(expected, src.High());
    }

    [Theory]
    [InlineData((ulong)0x0000_0000_0000_0000, (uint)0x0000_0000)]
    [InlineData((ulong)0xffff_ffff_ffff_ffff, (uint)0xffff_ffff)]
    [InlineData((ulong)0xffff_ffff_f0f0_f0f0, (uint)0xf0f0_f0f0)]
    [InlineData((ulong)0x0123_4567_89ab_cdef, (uint)0x89ab_cdef)]
    public void Low(ulong src, uint expected)
    {
        Assert.Equal(expected, src.Low());
    }

    [Theory]
    [InlineData((ulong)0x0000_0000_0000_0000, (ushort)0x0000)]
    [InlineData((ulong)0xffff_ffff_ffff_ffff, (ushort)0xffff)]
    [InlineData((ulong)0xffff_ffff_abcd_0123, (ushort)0xabcd)]
    [InlineData((ulong)0x0123_4567_89ab_cdef, (ushort)0x89ab)]
    public void LowFront(ulong src, ushort expected)
    {
        Assert.Equal(expected, src.LowFront());
    }

    [Theory]
    [InlineData((uint)0x0000_0000, (uint)0x0000_0000, (ulong)0x0000_0000_0000_0000)]
    [InlineData((uint)0xffff_ffff, (uint)0xffff_ffff, (ulong)0xffff_ffff_ffff_ffff)]
    [InlineData((uint)0x00ff_ff00, (uint)0xf0f0_f0f0, (ulong)0x00ff_ff00_f0f0_f0f0)]
    public void Pack(uint a, uint b, ulong expected)
    {
        Assert.Equal(expected, ByteExtensions.Pack(a, b));
    }
}
