using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Ion.Extensions.Debug;

namespace Ion.Extensions.Scenes;

internal delegate SceneInstance SceneBuilderFactory(IConfiguration config, IServiceProvider services);

/// <summary>
/// Owns the registered scenes of one application and runs the active scene's pipelines.
/// </summary>
/// <remarks>
/// Ordering: in every stage the active scene's systems run first, then <c>next</c> is called so that any application
/// systems registered after <c>UseScene</c> still run. Systems registered before <c>UseScene</c> wrap the scene
/// (their code before <c>next</c> runs before the scene, their code after <c>next</c> runs after it).
/// </remarks>
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

	private SceneInstance? _activeScene;
	private IServiceScope? _activeScope;
	private bool _activeSceneDestroyed;
	private int _nextSceneId = 0;

	/// <summary>
	/// True once this instance has been added to the application's pipelines. Tracked on the (per-application) singleton
	/// so that several applications in one process each get their own scene system.
	/// </summary>
	internal bool IsBound { get; set; }

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
			if (!_activeSceneDestroyed) _activeScene.Destroy(dt);
			_activeScope?.Dispose();
			_logger.LogInformation("Unloaded {CurrentSceneId} Scene.", CurrentSceneId);
			_activeScene = null;
			_activeScope = null;
		}

		_logger.LogInformation("Loading {NextScene} Scene.", _nextSceneId);
		_activeScope = serviceProvider.CreateScope();
		var currScene = (CurrentScene)_activeScope.ServiceProvider.GetRequiredService<ICurrentScene>();
		currScene.Set(_nextSceneId);

		_activeScene = _scenesBuilders[_nextSceneId](config, _activeScope.ServiceProvider);
		_activeSceneDestroyed = false;
		_activeScene.Init(dt);
		_logger.LogInformation("Loaded {NextScene} Scene.", _nextSceneId);
	}

	/// <summary>
	/// Initializes the active scene.
	/// </summary>
	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Init");

		_logger.LogDebug("Init ({CurrentSceneId}) {dt}", CurrentSceneId, dt);

		_handleChangeSceneEvents();

		if (_activeScene != null)
		{
			_activeScene.Init(dt);
		}
		else if (_scenesBuilders.Count > 0)
		{
			if (!_scenesBuilders.ContainsKey(_nextSceneId)) _nextSceneId = _scenesBuilders.First().Key;
			_loadNextScene(dt);
		}

		timer.Stop();

		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("First");

		_handleChangeSceneEvents();

		if (_nextSceneId != CurrentSceneId) _loadNextScene(dt);
		_activeScene?.First(dt);

		timer.Stop();

		next(dt);
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("FixedUpdate");

		_activeScene?.FixedUpdate(dt);

		timer.Stop();

		next(dt);
	}

	/// <summary>
	/// Updates the active scene.
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Update.</param>
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Update");

		_activeScene?.Update(dt);

		timer.Stop();

		next(dt);
	}

	/// <summary>
	/// Draws the active scene.
	/// </summary>
	/// <param name="dt">The elapsed time since the last call to Draw.</param>
	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Render");

		_activeScene?.Render(dt);

		timer.Stop();

		next(dt);
	}

	[Last]
	public void Last(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Last");

		_activeScene?.Last(dt);

		timer.Stop();

		next(dt);
	}

	/// <summary>
	/// Unloads content for the active scene.
	/// </summary>
	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Destroy");

		_logger.LogDebug("Destroy");
		if (_activeScene != null && !_activeSceneDestroyed)
		{
			_activeScene.Destroy(dt);
			_activeSceneDestroyed = true;
		}

		timer.Stop();

		next(dt);
	}

	public void Dispose()
	{
		if (_activeScene != null && !_activeSceneDestroyed)
		{
			try
			{
				_activeScene.Destroy(new GameTime());
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error while destroying scene {CurrentSceneId} during dispose.", CurrentSceneId);
			}
			_activeSceneDestroyed = true;
		}

		_activeScene = null;
		_activeScope?.Dispose();
		_activeScope = null;
	}

	private void _handleChangeSceneEvents()
	{
		if (events.OnLatest<ChangeSceneEvent>(out var e))
		{
			e.Handled = true;

			if (!_scenesBuilders.ContainsKey(e.Data.NextSceneId))
			{
				_logger.LogError("Tried to load unknown scene '{NextSceneId}'; staying on scene '{CurrentSceneId}'.", e.Data.NextSceneId, CurrentSceneId);
				return;
			}

			_nextSceneId = e.Data.NextSceneId;
		}
	}
}
