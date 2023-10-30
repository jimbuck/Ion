using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Extensions.Scenes;

public static class BuilderExtensions
{
	private static bool _scenesAdded = false;

	public static IServiceCollection AddScenes(this IServiceCollection services)
	{
		return services
			.AddSingleton<SceneSystem>()
			.AddSingleton<ICurrentScene, CurrentScene>();
	}

	public static IKyberApplication UseScene(this IKyberApplication app, string name, Action<ISceneBuilder> configure)
	{
		var sceneManager = app.Services.GetRequiredService<SceneSystem>();
		sceneManager.Register(name, (config, services) =>
		{
			var sceneBuilder = new SceneBuilder(name, config, services);
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
