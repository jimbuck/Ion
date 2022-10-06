using System.ComponentModel;

namespace Kyber.ECS;

public class Query
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

    public delegate void C1Query<C1>(ref C1 c1);

    public void ForEach<C>(in C1Query<C> action)
    {
        var componentId = ComponentId.From<C>();

        foreach(var archetype in _world.ArchetypesByComponentId[componentId])
        {
            if (archetype.Components[componentId].TryGet<C>(out var c1))
            {
                foreach (var i in archetype.RowIndex.Values)
                {
                    action(ref c1[i]);
                }
            } 
        }
    }

    public delegate void C2Query<C1, C2>(ref C1 c1, ref C2 c2);

    public void ForEach<C1, C2>(in C2Query<C1, C2> action)
    {
        ComponentId componentId1 = ComponentId.From<C1>(), componentId2 = ComponentId.From<C2>();

        foreach (var archetype in _world.Archetypes(_with, _without))
        {
            if (archetype.Components[componentId1].TryGet<C1>(out var c1) && archetype.Components[componentId2].TryGet<C2>(out var c2))
            {
                foreach (var i in archetype.RowIndex.Values)
                {
                    action(ref c1[i], ref c2[i]);
                }
            }
        }
    }
}
