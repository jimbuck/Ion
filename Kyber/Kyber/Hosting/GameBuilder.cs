using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kyber.Hosting;

public interface IGameBuilder
{
	IGameConfig Config { get; }
    IServiceCollection Services { get; }
    IGameBuilder AddSystem<T>() where T : class;
    IGameBuilder AddSystem(Type type);
}

public class GameBuilder : IGameBuilder
{
    public IGameConfig Config { get; }
    public IServiceCollection Services { get; }
    private readonly SystemGroupBuilder _systems = new();

    public GameBuilder(IServiceCollection services)
    {
        Services = services;
        Config = new GameConfig();
    }

	public IGameBuilder AddSystem<T>() where T : class
	{
		_systems.AddSystem(typeof(T));
		Services.TryAddScoped<T>();
		return this;
	}

	public IGameBuilder AddSystem(Type type)
	{
		_systems.AddSystem(type);
		Services.TryAddScoped(type);
		return this;
	}

	internal IGameBuilder DirectAddSystem<T>() where T : class
	{
		_systems.AddSystem(typeof(T));
		return this;
	}

    internal IGameBuilder DirectAddSystem(Type type)
    {
        _systems.AddSystem(type);
        return this;
    }

    internal SystemGroup Build(IServiceProvider serviceProvider)
    {
        return _systems.Build(serviceProvider);
    }
}
