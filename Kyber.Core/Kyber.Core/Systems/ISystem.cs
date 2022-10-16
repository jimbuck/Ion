namespace Kyber.Core;

public interface ISystem
{
    bool IsEnabled { get; set; }
}

public interface IStartupSystem : ISystem { void Startup(); }

public interface IPreUpdateSystem : ISystem { void PreUpdate(float dt); }

public interface IUpdateSystem : ISystem { void Update(float dt); }

public interface IPostUpdateSystem : ISystem { void PostUpdate(float dt); }

public interface IPreRenderSystem : ISystem { void PreRender(float dt); }

public interface IRenderSystem : ISystem { void Render(float dt); }

public interface IPostRenderSystem : ISystem { void PostRender(float dt); }

public interface IShutdownSystem : ISystem { void Shutdown(); }