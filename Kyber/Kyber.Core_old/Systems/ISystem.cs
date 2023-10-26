namespace Kyber;

public interface IBaseSystem
{
    bool IsEnabled { get; set; }
}

public interface IInitializeSystem : IBaseSystem { void Initialize(); }

public interface IFirstSystem : IBaseSystem { void First(GameTime time); }

public interface IFixedUpdateSystem : IBaseSystem { void FixedUpdate(GameTime time); }

public interface IUpdateSystem : IBaseSystem { void Update(GameTime time); }

public interface IRenderSystem : IBaseSystem { void Render(GameTime time); }

public interface ILastSystem : IBaseSystem { void Last(GameTime time); }

public interface IDestroySystem : IBaseSystem { void Destroy(); }


public interface ISystem : IInitializeSystem, IFirstSystem, IUpdateSystem, IFixedUpdateSystem, IRenderSystem, ILastSystem, IDestroySystem {
	//void IInitializeSystem.Initialize() { }
	//void IFirstSystem.First(float dt) { }
	//void IUpdateSystem.Update(float dt) { }
	//void IRenderSystem.Render(float dt) { }
	//void ILastSystem.Last(float dt) { }
	//void IDestroySystem.Destroy() { }
}