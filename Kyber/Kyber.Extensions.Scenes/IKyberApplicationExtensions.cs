using Kyber.Scenes;

using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Builder;

public static class IKyberApplicationExtensions
{
	private static bool _scenesAdded = false;

	public static IServiceCollection AddScenes(this IServiceCollection services)
	{
		return services
			.AddSingleton<ISceneManager, SceneManager>()
			.AddSingleton<ICurrentScene, CurrentScene>();
	}

	public static IKyberApplication UseScene(this IKyberApplication app, string name, Action<ISceneBuilder> configure)
	{
		var sceneManager = (SceneManager)app.Services.GetRequiredService<ISceneManager>();
		sceneManager.Register(name, (config, services) =>
		{
			var sceneBuilder = new SceneBuilder(name, config, services);
			configure(sceneBuilder);
			return sceneBuilder.Build();
		});

		if (!_scenesAdded)
		{
			_scenesAdded = true;
			app.UseSystem<ISceneManager, SceneManager>();
		}

		return app;
	}
}
