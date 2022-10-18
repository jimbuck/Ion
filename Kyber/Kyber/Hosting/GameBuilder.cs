using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kyber.Hosting;

public interface IGameBuilder
{
    StartupConfig Config { get; }
    IServiceCollection Services { get; }
    IGameBuilder AddSystem<T>() where T : class;
    IGameBuilder AddSystem(Type type);
}

public class GameBuilder : IGameBuilder
{
    public StartupConfig Config { get; }
    public IServiceCollection Services { get; }
    private readonly SystemGroupBuilder _systems = new();

    public GameBuilder(IServiceCollection services)
    {
        Services = services;
        Config = new();
    }

    public IGameBuilder AddSystem<T>() where T : class
    {
        return AddSystem(typeof(T));
    }

    public IGameBuilder AddSystem(Type type)
    {
        _systems.AddSystem(type);
        Services.TryAddScoped(type);
        return this;
    }

    internal SystemGroup Build(IServiceProvider serviceProvider)
    {
        return _systems.Build(serviceProvider);
    }
}
