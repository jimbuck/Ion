﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Kyber.Scenes;
using Microsoft.Extensions.Configuration;

namespace Kyber;

internal delegate Scene SceneBuilderFactory(IConfiguration config, IServiceProvider services);

internal sealed class SceneManager : ISceneManager, IDisposable
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger _logger;
	private readonly IConfiguration _config;
	private readonly Dictionary<string, SceneBuilderFactory> _scenesBuilders = new();

	private Scene? _activeScene;
	private IServiceScope? _activeScope;
	private string? _nextScene = null;

	public Guid Id { get; } = Guid.NewGuid();

	public string CurrentScene => _activeScene?.Name ?? "<Root>";

	/// <summary>
	/// Creates a new SceneManager instance, keeping a reference to the service provider.
	/// </summary>
	/// <param name="serviceProvider">The root service provider.</param>
	public SceneManager(IServiceProvider serviceProvider, ILogger<SceneManager> logger, IConfiguration config)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_config = config;
	}

	public void Register(string name, SceneBuilderFactory sceneBuilderFactory)
	{
		_scenesBuilders[name] = sceneBuilderFactory;
	}

	/// <summary>
	/// Unloads the current scene and loads a new scene immediately.
	/// </summary>
	/// <param name="name">The scene name to load.</param>
	/// <exception cref="Exception"></exception>
	public void LoadScene(string name)
	{
		// TODO: Use events rather than this method call.
		if (!_scenesBuilders.ContainsKey(name)) throw new Exception($"Unknown scene '{name}'!");

		_nextScene = name;
	}

	private void _loadNextScene(GameTime dt)
	{
		if (_nextScene == null || _nextScene == CurrentScene)
		{
			_nextScene = null;
			return;
		}

		if (_activeScene != null)
		{
			_logger.LogInformation("Unloading {0} Scene.", CurrentScene);
			_activeScene?.Destroy(dt);
			_activeScope?.Dispose();
			_logger.LogInformation("Unloaded {0} Scene.", CurrentScene);
			_activeScene = null;
		}

		_logger.LogInformation("Loading {0} Scene.", _nextScene);
		_activeScope = _serviceProvider.CreateScope();
		var currScene = (CurrentScene)_activeScope.ServiceProvider.GetRequiredService<ICurrentScene>();
		currScene.Set(_nextScene);

		_activeScene = _scenesBuilders[_nextScene](_config, _activeScope.ServiceProvider);
		_activeScene.Init(dt);
		_logger.LogInformation("Loaded {0} Scene.", _nextScene);
		_nextScene = null;
	}

	/// <summary>
	/// Unloads the current scene and loads a new scene immediately.
	/// </summary>
	/// <typeparam name="TScene">The scene type to load.</typeparam>
	public void LoadScene<TScene>() where TScene : Scene
	{
		LoadScene(typeof(TScene).Name);
	}

	/// <summary>
	/// Initializes the active scene.
	/// </summary>
	[Init]
	public void Init(GameTime dt, GameLoopDelegate _)
	{
		_logger.LogDebug("Init ({0}) {1}", CurrentScene, dt);

		if (_activeScene != null)
		{
			_activeScene.Init(dt);
		}
		else
		{
			_nextScene = _scenesBuilders.First().Key;
			_loadNextScene(dt);
		}
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate _)
	{
		//_logger.LogDebug("First ({0}) {1}", CurrentScene, dt);
		_loadNextScene(dt);
		_activeScene?.First(dt);
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate _)
	{
		//_logger.LogDebug("FixedUpdate ({0}) {1}", CurrentScene, dt);
		_activeScene?.FixedUpdate(dt);
	}

	/// <summary>
	/// Updates the active scene (and transition, if in progress).
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Update.</param>
	[Update]
	public void Update(GameTime dt, GameLoopDelegate _)
	{
		//_logger.LogDebug("Update ({0}) {1}", CurrentScene, dt);
		_activeScene?.Update(dt);
	}

	/// <summary>
	/// Draws the active scene (and transition, if in progress).
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Draw.</param>
	[Render]
	public void Render(GameTime dt, GameLoopDelegate _)
	{
		//_logger.LogDebug("Render ({0}) {1}", CurrentScene, dt);
		_activeScene?.Render(dt);
	}

	[Last]
	public void Last(GameTime dt, GameLoopDelegate _)
	{
		//_logger.LogDebug("Last ({0}) {1}", CurrentScene, dt);
		_activeScene?.Last(dt);
	}

	/// <summary>
	/// Unloads content for the active scene.
	/// </summary>
	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate _)
	{
		_logger.LogDebug("Destroy");
		_activeScene?.Destroy(dt);
	}

	public void Dispose()
	{
		_activeScene = null;
		_activeScope?.Dispose();
		_activeScope = null;
	}
}
