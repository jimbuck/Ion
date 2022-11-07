namespace Kyber;

public sealed class SystemGroup : ISystem, IFirstSystem, ILastSystem
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HashSet<Type> _systems;

    private readonly List<IInitializeSystem> _initializeSystems = new();
	private readonly List<IFirstSystem> _firstSystems = new();
	private readonly List<IPreUpdateSystem> _preUpdateSystems = new();
	private readonly List<IFixedUpdateSystem> _fixedUpdateSystems = new();
	private readonly List<IUpdateSystem> _updateSystems = new();
    private readonly List<IPostUpdateSystem> _postUpdateSystems = new();
    private readonly List<IPreRenderSystem> _preRenderSystems = new();
    private readonly List<IRenderSystem> _renderSystems = new();
    private readonly List<IPostRenderSystem> _postRenderSystems = new();
	private readonly List<ILastSystem> _lastSystems = new();
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

	public void First(GameTime dt)
	{
		foreach (var system in _firstSystems) if (system.IsEnabled) system.First(dt);
	}

	public void PreUpdate(GameTime dt)
    {
        foreach(var system in _preUpdateSystems) if (system.IsEnabled) system.PreUpdate(dt);
    }

	public void FixedUpdate(GameTime dt)
	{
		foreach (var system in _fixedUpdateSystems) if (system.IsEnabled) system.FixedUpdate(dt);
	}

	public void Update(GameTime dt)
    {
        foreach (var system in _updateSystems) if (system.IsEnabled) system.Update(dt);
    }

    public void PostUpdate(GameTime dt)
    {
        foreach (var system in _postUpdateSystems) if (system.IsEnabled) system.PostUpdate(dt);
    }

    public void PreRender(GameTime dt)
    {
        foreach (var system in _preRenderSystems) if (system.IsEnabled) system.PreRender(dt);
    }

    public void Render(GameTime dt)
    {
        foreach (var system in _renderSystems) if (system.IsEnabled) system.Render(dt);
    }

    public void PostRender(GameTime dt)
    {
        foreach (var system in _postRenderSystems) if (system.IsEnabled) system.PostRender(dt);
    }

	public void Last(GameTime dt)
	{
		foreach (var system in _lastSystems) if (system.IsEnabled) system.Last(dt);
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

		if (system is IFirstSystem firstSystem)
		{
			_firstSystems.Add(firstSystem);
			added = true;
		}

		if (system is IPreUpdateSystem preUpdateSystem)
		{
			_preUpdateSystems.Add(preUpdateSystem);
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

		if (system is IPostUpdateSystem postUpdateSystem)
		{
			_postUpdateSystems.Add(postUpdateSystem);
			added = true;
		}

		if (system is IPreRenderSystem preRenderSystem)
		{
			_preRenderSystems.Add(preRenderSystem);
			added = true;
		}

		if (system is IRenderSystem renderSystem)
		{
			_renderSystems.Add(renderSystem);
			added = true;
		}

		if (system is IPostRenderSystem postRenderSystem)
		{
			_postRenderSystems.Add(postRenderSystem);
			added = true;
		}

		if (system is ILastSystem lastSystem)
		{
			_lastSystems.Add(lastSystem);
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
		_preUpdateSystems.RemoveAll(s => s is T);
		_fixedUpdateSystems.RemoveAll(s => s is T);
		_updateSystems.RemoveAll(s => s is T);
		_postUpdateSystems.RemoveAll(s => s is T);
		_preRenderSystems.RemoveAll(s => s is T);
		_renderSystems.RemoveAll(s => s is T);
		_postRenderSystems.RemoveAll(s => s is T);
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
