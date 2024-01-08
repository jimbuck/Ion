namespace Ion;

internal class SystemGroupBuilder
{
    private readonly HashSet<Type> _systemTypes = new();
    private readonly Type[] _validSystemTypes = new[] { typeof(IInitializeSystem), typeof(IFirstSystem), typeof(IFixedUpdateSystem), typeof(IUpdateSystem), typeof(IRenderSystem), typeof(ILastSystem), typeof(IDestroySystem) };

    public SystemGroupBuilder AddSystem<T>() where T : class
    {
        var systemType = typeof(T);

        if (!_isValidSystem(systemType)) throw new Exception($"Invalid system provided ({systemType.FullName})!");

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