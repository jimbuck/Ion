using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Scenes;

internal class SceneBuilder(int sceneId, IConfiguration config, IServiceProvider services) : ISceneBuilder
{
	public int SceneId { get; } = sceneId;

	public IConfiguration Configuration { get; } = config;

	public IServiceProvider Services { get; } = services;

	public ScheduleModel Schedule { get; } = new(SceneName(sceneId), isRoot: false);

	public static string SceneName(int sceneId) => $"Scene {sceneId}";

	public ISceneBuilder UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Init, middleware);

	public ISceneBuilder UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.First, middleware);

	public ISceneBuilder UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.FixedUpdate, middleware);

	public ISceneBuilder UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Update, middleware);

	public ISceneBuilder UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Render, middleware);

	public ISceneBuilder UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Last, middleware);

	public ISceneBuilder UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Destroy, middleware);

	private SceneBuilder UseMiddleware(Stage stage, Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		Schedule.AddMiddleware(stage, middleware);
		return this;
	}

	/// <summary>
	/// Binds the scene's schedule to its scope. Warnings were already logged when the application's schedule was built
	/// (scenes are planned with it), so they are not logged again on every load.
	/// </summary>
	internal SceneInstance Build()
	{
		var schedule = Schedule.Build(Services, logWarnings: false);
		Schedule.Freeze();
		return new SceneInstance(SceneId, schedule);
	}
}
