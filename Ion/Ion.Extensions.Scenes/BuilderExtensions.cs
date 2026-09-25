using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Scenes;

public static class BuilderExtensions
{
	public static IServiceCollection AddScenes(this IServiceCollection services)
	{
		return services
			.AddSingleton<SceneSystem>()
			.AddSingleton<ICurrentScene, CurrentScene>();
	}

	/// <summary>
	/// Registers a scene. The first call per application also adds the scene system to the application's pipelines,
	/// at the position of that call: systems registered before it wrap the scenes, systems registered after it run
	/// after the active scene's systems in each stage.
	/// </summary>
	public static IIonApplication UseScene(this IIonApplication app, int sceneId, Action<ISceneBuilder> configure)
	{
		var sceneSystem = app.Services.GetRequiredService<SceneSystem>();
		sceneSystem.Register(sceneId, (config, services) =>
		{
			var sceneBuilder = new SceneBuilder(sceneId, config, services);
			configure(sceneBuilder);
			return sceneBuilder.Build();
		});

		// SceneSystem is a singleton of this application's service provider, so the flag is per application.
		if (!sceneSystem.IsBound)
		{
			sceneSystem.IsBound = true;
			app.UseSystem<SceneSystem>();
		}

		return app;
	}
}
