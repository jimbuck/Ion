using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Core;

public class SystemCollection : ISystemCollection
{
    public Type[] StartupSystems { get; init; } = Array.Empty<Type>();
    public Type[] UpdateSystems { get; init; } = Array.Empty<Type>();
    public Type[] RenderSystems { get; init; } = Array.Empty<Type>();

    private readonly IServiceProvider _serviceProvider;

    public SystemCollection(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IStartupSystem[] GetStartupSystems()
    {
        return _getSystems<IStartupSystem>(StartupSystems);
    }

    public IUpdateSystem[] GetUpdateSystems()
    {
        return _getSystems<IUpdateSystem>(UpdateSystems);
    }

    public IRenderSystem[] GetRenderSystems()
    {
        return _getSystems<IRenderSystem>(RenderSystems);
    }

    private T[] _getSystems<T>(Type[] types)
    {
        var systems = new List<T>();
        foreach (var type in types)
        {
            var service = (T)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, type);
            if (service is not null) systems.Add(service);
        }
        return systems.ToArray();
    }
}