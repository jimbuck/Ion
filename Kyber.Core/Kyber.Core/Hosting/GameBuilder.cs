using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kyber.Core.Hosting;

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
        _systems.AddSystem<T>();
        Services.TryAddScoped<T>();
        return this;
    }

    internal SystemGroup Build(IServiceProvider serviceProvider)
    {
        return _systems.Build(serviceProvider);
    }
}
