using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Core.Hosting;

public interface IGameBuilder {
    StartupConfig Config { get; }
    IServiceCollection Services { get; }
    IGameBuilder AddSystem<T>() where T : class;
}
