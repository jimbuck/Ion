using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Scenes;

public static class BuilderExtensions
{
	private static bool _scenesAdded = false;

	public static IServiceCollection AddScenes(this IServiceCollection services)
	{
		return services
			.AddSingleton<SceneSystem>()
			.AddSingleton<ICurrentScene, CurrentScene>();
	}

	public static IIonApplication UseScene(this IIonApplication app, int sceneId, Action<ISceneBuilder> configure)
	{
		var sceneManager = app.Services.GetRequiredService<SceneSystem>();
		sceneManager.Register(sceneId, (config, services) =>
		{
			var sceneBuilder = new SceneBuilder(sceneId, config, services);
			configure(sceneBuilder);
			return sceneBuilder.Build();
		});

		if (!_scenesAdded)
		{
			_scenesAdded = true;
			app.UseSystem<SceneSystem>();
		}

		return app;
	}
}
