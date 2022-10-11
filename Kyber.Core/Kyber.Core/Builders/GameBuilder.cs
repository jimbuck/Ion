using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kyber.Core.Hosting;

internal class GameBuilder : IGameBuilder {

    private readonly HashSet<Type> _systems = new();
    private readonly HashSet<Type> _startupSystems = new();
    private readonly HashSet<Type> _updateSystems = new();
    private readonly HashSet<Type> _renderSystems = new();

    public StartupConfig Config { get; private set; } = new();

    public IGameBuilder AddSystem<T>()
    {
        var type = typeof(T);

        if (typeof(IStartupSystem).IsAssignableFrom(type))
        {
            _systems.Add(type);
            _startupSystems.Add(type);
        }

        if (typeof(IUpdateSystem).IsAssignableFrom(type))
        {
            _systems.Add(type);
            _updateSystems.Add(type);
        }

        if (typeof(IRenderSystem).IsAssignableFrom(type))
        {
            _systems.Add(type);
            _renderSystems.Add(type);
        }

        return this;
    }

    public IGameBuilder AddScene<T>()
    {
        return this;
    }

    internal void Configure(HostBuilderContext hostContext, IServiceCollection services)
    {
        services.AddSingleton(serviceProvider => new SystemCollection(serviceProvider)
        {
            StartupSystems = _startupSystems.ToArray(),
            UpdateSystems = _updateSystems.ToArray(),
            RenderSystems = _renderSystems.ToArray()
        });
    }
}
