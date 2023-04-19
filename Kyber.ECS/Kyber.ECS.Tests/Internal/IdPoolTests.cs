namespace Kyber.ECS.Tests;

public class IdPoolTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void IntPool_StartsAt()
    {
        var intPool0 = new IntPool(0);
        Assert.Equal(0, intPool0.Next());

        var intPool1 = new IntPool(1);
        Assert.Equal(1, intPool1.Next());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void IntPool_Incremental()
    {
        var intPool = new IntPool(1);

        Assert.Equal(1, intPool.Next());
        Assert.Equal(2, intPool.Next());
        Assert.Equal(3, intPool.Next());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void IntPool_Recycle()
    {
        var intPool = new IntPool(1);

        Assert.Equal(1, intPool.Next());
        Assert.Equal(2, intPool.Next());
        intPool.Recycle(1);
        Assert.Equal(1, intPool.Next());
        Assert.Equal(3, intPool.Next());
        intPool.Recycle(3);
        intPool.Recycle(2);
        Assert.Equal(2, intPool.Next());
        Assert.Equal(3, intPool.Next());
    }
}
