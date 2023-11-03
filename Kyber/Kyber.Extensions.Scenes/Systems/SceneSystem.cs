using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Kyber.Extensions.Debug;

namespace Kyber.Extensions.Scenes;

internal delegate Scene SceneBuilderFactory(IConfiguration config, IServiceProvider services);

internal sealed class SceneSystem : IDisposable
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger _logger;
	private readonly IConfiguration _config;
	private readonly IEventListener _events;
	private readonly ITraceTimer _trace;
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
	public SceneSystem(IServiceProvider serviceProvider, ILogger<SceneSystem> logger, IConfiguration config, IEventListener events, ITraceTimer<SceneSystem> trace)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_config = config;
		_events = events;
		_trace = trace;
	}

	public void Register(string name, SceneBuilderFactory sceneBuilderFactory)
	{
		_scenesBuilders[name] = sceneBuilderFactory;
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
	/// Initializes the active scene.
	/// </summary>
	[Init]
	public void Init(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Init");

		_logger.LogDebug("Init ({0}) {1}", CurrentScene, dt);

		_handleChangeSceneEvents();

		if (_activeScene != null)
		{
			_activeScene.Init(dt);
		}
		else
		{
			_nextScene = _scenesBuilders.First().Key;
			_loadNextScene(dt);
		}

		timer.Stop();
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("First");
		//_logger.LogDebug("First ({0}) {1}", CurrentScene, dt);

		_handleChangeSceneEvents();

		_loadNextScene(dt);
		_activeScene?.First(dt);

		timer.Stop();
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("FixedUpdate");

		_activeScene?.FixedUpdate(dt);

		timer.Stop();
	}

	/// <summary>
	/// Updates the active scene (and transition, if in progress).
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Update.</param>
	[Update]
	public void Update(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Update");

		_activeScene?.Update(dt);

		timer.Stop();
	}

	/// <summary>
	/// Draws the active scene (and transition, if in progress).
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Draw.</param>
	[Render]
	public void Render(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Render");

		_activeScene?.Render(dt);

		timer.Stop();
	}

	[Last]
	public void Last(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Last");

		_activeScene?.Last(dt);

		timer.Stop();
	}

	/// <summary>
	/// Unloads content for the active scene.
	/// </summary>
	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Destroy");

		_logger.LogDebug("Destroy");
		_activeScene?.Destroy(dt);

		timer.Stop();
	}

	public void Dispose()
	{
		_activeScene = null;
		_activeScope?.Dispose();
		_activeScope = null;
	}

	private void _handleChangeSceneEvents()
	{
		if (_events.OnLatest<ChangeSceneEvent>(out var e))
		{
			if (!_scenesBuilders.ContainsKey(e.Data.NextScene)) _logger.LogWarning($"Tried to load unknown scene '{e.Data.NextScene}'.");

			_nextScene = e.Data.NextScene;
			e.Handled = true;
		}
	}
}
