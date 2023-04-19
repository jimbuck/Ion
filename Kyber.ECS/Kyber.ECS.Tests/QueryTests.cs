namespace Kyber.ECS.Tests;

public class QueryTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void SingleQuery_SingleComponentEntities()
    {
        using var world = new World();
        var entity1 = world.CreateEntityId();
        var entity2 = world.CreateEntityId();
        var entity3 = world.CreateEntityId();
        var entity4 = world.CreateEntityId();

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

    [Fact, Trait(CATEGORY, UNIT)]
    public void DoubleQuery_DoubleComponentEntities()
    {
        using var world = new World();
        var entity1 = world.CreateEntityId();
        var entity2 = world.CreateEntityId();
        var entity3 = world.CreateEntityId();
        var entity4 = world.CreateEntityId();

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

    [Fact, Trait(CATEGORY, UNIT)]
    public void DoubleQuery_DoubleComponentEntities_IncorrectQuery()
    {
        using var world = new World();
        var entity1 = world.CreateEntityId();
        var entity2 = world.CreateEntityId();
        var entity3 = world.CreateEntityId();
        var entity4 = world.CreateEntityId();

        world.Set(entity1, 1);
        world.Set(entity1, "item 1");

        world.Set(entity2, "item 2");

        world.Set(entity3, 3);

        world.Set(entity4, 4);
        world.Set(entity4, "item 4");

        var query = world.CreateQuery().With<int>();
        
        Assert.Throws<KeyNotFoundException>(() =>
        {
            query.ForEach((ref int num, ref string text) => { });
        });
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void ManyComponents()
    {
        using var world = new World();
        var entity1 = world.CreateEntityId();
        var entity2 = world.CreateEntityId();
        var entity3 = world.CreateEntityId();
        var entity4 = world.CreateEntityId();

        world.Set(entity1, 1);
        world.Set(entity1, "item 1");
        world.Set(entity1, new Position());

        world.Set(entity2, 2);
        world.Set(entity2, "item 2");
        world.Set(entity2, new Position());
        world.Set(entity2, new Rotation());

        world.Set(entity3, 3);
        world.Set(entity3, "item 3");
        world.Set(entity3, new Rotation());

        world.Set(entity4, 4);
        world.Set(entity4, "item 4");
        world.Set(entity4, new Position());
        world.Set(entity4, new Rotation());
        world.Set(entity4, new Velocity());

        var query = world.CreateQuery().With<int>().With<Position>();

        var count = 0;
        query.ForEach((ref int num, ref string name, ref Position pos) =>
        {
            if (!string.IsNullOrEmpty(name)) count++;
        });

        Assert.Equal(3, count);
    }
}
