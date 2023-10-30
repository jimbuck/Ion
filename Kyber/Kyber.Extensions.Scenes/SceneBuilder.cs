using Microsoft.Extensions.Configuration;

namespace Kyber.Extensions.Scenes;

internal class SceneBuilder : ISceneBuilder
{
	private readonly IMiddlewarePipelineBuilder _init = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _first = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _fixedUpdate = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _update = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _render = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _last = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _destroy = new MiddlewarePipelineBuilder();

	public string Name { get; }

	public IConfiguration Configuration { get; }

	public IServiceProvider Services { get; }

	public SceneBuilder(string name, IConfiguration config, IServiceProvider services)
    {
        Name = name;
		Configuration = config;
		Services = services;
    }

	public ISceneBuilder UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_init.Use(middleware);
		return this;
	}

	public ISceneBuilder UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_first.Use(middleware);
		return this;
	}

	public ISceneBuilder UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_fixedUpdate.Use(middleware);
		return this;
	}

	public ISceneBuilder UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_update.Use(middleware);
		return this;
	}

	public ISceneBuilder UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_render.Use(middleware);
		return this;
	}


	public ISceneBuilder UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_last.Use(middleware);
		return this;
	}

	public ISceneBuilder UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_destroy.Use(middleware);
		return this;
	}

	internal Scene Build()
    {
		return new Scene(Name)
		{
			Init = _init.Build(),
			First = _first.Build(),
			FixedUpdate = _fixedUpdate.Build(),
			Update = _update.Build(),
			Render = _render.Build(),
			Last = _last.Build(),
			Destroy = _destroy.Build()
		};
	}
}