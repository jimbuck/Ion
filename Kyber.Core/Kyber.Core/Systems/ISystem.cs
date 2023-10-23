namespace Kyber;

public interface IBaseSystem
{
    bool IsEnabled { get; set; }
}

public interface IInitializeSystem : IBaseSystem { void Initialize(); }

public interface IFixedUpdateSystem : IBaseSystem { void FixedUpdate(GameTime time); }

public interface IUpdateSystem : IBaseSystem { void Update(GameTime time); }

public interface IRenderSystem : IBaseSystem { void Render(GameTime time); }

public interface IDestroySystem : IBaseSystem { void Destroy(); }


public interface ISystem : IInitializeSystem, IUpdateSystem, IFixedUpdateSystem, IRenderSystem, IDestroySystem {
	//void IInitializeSystem.Initialize() { }
	//void IUpdateSystem.Update(float dt) { }
	//void IRenderSystem.Render(float dt) { }
	//void IDestroySystem.Destroy() { }
}