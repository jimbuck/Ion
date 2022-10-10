namespace Kyber.ECS;

public partial class Query
{
    private readonly World _world;
    private readonly HashSet<ComponentId> _with = new();
    private readonly HashSet<ComponentId> _without = new();

    internal Query(World world)
    {
        _world = world;
    }

    public Query With<T>()
    {
        _with.Add(ComponentId.From<T>());

        return this;
    }

    public Query Without<T>()
    {
        _without.Add(ComponentId.From<T>());

        return this;
    }
}
