namespace Kyber.ECS.Tests;

public class EntityTests
{
    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Create()
    {
        using var world = new World();

        var entity = world.CreateEntity();
        Assert.True(entity.IsAlive);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Tag()
    {
        using var world = new World();

        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();

        Assert.Equal(entity1.Archetype, entity2.Archetype);

        entity1.Tag<int>();

        Assert.True(entity1.Tagged<int>());
        Assert.False(entity2.Tagged<int>());
        Assert.NotEqual(entity1.Archetype, entity2.Archetype);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Untag()
    {
        using var world = new World();

        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();

        Assert.Equal(entity1.Archetype, entity2.Archetype);

        entity1.Tag<int>();

        Assert.NotEqual(entity1.Archetype, entity2.Archetype);

        entity1.Untag<int>();

        Assert.False(entity1.Tagged<int>());
        Assert.Equal(entity1.Archetype, entity2.Archetype);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Set()
    {
        using var world = new World();

        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();

        Assert.Equal(entity1.Archetype, entity2.Archetype);

        entity1.Set(6014);

        Assert.Equal(6014, entity1.Get<int>());
        Assert.False(entity2.Tagged<int>());
        Assert.NotEqual(entity1.Archetype, entity2.Archetype);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_SetRef()
    {
        using var world = new World();

        var entity = world.CreateEntity();

        entity.Set(0);

        entity.Get<int>() = 6014;

        Assert.Equal(6014, entity.Get<int>());
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Unset()
    {
        using var world = new World();

        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();

        Assert.Equal(entity1.Archetype, entity2.Archetype);

        entity1.Set(6014);

        Assert.NotEqual(entity1.Archetype, entity2.Archetype);

        entity1.Unset<int>();

        Assert.False(entity1.Has<int>());
        Assert.Equal(entity1.Archetype, entity2.Archetype);
    }

    [Fact, Trait(CATEGORY, UNIT)]
    public void CreateEntity_Destroy()
    {
        using var world = new World();

        var entity = world.CreateEntity();
        Assert.True(entity.IsAlive);

        entity.Destroy();

        Assert.False(entity.IsAlive);
    }
}
