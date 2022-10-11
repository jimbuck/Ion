namespace Kyber.Core.Hosting;

public interface IGameBuilder : ISceneBuilder {
    StartupConfig Config { get; }
    IGameBuilder AddScene<T>();
}
