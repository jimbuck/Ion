namespace Kyber;

public interface IBaseSystem
{
    bool IsEnabled { get; set; }
}

public interface IStartupSystem : IBaseSystem { void Startup(); }

public interface IPreUpdateSystem : IBaseSystem { void PreUpdate(float dt); }

public interface IUpdateSystem : IBaseSystem { void Update(float dt); }

public interface IPostUpdateSystem : IBaseSystem { void PostUpdate(float dt); }

public interface IPreRenderSystem : IBaseSystem { void PreRender(float dt); }

public interface IRenderSystem : IBaseSystem { void Render(float dt); }

public interface IPostRenderSystem : IBaseSystem { void PostRender(float dt); }

public interface IShutdownSystem : IBaseSystem { void Shutdown(); }


public interface ISystem : IStartupSystem, IPreUpdateSystem, IUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IShutdownSystem {
    //void IStartupSystem.Startup() { }
    //void IPreUpdateSystem.PreUpdate(float dt) { }
    //void IUpdateSystem.Update(float dt) { }
    //void IPostUpdateSystem.PostUpdate(float dt) { }
    //void IPreRenderSystem.PreRender(float dt) { }
    //void IRenderSystem.Render(float dt) { }
    //void IPostRenderSystem.PostRender(float dt) { }
    //void IShutdownSystem.Shutdown() { }
}