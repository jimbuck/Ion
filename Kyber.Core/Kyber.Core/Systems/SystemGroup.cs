namespace Kyber;

public sealed class SystemGroup : ISystem
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HashSet<Type> _systems;

    private readonly List<IInitializeSystem> _initializeSystems = new();
	private readonly List<IFixedUpdateSystem> _fixedUpdateSystems = new();
	private readonly List<IUpdateSystem> _updateSystems = new();
    private readonly List<IRenderSystem> _renderSystems = new();
	private readonly List<IDestroySystem> _destroySystems = new();

    public bool IsEnabled { get; set; } = true;

    public SystemGroup(IServiceProvider serviceProvider, HashSet<Type> systemTypes)
    {
        _serviceProvider = serviceProvider;
        _systems = systemTypes;
    }

    public void Initialize()
    {
        _setupSystems();

        foreach (var system in _initializeSystems) if (system.IsEnabled) system.Initialize();
    }


	public void FixedUpdate(GameTime dt)
	{
		foreach (var system in _fixedUpdateSystems) if (system.IsEnabled) system.FixedUpdate(dt);
	}

	public void Update(GameTime dt)
    {
        foreach (var system in _updateSystems) if (system.IsEnabled) system.Update(dt);
    }

    public void Render(GameTime dt)
    {
        foreach (var system in _renderSystems) if (system.IsEnabled) system.Render(dt);
    }
	public void Destroy()
    {
        foreach (var system in _destroySystems) if (system.IsEnabled) system.Destroy();
    }

	public bool AddSystem<T>()
	{
		var type = typeof(T);
		if (_systems.Contains(type)) return false;

		var instance = _serviceProvider.GetService(type);
		var added = _addSystem(instance);

		if (added) _systems.Add(type);

		return added;
	}

	internal bool AddSystem<T>(T instance)
	{
		var type = typeof(T);
		if (_systems.Contains(type)) return false;

		var added = _addSystem(instance);

		if (added) _systems.Add(type);

		return added;
	}

	private bool _addSystem(object? system)
	{
		if (system is null) return false;

		var added = false;

		if (system is IInitializeSystem startupSystem)
		{
			_initializeSystems.Add(startupSystem);
			added = true;
		}

		if (system is IFixedUpdateSystem fixedUpdateSystem)
		{
			_fixedUpdateSystems.Add(fixedUpdateSystem);
			added = true;
		}

		if (system is IUpdateSystem updateSystem)
		{
			_updateSystems.Add(updateSystem);
			added = true;
		}

		if (system is IRenderSystem renderSystem)
		{
			_renderSystems.Add(renderSystem);
			added = true;
		}

		if (system is IDestroySystem shutdownSystem)
		{
			_destroySystems.Add(shutdownSystem);
			added = true;
		}

		return added;
	}

	public void RemoveSystem<T>()
	{
		_initializeSystems.RemoveAll(s => s is T);
		_fixedUpdateSystems.RemoveAll(s => s is T);
		_updateSystems.RemoveAll(s => s is T);
		_renderSystems.RemoveAll(s => s is T);
		_destroySystems.RemoveAll(s => s is T);

		_systems.Remove(typeof(T));
	}

    private void _setupSystems()
    {
        foreach (var type in _systems)
        {
			var instance = _serviceProvider.GetService(type);
			_addSystem(instance);
        }
    }
}
