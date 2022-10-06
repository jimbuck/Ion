namespace Kyber.ECS.Tests;

public class TypeIdTests
{
    [Theory]
    [InlineData(
        new[] { typeof(string), typeof(int) }, new Type[] {},
        new[] { typeof(int), typeof(string) }, new Type[] {}
    )]
    [InlineData(
        new[] { typeof(float) }, new Type[] { typeof(string), typeof(int) },
        new[] { typeof(float) }, new Type[] { typeof(int), typeof(string) }
    )]
    [InlineData(
        new[] { typeof(float), typeof(short) }, new Type[] { typeof(string), typeof(int) },
        new[] { typeof(short), typeof(float) }, new Type[] { typeof(int), typeof(string) }
    )]
    public void Create_Same(Type[] aCmps, Type[] aTags, Type[] bCmps, Type[] bTags)
    {
        var a = TypeId.Create(aCmps, aTags);
        var b = TypeId.Create(bCmps, bTags);

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(
        new[] { typeof(string), typeof(int) }, new Type[] { },
        new[] { typeof(int), typeof(char) }, new Type[] { }
    )]
    [InlineData(
        new[] { typeof(float) }, new Type[] { typeof(short), typeof(int) },
        new[] { typeof(float) }, new Type[] { typeof(int), typeof(string) }
    )]
    [InlineData(
        new[] { typeof(float), typeof(string) }, new Type[] { typeof(int) },
        new[] { typeof(float) }, new Type[] { typeof(int), typeof(string) }
    )]
    [InlineData(
        new[] { typeof(bool), typeof(short) }, new Type[] { typeof(string), typeof(int) },
        new[] { typeof(short), typeof(float) }, new Type[] { typeof(int), typeof(string) }
    )]
    public void Create_Diff(Type[] aCmps, Type[] aTags, Type[] bCmps, Type[] bTags)
    {
        var a = TypeId.Create(aCmps, aTags);
        var b = TypeId.Create(bCmps, bTags);

        Assert.NotEqual(a, b);
    }
}
