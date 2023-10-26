namespace Kyber.Scenes;

public sealed class Scene : IInitializeSystem,  IUpdateSystem, IFixedUpdateSystem, IRenderSystem, IDestroySystem
{
    public bool IsEnabled { get; set; } = true;
    public string Name { get; }
    private readonly SystemGroup _systems;

    internal Scene(string name, SystemGroup systems)
    {
        Name = name;
        _systems = systems;
    }

    public void Initialize() => _systems.Initialize();

	public void FixedUpdate(GameTime dt) => _systems.FixedUpdate(dt);
    public void Update(GameTime dt) => _systems.Update(dt);

    public void Render(GameTime dt) => _systems.Render(dt);

    public void Destroy() => _systems.Destroy();    
}
