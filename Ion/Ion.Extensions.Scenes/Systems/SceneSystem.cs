using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;

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
/// <para>
/// A <see cref="ChangeSceneEvent"/> with a <see cref="SceneTransition"/> runs across frames on game time
/// (<see cref="GameTime.Delta"/>, so it is deterministic under a fixed clock): the out phase starts the frame after the
/// event, the old scene keeps running until it ends, the new scene loads in the First step that ends it, and the in
/// phase follows. <see cref="Transition"/> exposes the progress for the code that draws it.
/// </para>
/// </remarks>
public sealed class SceneSystem(
	IServiceProvider serviceProvider,
	ILogger<SceneSystem> logger, IConfiguration config,
	IEvents events
	) : IDisposable
{
	// Each step is timed by the schedule that runs it (a span named SceneSystem.{Stage}); the scene's own steps get spans
	// from the scene's schedule.
	private readonly ILogger _logger = logger;
	private EventReader<ChangeSceneEvent> _changeScene = events.Reader<ChangeSceneEvent>();
	private readonly Dictionary<int, SceneBuilderFactory> _scenesBuilders = new();

	private SceneInstance? _activeScene;
	private IServiceScope? _activeScope;
	private bool _activeSceneDestroyed;
	// The scene to switch to. Any int is a valid id, so whether one was chosen is tracked separately.
	private int _nextSceneId;
	private bool _hasNextScene;

	// The running transition: its phase and the seconds of game time spent in that phase.
	private SceneTransition _transition;
	private TransitionPhase _phase;
	private float _elapsed;
	// Set when a request started or reversed the transition this frame: it does not advance until the next frame.
	private bool _heldThisFrame;

	/// <summary>
	/// True once this instance has been added to the application's schedule. Tracked on the (per-application) singleton
	/// so that several applications in one process each get their own scene system.
	/// </summary>
	internal bool IsBound { get; set; }

	/// <summary>
	/// The id of the active scene. Every <c>int</c> is a valid scene id (0 included), so it is only meaningful when
	/// <see cref="HasScene"/> is true; it reads 0 when no scene is loaded.
	/// </summary>
	public int CurrentSceneId => _activeScene?.Id ?? 0;

	/// <summary>Whether a scene is loaded.</summary>
	public bool HasScene => _activeScene is not null;

	/// <summary>The active scene, or null when none is loaded.</summary>
	public SceneInstance? ActiveScene => _activeScene;

	/// <summary>
	/// The running scene transition (see <see cref="ChangeSceneEvent.Transition"/>): its phase, its progress through the
	/// phase and the coverage a renderer draws. Updated in the First stage, so every step of a frame sees the same value.
	/// </summary>
	public SceneTransitionState Transition => new(_phase == TransitionPhase.None ? SceneTransition.None : _transition, _phase, _progress);

	// How far through the current phase the transition is (0 to 1).
	private float _progress
	{
		get
		{
			var duration = _phase switch
			{
				TransitionPhase.Out => _transition.OutDuration,
				TransitionPhase.In => _transition.InDuration,
				_ => 0f,
			};

			if (_phase == TransitionPhase.None) return 0f;
			return duration > 0f ? Math.Clamp(_elapsed / duration, 0f, 1f) : 1f;
		}
	}

	/// <summary>
	/// Whether a scene change is under way: a scene is being unloaded or loaded right now, a change was requested and will
	/// be applied at the start of the next frame or at the end of a transition's out phase, or a
	/// <see cref="ChangeSceneEvent"/> is waiting to be handled. The remote protocol rejects mutations while this is true,
	/// because the world they address is about to be replaced. It is false during a transition's in phase (the new scene
	/// is loaded).
	/// </summary>
	public bool IsLoading => _loading || _changePending || _changeScene.Any();

	// A scene was chosen that is not the active one.
	private bool _changePending => _hasNextScene && (_activeScene is null || _nextSceneId != _activeScene.Id);

	private bool _loading;

	internal void Register(int sceneId, SceneBuilderFactory sceneBuilderFactory)
	{
		_scenesBuilders[sceneId] = sceneBuilderFactory;
	}

	[StackTraceHidden]
	private void _loadNextScene(GameTime dt)
	{
		if (!_changePending) return;

		_loading = true;
		try
		{
			_loadScene(dt);
		}
		finally
		{
			_loading = false;
		}
	}

	[StackTraceHidden]
	private void _loadScene(GameTime dt)
	{
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
		_logger.LogDebug("Init ({CurrentSceneId}) {dt}", CurrentSceneId, dt);

		_handleChangeSceneEvents();

		if (_activeScene != null)
		{
			_activeScene.Init(dt);
		}
		else if (_scenesBuilders.Count > 0)
		{
			if (!_hasNextScene || !_scenesBuilders.ContainsKey(_nextSceneId))
			{
				_nextSceneId = _scenesBuilders.First().Key;
				_hasNextScene = true;
			}
			_loadNextScene(dt);
		}
	}

	/// <summary>
	/// Handles the <see cref="ChangeSceneEvent"/>s emitted since the previous frame and advances the running transition by
	/// <see cref="GameTime.Delta"/>: switches scene at once without a transition, or when the transition's out phase
	/// ends. Then runs the active scene's First stage.
	/// </summary>
	[StackTraceHidden]
	[First(Order = StageOrder.Scenes)]
	public void First(GameTime dt)
	{
		_heldThisFrame = false;
		_handleChangeSceneEvents();

		switch (_phase)
		{
			case TransitionPhase.Out:
				if (!_heldThisFrame) _elapsed += dt.Delta;
				if (_elapsed >= _transition.OutDuration)
				{
					if (_changePending) _loadNextScene(dt);
					_startPhase(TransitionPhase.In, 0f);
				}
				break;

			case TransitionPhase.In:
				if (!_heldThisFrame) _elapsed += dt.Delta;
				if (_elapsed >= _transition.InDuration) _startPhase(TransitionPhase.None, 0f);
				break;

			default:
				if (_changePending) _loadNextScene(dt);
				break;
		}

		_activeScene?.First(dt);
	}

	private void _startPhase(TransitionPhase phase, float elapsed)
	{
		_elapsed = elapsed;
		// An empty in phase ends at once. An empty out phase still runs for one First step, which switches the scene.
		_phase = phase == TransitionPhase.In && !(_transition.InDuration > 0f) ? TransitionPhase.None : phase;
		if (_phase == TransitionPhase.None)
		{
			_transition = SceneTransition.None;
			_elapsed = 0f;
		}
	}

	/// <summary>Runs the active scene's FixedUpdate stage.</summary>
	[StackTraceHidden]
	[FixedUpdate(Order = StageOrder.Scenes)]
	public void FixedUpdate(GameTime dt)
	{
		_activeScene?.FixedUpdate(dt);
	}

	/// <summary>Runs the active scene's Update stage.</summary>
	[StackTraceHidden]
	[Update(Order = StageOrder.Scenes)]
	public void Update(GameTime dt)
	{
		_activeScene?.Update(dt);
	}

	/// <summary>Runs the active scene's Render stage.</summary>
	[StackTraceHidden]
	[Render(Order = StageOrder.Scenes)]
	public void Render(GameTime dt)
	{
		_activeScene?.Render(dt);
	}

	/// <summary>Runs the active scene's Last stage.</summary>
	[StackTraceHidden]
	[Last(Order = StageOrder.Scenes)]
	public void Last(GameTime dt)
	{
		_activeScene?.Last(dt);
	}

	/// <summary>Runs the active scene's Destroy stage (once).</summary>
	[StackTraceHidden]
	[Destroy(Order = StageOrder.Scenes)]
	public void Destroy(GameTime dt)
	{
		_logger.LogDebug("Destroy");
		if (_activeScene != null && !_activeSceneDestroyed)
		{
			_activeScene.Destroy(dt);
			_activeSceneDestroyed = true;
		}
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
		if (!_changeScene.TryReadLatest(out var e)) return;

		if (!_scenesBuilders.ContainsKey(e.NextSceneId))
		{
			_logger.LogError("Tried to load unknown scene '{NextSceneId}'; staying on scene '{CurrentSceneId}'.", e.NextSceneId, CurrentSceneId);
			return;
		}

		var transition = _sanitize(e.Transition);
		var coverage = Transition.Coverage;
		var shown = _activeScene is not null && e.NextSceneId == _activeScene.Id;

		_nextSceneId = e.NextSceneId;
		_hasNextScene = true;

		if (shown)
		{
			// The scene on screen was asked for again: nothing to load. A running out phase turns around and uncovers it
			// from where it got to; otherwise the request changes nothing.
			if (_phase == TransitionPhase.Out)
			{
				_startPhase(TransitionPhase.In, (1f - coverage) * _transition.InDuration);
				_heldThisFrame = true;
			}

			return;
		}

		if (transition.IsNone)
		{
			// An immediate change, which also cancels a running transition.
			_startPhase(TransitionPhase.None, 0f);
			return;
		}

		// Cover the old scene, starting from the coverage already reached (so a transition that is turned around or
		// retargeted does not jump). Without an active scene there is nothing to cover.
		var wasOut = _phase == TransitionPhase.Out;
		_transition = transition;
		_startPhase(TransitionPhase.Out, (_activeScene is null ? 1f : coverage) * transition.OutDuration);
		// A new or reversed transition holds for the frame it starts in; a retargeted out phase keeps running.
		_heldThisFrame = !wasOut;
	}

	private static SceneTransition _sanitize(SceneTransition transition)
	{
		if (transition.IsNone) return SceneTransition.None;
		return transition with
		{
			OutDuration = transition.OutDuration > 0f ? transition.OutDuration : 0f,
			InDuration = transition.InDuration > 0f ? transition.InDuration : 0f,
		};
	}
}
