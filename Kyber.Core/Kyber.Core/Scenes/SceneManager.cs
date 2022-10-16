﻿using Microsoft.Extensions.DependencyInjection;
using Kyber.Core.Scenes.Transitions;
using Kyber.Core.Scenes.Hosting;
using Kyber.Core.Scenes;

namespace Kyber.Core;

public sealed class SceneManager : IStartupSystem, IPreUpdateSystem, IUpdateSystem, IPostUpdateSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IShutdownSystem, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly Dictionary<string, SceneBuilder> _scenesBuilders;

    private Scene? _activeScene;
    private Transition? _activeTransition;
    private IServiceScope? _activeScope;
    private string? _nextScene = null;

    public Guid Id { get; } = Guid.NewGuid();

    public bool IsEnabled { get; set; } = true;

    public string[] Scenes { get; private set; }

    public string CurrentScene => _activeScene?.Name ?? "<Root>";

    /// <summary>
    /// Creates a new SceneManager instance, keeping a reference to the service provider.
    /// </summary>
    /// <param name="serviceProvider">The root service provider.</param>
    public SceneManager(IServiceProvider serviceProvider, ILogger<SceneManager> logger, IEnumerable<SceneBuilder> sceneBuilders)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var sb = sceneBuilders.ToArray();
        Scenes = sb.Select(s => s.Name).ToArray();
        _scenesBuilders = sb.ToDictionary(s => s.Name, s => s);
    }

    /// <summary>
    /// Begins initializes and begins the transition which will then load the new scene.
    /// </summary>
    /// <typeparam name="TScene">The scene type to load.</typeparam>
    /// <typeparam name="TTransition">The transition effect.</typeparam>
    /// <param name="duration">The duration of the transition.</param>
    public void LoadScene<TScene, TTransition>(float duration) where TScene : ISceneConfiguration where TTransition : Transition
    {
        if (_activeTransition != null) return;

        var transitionType = typeof(TTransition);

        _activeTransition = (Transition)ActivatorUtilities.CreateInstance(_serviceProvider, transitionType);
        _activeTransition.Duration = duration;
        _activeTransition.StateChanged += (sender, args) => LoadScene<TScene>();
        _activeTransition.Completed += (sender, args) =>
        {
            _activeTransition.Dispose();
            _activeTransition = null;
        };
    }

    /// <summary>
    /// Unloads the current scene and loads a new scene immediately.
    /// </summary>
    /// <param name="name">The scene name to load.</param>
    /// <exception cref="Exception"></exception>
    public void LoadScene(string name)
    {
        if (!_scenesBuilders.ContainsKey(name)) throw new Exception($"Unknown scene '{name}'!");

        _nextScene = name;   
    }

    private void _loadNextScene()
    {
        if (_nextScene == null || _nextScene == CurrentScene)
        {
            _nextScene = null;
            return;
        }

        if (_activeScene != null)
        {
            _logger.LogInformation("Unloading {0} Scene.", CurrentScene);
            _activeScene?.Shutdown();
            _activeScope?.Dispose();
            _logger.LogInformation("Unloaded {0} Scene.", CurrentScene);
            _activeScene = null;
        }

        _logger.LogInformation("Loading {0} Scene.", _nextScene);
        _activeScope = _serviceProvider.CreateScope();
        var rootScene = _serviceProvider.GetRequiredService<CurrentScene>();
        var currScene = _activeScope.ServiceProvider.GetRequiredService<CurrentScene>();
        currScene.Set(_nextScene);

        _activeScene = _scenesBuilders[_nextScene].Build(_activeScope.ServiceProvider);
        _activeScene.Startup();
        _logger.LogInformation("Loaded {0} Scene.", _nextScene);
        _nextScene = null;
    }

    /// <summary>
    /// Unloads the current scene and loads a new scene immediately.
    /// </summary>
    /// <param name="configure">The predefined method used to configure the scene during startup.</param>
    public void LoadScene(Action<ISceneBuilder> configure)
    {
        LoadScene(configure.Method.Name);
    }

    /// <summary>
    /// Unloads the current scene and loads a new scene immediately.
    /// </summary>
    /// <typeparam name="TScene">The scene type to load.</typeparam>
    public void LoadScene<TScene>() where TScene : ISceneConfiguration
    {
        LoadScene(typeof(TScene).Name);
    }

    /// <summary>
    /// Initializes the active scene.
    /// </summary>
    public void Startup()
    {
        _logger.LogDebug($"Startup");
        
        if (_activeScene != null)
        {
            _activeScene.Startup();
        }
        else
        {
            _nextScene = Scenes[0];
            _loadNextScene();
        }
    }


    public void PreUpdate(float dt)
    {
        _logger.LogDebug("PreUpdate ({0}) {1}", CurrentScene, dt);
        _loadNextScene();
        _activeScene?.PreUpdate(dt);
    }

    /// <summary>
    /// Updates the active scene (and transition, if in progress).
    /// </summary>
    /// <param name="dt">The elapsed time since the last call to Update.</param>
    public void Update(float dt)
    {
        _logger.LogDebug("Update ({0}) {1}", CurrentScene, dt);
        _activeScene?.Update(dt);
        _activeTransition?.Update(dt);
    }

    public void PostUpdate(float dt)
    {
        _logger.LogDebug("PostUpdate ({0}) {1}", CurrentScene, dt);
        _activeScene?.PostUpdate(dt);
    }

    public void PreRender(float dt)
    {
        _logger.LogDebug("PreRender ({0}) {1}", CurrentScene, dt);
        _activeScene?.PreRender(dt);
    }

    /// <summary>
    /// Draws the active scene (and transition, if in progress).
    /// </summary>
    /// <param name="dt">The elapsed time since the last call to Draw.</param>
    public void Render(float dt)
    {
        _logger.LogDebug("Render ({0}) {1}", CurrentScene, dt);
        _activeScene?.Render(dt);
        _activeTransition?.Render(dt);
    }

    public void PostRender(float dt)
    {
        _logger.LogDebug("PostRender ({0}) {1}", CurrentScene, dt);
        _activeScene?.PostRender(dt);
    }

    /// <summary>
    /// Unloads content for the active scene.
    /// </summary>
    public void Shutdown()
    {
        _logger.LogDebug("Shutdown");
        _activeScene?.Shutdown();
    }

    public void Dispose()
    {
        _activeScene = null;
        _activeScope?.Dispose();
        _activeScope = null;
    }
}