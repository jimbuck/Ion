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
	/// Registers a scene. <paramref name="configure"/> adds the scene's systems and steps; it runs every time the scene
	/// loads (building the scene's schedule in a fresh service scope), and once more when the application's schedule is
	/// built, to validate the scene and include it in <c>PrintSchedule</c>. The first call per application also adds the
	/// <see cref="SceneSystem"/>, whose steps run the active scene at order <see cref="StageOrder.Scenes"/> in every stage,
	/// so the position of this call relative to other registrations does not matter.
	/// </summary>
	public static IIonApplication UseScene(this IIonApplication app, int sceneId, Action<ISceneBuilder> configure)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(configure);

		var sceneSystem = app.Services.GetRequiredService<SceneSystem>();
		sceneSystem.Register(sceneId, (config, services) =>
		{
			var sceneBuilder = new SceneBuilder(sceneId, config, services);
			configure(sceneBuilder);
			return sceneBuilder.Build();
		});

		app.Schedule.AddNested(SceneBuilder.SceneName(sceneId), typeof(SceneSystem), services =>
		{
			using var scope = (services ?? app.Services).CreateScope();
			var sceneBuilder = new SceneBuilder(sceneId, app.Configuration, scope.ServiceProvider);
			configure(sceneBuilder);
			return sceneBuilder.Schedule.Plan(services is null ? null : scope.ServiceProvider);
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
