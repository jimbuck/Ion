using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Kyber.Scenes;

namespace Kyber.Hosting.Scenes;

internal class SceneBuilder : ISceneBuilder
{
    public string Name { get; }

	private readonly IGameLoopBuilder _init = new GameLoopBuilder();
	private readonly IGameLoopBuilder _first = new GameLoopBuilder();
	private readonly IGameLoopBuilder _fixedUpdate = new GameLoopBuilder();
	private readonly IGameLoopBuilder _update = new GameLoopBuilder();
	private readonly IGameLoopBuilder _render = new GameLoopBuilder();
	private readonly IGameLoopBuilder _last = new GameLoopBuilder();
	private readonly IGameLoopBuilder _destroy = new GameLoopBuilder();

	public SceneBuilder(string name, IServiceCollection services)
    {
        Name = name;
        _services = services;
    }

    public ISceneBuilder AddSystem<T>() where T : class
    {
        _systems.AddSystem<T>();
        _services.TryAddScoped<T>();
        return this;
    }

    internal Scene Build(IServiceProvider serviceProvider)
    {
        //var aggSys = _systems.Build(serviceProvider);

        return new Scene(Name, aggSys);
    }
}