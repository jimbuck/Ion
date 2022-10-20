﻿namespace Kyber.Scenes;

public sealed class Scene : IInitializeSystem, IPreUpdateSystem, IUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IDestroySystem
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

    public void PreUpdate(float dt) => _systems.PreUpdate(dt);
    public void Update(float dt) => _systems.Update(dt);
    public void PostUpdate(float dt) => _systems.PostUpdate(dt);

    public void PreRender(float dt) => _systems.PreRender(dt);
    public void Render(float dt) => _systems.Render(dt);
    public void PostRender(float dt) => _systems.PostRender(dt);

    public void Destroy() => _systems.Destroy();    
}
