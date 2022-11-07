namespace Kyber.Scenes;

public sealed class Scene : IInitializeSystem, IPreUpdateSystem, IUpdateSystem, IFixedUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IDestroySystem
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

    public void PreUpdate(GameTime dt) => _systems.PreUpdate(dt);
	public void FixedUpdate(GameTime dt) => _systems.FixedUpdate(dt);
    public void Update(GameTime dt) => _systems.Update(dt);
    public void PostUpdate(GameTime dt) => _systems.PostUpdate(dt);

    public void PreRender(GameTime dt) => _systems.PreRender(dt);
    public void Render(GameTime dt) => _systems.Render(dt);
    public void PostRender(GameTime dt) => _systems.PostRender(dt);

    public void Destroy() => _systems.Destroy();    
}
