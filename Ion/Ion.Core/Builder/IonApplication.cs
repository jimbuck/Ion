using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Ion.Core;

namespace Ion;

public class IonApplication : IIonApplication, IDisposable
{
	private readonly IHost _host;

	private readonly MiddlewarePipelineBuilder _init = new();
	private readonly MiddlewarePipelineBuilder _first = new();
	private readonly MiddlewarePipelineBuilder _fixedUpdate = new();
	private readonly MiddlewarePipelineBuilder _update = new();
	private readonly MiddlewarePipelineBuilder _render = new();
	private readonly MiddlewarePipelineBuilder _last = new();
	private readonly MiddlewarePipelineBuilder _destroy = new();

	/// <summary>
	/// The application's configured services.
	/// </summary>
	public IServiceProvider Services => _host.Services;

	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

	internal IonApplication(IHost host)
	{
		_host = host;
	}

	public static IonApplicationBuilder CreateBuilder(string[] args)
	{
		return new IonApplicationBuilder(args);
	}

	public static IonApplicationBuilder CreateBuilder()
	{
		return CreateBuilder([]);
	}

	public IIonApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_init.Use(middleware);
		return this;
	}

	public IIonApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_first.Use(middleware);
		return this;
	}

	public IIonApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_fixedUpdate.Use(middleware);
		return this;
	}

	public IIonApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_update.Use(middleware);
		return this;
	}

	public IIonApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_render.Use(middleware);
		return this;
	}


	public IIonApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_last.Use(middleware);
		return this;
	}

	public IIonApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_destroy.Use(middleware);
		return this;
	}

	public GameLoop Build()
	{
		var gameLoop = ActivatorUtilities.CreateInstance<GameLoop>(Services);

		gameLoop.InitBuilder = _init;
		gameLoop.FirstBuilder = _first;
		gameLoop.FixedUpdateBuilder = _fixedUpdate;
		gameLoop.UpdateBuilder = _update;
		gameLoop.RenderBuilder = _render;
		gameLoop.LastBuilder = _last;
		gameLoop.DestroyBuilder = _destroy;

		gameLoop.Build();

		return gameLoop;
	}

	public void Run()
	{
		var gameLoop = Build();

#if DEBUG
		HotReloadService.ActiveApplication = this;
		HotReloadService.ActiveGameLoop = gameLoop;
#endif
		gameLoop.Run();
	}

	public void Dispose()
	{
		_host.Dispose();
	}
}
