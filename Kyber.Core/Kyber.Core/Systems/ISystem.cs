namespace Kyber;

public interface IBaseSystem
{
    bool IsEnabled { get; set; }
}

public interface IInitializeSystem : IBaseSystem { void Initialize(); }

public interface IFirstSystem : IBaseSystem { void First(GameTime time); }

public interface IPreUpdateSystem : IBaseSystem { void PreUpdate(GameTime time); }

public interface IFixedUpdateSystem : IBaseSystem { void FixedUpdate(GameTime time); }

public interface IUpdateSystem : IBaseSystem { void Update(GameTime time); }

public interface IPostUpdateSystem : IBaseSystem { void PostUpdate(GameTime time); }

public interface IPreRenderSystem : IBaseSystem { void PreRender(GameTime time); }

public interface IRenderSystem : IBaseSystem { void Render(GameTime time); }

public interface IPostRenderSystem : IBaseSystem { void PostRender(GameTime time); }

public interface ILastSystem : IBaseSystem { void Last(GameTime time); }

public interface IDestroySystem : IBaseSystem { void Destroy(); }


public interface ISystem : IInitializeSystem, IPreUpdateSystem, IUpdateSystem, IFixedUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IDestroySystem {
	//void IInitializeSystem.Initialize() { }
	//void IPreUpdateSystem.PreUpdate(float dt) { }
	//void IUpdateSystem.Update(float dt) { }
	//void IPostUpdateSystem.PostUpdate(float dt) { }
	//void IPreRenderSystem.PreRender(float dt) { }
	//void IRenderSystem.Render(float dt) { }
	//void IPostRenderSystem.PostRender(float dt) { }
	//void IDestroySystem.Destroy() { }
}