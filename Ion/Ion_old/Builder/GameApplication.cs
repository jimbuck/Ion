using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ion.Builder;

public class GameApplication : IDisposable
{
	private readonly IHost _host;

	private readonly IGameLoopBuilder _init = new GameLoopBuilder();
	private readonly IGameLoopBuilder _first = new GameLoopBuilder();
	private readonly IGameLoopBuilder _fixedUpdate = new GameLoopBuilder();
	private readonly IGameLoopBuilder _update = new GameLoopBuilder();
	private readonly IGameLoopBuilder _render = new GameLoopBuilder();
	private readonly IGameLoopBuilder _last = new GameLoopBuilder();
	private readonly IGameLoopBuilder _destroy = new GameLoopBuilder();

	/// <summary>
	/// The application's configured services.
	/// </summary>
	public IServiceProvider Services => _host.Services;

	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

	/// <summary>
	/// Allows consumers to be notified of application lifetime events.
	/// </summary>
	public IHostApplicationLifetime Lifetime => _host.Services.GetRequiredService<IHostApplicationLifetime>();

	internal GameApplication(IHost host)
	{
		_host = host;
	}

	public static GameApplicationBuilder CreateBuilder(string[] args)
	{
		return new GameApplicationBuilder(args);
	}

	public GameApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_init.Use(middleware);
		return this;
	}

	public GameApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_first.Use(middleware);
		return this;
	}

	public GameApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_fixedUpdate.Use(middleware);
		return this;
	}

	public GameApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_update.Use(middleware);
		return this;
	}

	public GameApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_update.Use(middleware);
		return this;
	}

	public GameApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_last.Use(middleware);
		return this;
	}

	public GameApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_destroy.Use(middleware);
		return this;
	}

	public void Run()
	{
		var gameLoop = _build();

		gameLoop.Run();
	}

	public void Dispose()
	{
		_host.Dispose();
	}

	private GameLoop _build()
	{
		var gameConfig = Services.GetRequiredService<IGameConfig>();
		var gameLoop = new GameLoop(gameConfig)
		{
			Initialize = _init.Build(),
			First = _first.Build(),
			FixedUpdate = _fixedUpdate.Build(),
			Update = _update.Build(),
			Render = _render.Build(),
			Last = _last.Build(),
			Destroy = _destroy.Build()
		};

		return gameLoop;
	}
}
