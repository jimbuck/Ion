using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Kyber.Core;

namespace Kyber;

public class KyberApplication : IKyberApplication, IDisposable
{
	private readonly IHost _host;

	private readonly IMiddlewarePipelineBuilder _init = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _first = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _fixedUpdate = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _update = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _render = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _last = new MiddlewarePipelineBuilder();
	private readonly IMiddlewarePipelineBuilder _destroy = new MiddlewarePipelineBuilder();

	/// <summary>
	/// The application's configured services.
	/// </summary>
	public IServiceProvider Services => _host.Services;

	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

	internal KyberApplication(IHost host)
	{
		_host = host;
	}

	public static KyberApplicationBuilder CreateBuilder(string[] args)
	{
		return new KyberApplicationBuilder(args);
	}

	public static KyberApplicationBuilder CreateBuilder()
	{
		return CreateBuilder(Array.Empty<string>());
	}

	public IKyberApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_init.Use(middleware);
		return this;
	}

	public IKyberApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_first.Use(middleware);
		return this;
	}

	public IKyberApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_fixedUpdate.Use(middleware);
		return this;
	}

	public IKyberApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_update.Use(middleware);
		return this;
	}

	public IKyberApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_render.Use(middleware);
		return this;
	}


	public IKyberApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_last.Use(middleware);
		return this;
	}

	public IKyberApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_destroy.Use(middleware);
		return this;
	}

	public GameLoop RunManually()
	{
		var gameLoop = ActivatorUtilities.CreateInstance<GameLoop>(Services);

		gameLoop.Init = _init.Build();
		gameLoop.First = _first.Build();
		gameLoop.FixedUpdate = _fixedUpdate.Build();
		gameLoop.Update = _update.Build();
		gameLoop.Render = _render.Build();
		gameLoop.Last = _last.Build();
		gameLoop.Destroy = _destroy.Build();

		return gameLoop;
	}

	public void Run()
	{
		var gameLoop = RunManually();

		gameLoop.Run();
	}

	public void Dispose()
	{
		_host.Dispose();
	}
}
