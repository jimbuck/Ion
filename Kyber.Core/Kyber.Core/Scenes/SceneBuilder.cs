using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kyber.Core.Scenes.Hosting;

public interface ISceneBuilder
{
    ISceneBuilder AddSystem<T>() where T : class;
}

public class SceneBuilder : ISceneBuilder
{
    public string Name { get; }
    protected readonly IServiceCollection _services;
    protected readonly SystemGroupBuilder _systems = new();

    public SceneBuilder(string name, IServiceCollection services)
    {
        Name = name;
        _services = services;
    }

    public ISceneBuilder AddSystem<T>() where T : class
    {
        _systems.AddSystem<T>();
        _services.TryAddScoped<T>();
        return this;
    }

    internal Scene Build(IServiceProvider serviceProvider)
    {
        var aggSys = _systems.Build(serviceProvider);

        return new Scene(Name, aggSys);
    }
}