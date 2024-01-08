using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Scenes;

internal class SceneBuilder : ISceneBuilder
{
	private readonly IMiddlewarePipelineBuilder _init = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _first = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _fixedUpdate = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _update = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _render = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _last = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _destroy = new MiddlewarePipelineBuilder();

	public int SceneId { get; }

	public IConfiguration Configuration { get; }

	public IServiceProvider Services { get; }

	public SceneBuilder(int sceneId, IConfiguration config, IServiceProvider services)
    {
		SceneId = sceneId;
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
		return new Scene(SceneId)
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