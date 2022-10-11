namespace Kyber.Core.Hosting;

public interface ISceneBuilder {
    IGameBuilder AddSystem<T>();
}
