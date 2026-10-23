using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Scenes;

internal class SceneBuilder(int sceneId, IConfiguration config, IServiceProvider services) : ISceneBuilder
{
	public int SceneId { get; } = sceneId;

	public IConfiguration Configuration { get; } = config;

	public IServiceProvider Services { get; } = services;

	public ScheduleModel Schedule { get; } = new(SceneName(sceneId), isRoot: false);

	public static string SceneName(int sceneId) => $"Scene {sceneId}";

	/// <summary>
	/// Binds the scene's schedule to its scope. Warnings were already logged when the application's schedule was built
	/// (scenes are planned with it), so they are not logged again on every load.
	/// </summary>
	internal SceneInstance Build()
	{
		var schedule = Schedule.Build(Services, logWarnings: false);
		Schedule.Freeze();
		return new SceneInstance(SceneId, schedule, Services);
	}
}
