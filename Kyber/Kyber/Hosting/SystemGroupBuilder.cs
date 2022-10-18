namespace Kyber.Hosting;

internal class SystemGroupBuilder
{
    private readonly HashSet<Type> _systemTypes = new();
    private readonly Type[] _validSystemTypes = new[] { typeof(IStartupSystem), typeof(IShutdownSystem), typeof(IUpdateSystem), typeof(IRenderSystem), typeof(IPreUpdateSystem), typeof(IPostUpdateSystem), typeof(IPreRenderSystem), typeof(IPostRenderSystem) };

    public SystemGroupBuilder AddSystem<T>() where T : class
    {
        return AddSystem(typeof(T));        
    }
    
    public SystemGroupBuilder AddSystem(Type systemType)
    {
        if (!_isValidSystem(systemType))
        {
            throw new Exception($"Invalid system provided ({systemType.FullName})!");
        }

        _systemTypes.Add(systemType);

        return this;
    }

    private bool _isValidSystem(Type systemType)
    {
        foreach(var validSystemType in _validSystemTypes)
        {
            if (validSystemType.IsAssignableFrom(systemType)) return true;
        }

        return false;
    }

    public SystemGroup Build(IServiceProvider serviceProvider)
    {
        return new SystemGroup(serviceProvider, _systemTypes);
    }
}