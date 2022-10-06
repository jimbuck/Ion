namespace Kyber.ECS.Tests;

public class QueryTests
{
    [Fact]
    public void SingleQuery_SingleComponentEntities()
    {
        using var world = new World();
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        var entity4 = world.CreateEntity();

        world.Set(entity1, 1);
        world.Set(entity2, "text");
        world.Set(entity4, 4);

        var query = world.CreateQuery().With<int>();

        var ints = new List<int>();
        query.ForEach((ref int val) =>
        {
            ints.Add(val);
        });

        Assert.Equal(2, ints.Count);
        Assert.Contains(ints, i => i == 1 || i == 4);
    }

    [Fact]
    public void DoubleQuery_DoubleComponentEntities()
    {
        using var world = new World();
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        var entity4 = world.CreateEntity();

        world.Set(entity1, 1);
        world.Set(entity1, "item 1");

        world.Set(entity2, "item 2");

        world.Set(entity3, 3);

        world.Set(entity4, 4);
        world.Set(entity4, "item 4");

        var query = world.CreateQuery().With<int>();

        var ints = new List<int>();
        query.ForEach((ref int num) =>
        {
            ints.Add(num);
        });

        Assert.Equal(3, ints.Count);
        Assert.Contains(ints, i => i == 1 || i == 4 || i == 3);
    }

    [Fact]
    public void DoubleQuery_DoubleComponentEntities_IncorrectQuery()
    {
        using var world = new World();
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        var entity4 = world.CreateEntity();

        world.Set(entity1, 1);
        world.Set(entity1, "item 1");

        world.Set(entity2, "item 2");

        world.Set(entity3, 3);

        world.Set(entity4, 4);
        world.Set(entity4, "item 4");

        var query = world.CreateQuery().With<int>();

        var ints = new List<int>();
        Assert.Throws<KeyNotFoundException>(() =>
        {
            query.ForEach((ref int num, ref string text) => { });
        });
    }
}
