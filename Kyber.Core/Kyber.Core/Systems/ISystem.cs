namespace Kyber;

public interface IBaseSystem
{
    bool IsEnabled { get; set; }
}

public interface IInitializeSystem : IBaseSystem { void Initialize(); }

public interface IPreUpdateSystem : IBaseSystem { void PreUpdate(float dt); }

public interface IUpdateSystem : IBaseSystem { void Update(float dt); }

public interface IPostUpdateSystem : IBaseSystem { void PostUpdate(float dt); }

public interface IPreRenderSystem : IBaseSystem { void PreRender(float dt); }

public interface IRenderSystem : IBaseSystem { void Render(float dt); }

public interface IPostRenderSystem : IBaseSystem { void PostRender(float dt); }

public interface IDestroySystem : IBaseSystem { void Destroy(); }


public interface ISystem : IInitializeSystem, IPreUpdateSystem, IUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IDestroySystem {
	//void IInitializeSystem.Initialize() { }
	//void IPreUpdateSystem.PreUpdate(float dt) { }
	//void IUpdateSystem.Update(float dt) { }
	//void IPostUpdateSystem.PostUpdate(float dt) { }
	//void IPreRenderSystem.PreRender(float dt) { }
	//void IRenderSystem.Render(float dt) { }
	//void IPostRenderSystem.PostRender(float dt) { }
	//void IDestroySystem.Destroy() { }
}