using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Ion.Extensions.Debug;

namespace Ion.Extensions.Scenes;

internal delegate SceneInstance SceneBuilderFactory(IConfiguration config, IServiceProvider services);

/// <summary>
/// Owns the registered scenes of one application and runs the active scene's schedule.
/// </summary>
/// <remarks>
/// In every stage it is one step at order <see cref="StageOrder.Scenes"/> (the end of the engine setup band): the active
/// scene's steps run after the engine's setup steps (window, input, frame and sprite batch scopes) and before the
/// application's own steps at the default order, whatever the registration order. Use <c>[Before&lt;SceneSystem&gt;]</c>
/// or a lower order to run an application step before the scene. Scene steps are ordered among themselves with the same
/// rules as application steps.
/// </remarks>
public sealed class SceneSystem(
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
	/// True once this instance has been added to the application's schedule. Tracked on the (per-application) singleton
	/// so that several applications in one process each get their own scene system.
	/// </summary>
	internal bool IsBound { get; set; }

	/// <summary>The id of the active scene, or 0 when none is loaded.</summary>
	public int CurrentSceneId => _activeScene?.Id ?? 0;

	/// <summary>The active scene, or null when none is loaded.</summary>
	public SceneInstance? ActiveScene => _activeScene;

	internal void Register(int sceneId, SceneBuilderFactory sceneBuilderFactory)
	{
		_scenesBuilders[sceneId] = sceneBuilderFactory;
	}

	[StackTraceHidden]
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
	/// Loads the first scene (running its Init stage), or runs the active scene's Init stage again.
	/// </summary>
	[StackTraceHidden]
	[Init(Order = StageOrder.Scenes)]
	public void Init(GameTime dt)
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
	}

	/// <summary>
	/// Switches scene when a <see cref="ChangeSceneEvent"/> asked for it, then runs the active scene's First stage.
	/// </summary>
	[StackTraceHidden]
	[First(Order = StageOrder.Scenes)]
	public void First(GameTime dt)
	{
		var timer = _trace.Start("First");

		_handleChangeSceneEvents();

		if (_nextSceneId != CurrentSceneId) _loadNextScene(dt);
		_activeScene?.First(dt);

		timer.Stop();
	}

	/// <summary>Runs the active scene's FixedUpdate stage.</summary>
	[StackTraceHidden]
	[FixedUpdate(Order = StageOrder.Scenes)]
	public void FixedUpdate(GameTime dt)
	{
		var timer = _trace.Start("FixedUpdate");
		_activeScene?.FixedUpdate(dt);
		timer.Stop();
	}

	/// <summary>Runs the active scene's Update stage.</summary>
	[StackTraceHidden]
	[Update(Order = StageOrder.Scenes)]
	public void Update(GameTime dt)
	{
		var timer = _trace.Start("Update");
		_activeScene?.Update(dt);
		timer.Stop();
	}

	/// <summary>Runs the active scene's Render stage.</summary>
	[StackTraceHidden]
	[Render(Order = StageOrder.Scenes)]
	public void Render(GameTime dt)
	{
		var timer = _trace.Start("Render");
		_activeScene?.Render(dt);
		timer.Stop();
	}

	/// <summary>Runs the active scene's Last stage.</summary>
	[StackTraceHidden]
	[Last(Order = StageOrder.Scenes)]
	public void Last(GameTime dt)
	{
		var timer = _trace.Start("Last");
		_activeScene?.Last(dt);
		timer.Stop();
	}

	/// <summary>Runs the active scene's Destroy stage (once).</summary>
	[StackTraceHidden]
	[Destroy(Order = StageOrder.Scenes)]
	public void Destroy(GameTime dt)
	{
		var timer = _trace.Start("Destroy");

		_logger.LogDebug("Destroy");
		if (_activeScene != null && !_activeSceneDestroyed)
		{
			_activeScene.Destroy(dt);
			_activeSceneDestroyed = true;
		}

		timer.Stop();
	}

	/// <summary>Destroys the active scene if its Destroy stage has not run, and disposes its scope.</summary>
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
