using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Core;

public sealed class SystemGroup : IStartupSystem, IPreUpdateSystem, IUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IShutdownSystem
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HashSet<Type> _systemTypes;

    private readonly List<IStartupSystem> _startupSystems = new();
    private readonly List<IPreUpdateSystem> _preUpdateSystems = new();
    private readonly List<IUpdateSystem> _updateSystems = new();
    private readonly List<IPostUpdateSystem> _postUpdateSystems = new();
    private readonly List<IPreRenderSystem> _preRenderSystems = new();
    private readonly List<IRenderSystem> _renderSystems = new();
    private readonly List<IPostRenderSystem> _postRenderSystems = new();
    private readonly List<IShutdownSystem> _shutdownSystems = new();

    public bool IsEnabled { get; set; } = true;

    public SystemGroup(IServiceProvider serviceProvider, HashSet<Type> systemTypes)
    {
        _serviceProvider = serviceProvider;
        _systemTypes = systemTypes;
    }

    public void Startup()
    {
        _setupSystems();

        foreach (var system in _startupSystems) if (system.IsEnabled) system.Startup();
    }

    public void PreUpdate(float dt)
    {
        foreach(var system in _preUpdateSystems) if (system.IsEnabled) system.PreUpdate(dt);
    }

    public void Update(float dt)
    {
        foreach (var system in _updateSystems) if (system.IsEnabled) system.Update(dt);
    }

    public void PostUpdate(float dt)
    {
        foreach (var system in _postUpdateSystems) if (system.IsEnabled) system.PostUpdate(dt);
    }

    public void PreRender(float dt)
    {
        foreach (var system in _preRenderSystems) if (system.IsEnabled) system.PreRender(dt);
    }

    public void Render(float dt)
    {
        foreach (var system in _renderSystems) if (system.IsEnabled) system.Render(dt);
    }

    public void PostRender(float dt)
    {
        foreach (var system in _postRenderSystems) if (system.IsEnabled) system.PostRender(dt);
    }

    public void Shutdown()
    {
        foreach (var system in _shutdownSystems) if (system.IsEnabled) system.Shutdown();
    }

    private void _setupSystems()
    {
        foreach (var type in _systemTypes)
        {
            var instance = _serviceProvider.GetService(type);
            if (instance is null) continue;
            
            if (instance is IStartupSystem startupSystem) _startupSystems.Add(startupSystem);
            if (instance is IPreUpdateSystem preUpdateSystem) _preUpdateSystems.Add(preUpdateSystem);
            if (instance is IUpdateSystem updateSystem) _updateSystems.Add(updateSystem);
            if (instance is IPostUpdateSystem postUpdateSystem) _postUpdateSystems.Add(postUpdateSystem);
            if (instance is IPreRenderSystem preRenderSystem) _preRenderSystems.Add(preRenderSystem);
            if (instance is IRenderSystem renderSystem) _renderSystems.Add(renderSystem);
            if (instance is IPostRenderSystem postRenderSystem) _postRenderSystems.Add(postRenderSystem);
            if (instance is IShutdownSystem shutdownSystem) _shutdownSystems.Add(shutdownSystem);
        }
    }
}
