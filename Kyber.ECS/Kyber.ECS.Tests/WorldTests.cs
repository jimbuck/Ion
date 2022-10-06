using Xunit.Abstractions;

namespace Kyber.ECS.Tests;

struct Position { }
struct Rotation { }
struct Velocity { }

public class WorldTests
{
    private ITestOutputHelper _output;

    public WorldTests(ITestOutputHelper output)
    {
        _output = output;
    }

	[Fact]
	public void Create_DefaultName()
	{
		using var world = new World();

		Assert.NotNull(world);
		Assert.NotNull(world.Name);
	}

    [Fact]
    public void Create_SpecifiedName()
    {
        var expected = "TestWorld";
        using var world = new World(expected);

        Assert.Equal(expected, world.Name);
    }

    [Fact]
    public void Create_Entity()
    {
        using var world = new World();

        var entityId = world.CreateEntity();
        Assert.True(entityId.Id > 0);
    }

    [Fact]
    public void IsAlive_Entity()
    {
        using var world = new World();

        var entityId = world.CreateEntity();
        var isAliveAfterCreate = world.IsAlive(entityId);
        Assert.True(isAliveAfterCreate);
        world.DestroyEntity(entityId);
        var isAliveAfterDestroy = world.IsAlive(entityId);
        Assert.False(isAliveAfterDestroy);
    }

    [Fact]
    public void ArchetypeGraph_Iterator()
    {
        using var world = new World();
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        var entity4 = world.CreateEntity();

        world.Set(entity1, new Position());
        world.Set(entity1, new Rotation());
        world.Set(entity1, new Velocity());

        world.Set(entity2, new Position());
        world.Set(entity2, new Velocity());

        world.Set(entity3, new Position());
        world.Set(entity3, new Velocity());
        world.Set(entity3, new Rotation());

        world.Set(entity4, new Velocity());
        world.Set(entity4, new Position());

        _output.WriteLine(world.ToString());
        _output.WriteLine(world.ToGraphString());
        Assert.Equal(8, world.ArchetypeGraph().Count());
    }

    [Fact]
    public void ArchetypeGraph_Filter()
    {
        using var world = new World();
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        var entity4 = world.CreateEntity();

        var positionId = ComponentId.From<Position>();
        var rotationId = ComponentId.From<Rotation>();
        var velocityId = ComponentId.From<Velocity>();

        world.Set(entity1, new Position());
        world.Set(entity1, new Rotation());
        world.Set(entity1, new Velocity());

        world.Set(entity2, new Position());
        world.Set(entity2, new Velocity());

        world.Set(entity3, new Position());
        world.Set(entity3, new Velocity());
        world.Set(entity3, new Rotation());

        world.Set(entity4, new Velocity());
        world.Set(entity4, new Position());

        _output.WriteLine(world.ToString());
        _output.WriteLine(world.ToGraphString());
        Assert.Equal(3, world.Archetypes(new[] { velocityId }).Count());
        Assert.Equal(2, world.Archetypes(new[] { velocityId, positionId }).Count());
        Assert.Equal(2, world.Archetypes(new ComponentId[] { }, new[] { velocityId }).Count());
        Assert.Equal(1, world.Archetypes(new ComponentId[] { rotationId }, new[] { velocityId }).Count());
    }

    [Fact]
    public void Dispose()
    {
        var world = new World();

        Assert.NotNull(world);

		world.Dispose();
		world.Dispose();
    }
}