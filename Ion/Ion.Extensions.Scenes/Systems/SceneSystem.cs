using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Ion.Extensions.Debug;

namespace Ion.Extensions.Scenes;

internal delegate Scene SceneBuilderFactory(IConfiguration config, IServiceProvider services);

/// <summary>
/// Creates a new SceneManager instance, keeping a reference to the service provider.
/// </summary>
/// <param name="serviceProvider">The root service provider.</param>
internal sealed class SceneSystem(
	IServiceProvider serviceProvider,
	ILogger<SceneSystem> logger, IConfiguration config,
	IEventListener events,
	ITraceTimer<SceneSystem> trace
	) : IDisposable
{
	private readonly ILogger _logger = logger;
	private readonly ITraceTimer _trace = trace;
	private readonly Dictionary<int, SceneBuilderFactory> _scenesBuilders = new();

	private Scene? _activeScene;
	private Transition? _activeTransition;
	private IServiceScope? _activeScope;
	private int _nextSceneId = 0;

	public int CurrentSceneId => _activeScene?.Id ?? 0;

	public void Register(int sceneId, SceneBuilderFactory sceneBuilderFactory)
	{
		_scenesBuilders[sceneId] = sceneBuilderFactory;
	}

	private void _loadNextScene(GameTime dt)
	{
		if (_nextSceneId == CurrentSceneId) return;

		if (_activeScene != null)
		{
			_logger.LogInformation("Unloading {CurrentSceneId} Scene.", CurrentSceneId);
			_activeScene?.Destroy(dt);
			_activeScope?.Dispose();
			_logger.LogInformation("Unloaded {CurrentSceneId} Scene.", CurrentSceneId);
			_activeScene = null;
		}

		_logger.LogInformation("Loading {NextScene} Scene.", _nextSceneId);
		_activeScope = serviceProvider.CreateScope();
		var currScene = (CurrentScene)_activeScope.ServiceProvider.GetRequiredService<ICurrentScene>();
		currScene.Set(_nextSceneId);

		_activeScene = _scenesBuilders[_nextSceneId](config, _activeScope.ServiceProvider);
		_activeScene.Init(dt);
		_logger.LogInformation("Loaded {NextScene} Scene.", _nextSceneId);
	}

	/// <summary>
	/// Initializes the active scene.
	/// </summary>
	[Init]
	public void Init(GameTime dt, GameLoopDelegate _)
	{
		var timer = _trace.Start("Init");

		_logger.LogDebug("Init ({CurrentSceneId}) {dt}", CurrentSceneId, dt);

		_handleChangeSceneEvents();

		if (_activeScene != null)
		{
			_activeScene.Init(dt);
		}
		else
		{
			_nextSceneId = _scenesBuilders.First().Key;
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

		if (_nextSceneId != CurrentSceneId) _loadNextScene(dt);
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
		_activeTransition?.Update(dt);

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
		_activeTransition?.Render(dt);

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
		if (events.OnLatest<ChangeSceneEvent>(out var e))
		{
			if (!_scenesBuilders.ContainsKey(e.Data.NextSceneId)) _logger.LogWarning("Tried to load unknown scene '{NextSceneId}'.", e.Data.NextSceneId);

			_nextSceneId = e.Data.NextSceneId;
			e.Handled = true;
		}
	}
}
