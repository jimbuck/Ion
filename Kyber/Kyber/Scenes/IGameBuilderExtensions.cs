using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Kyber.Scenes;

namespace Kyber.Hosting.Scenes;

public static class IGameBuilderExtensions
{
    public static IGameBuilder AddScene(this IGameBuilder gameBuilder, Action<ISceneBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(configure.Method.Name)) throw new ArgumentException("Scenes configured via methods must use named methods OR specify a name.");

        return AddScene(gameBuilder, configure.Method.Name, configure);
    }

    public static IGameBuilder AddScene(this IGameBuilder gameBuilder, string name, Action<ISceneBuilder> configure)
    {
        var sceneBuilder = new SceneBuilder(name, gameBuilder.Services);
        configure(sceneBuilder);

        return _addScene(gameBuilder, sceneBuilder);
    }

    public static IGameBuilder AddScene<T>(this IGameBuilder gameBuilder) where T : class, ISceneConfiguration
    {
        return AddScene<T>(gameBuilder, typeof(T).Name);
    }

    public static IGameBuilder AddScene<T>(this IGameBuilder gameBuilder, string name) where T : class, ISceneConfiguration
    {
        var sceneBuilder = new SceneBuilder(name, gameBuilder.Services);
        var configMethod = Activator.CreateInstance<T>();
        if (configMethod is not null) configMethod.Configure(sceneBuilder);

        return _addScene(gameBuilder, sceneBuilder);
    }

    private static IGameBuilder _addScene(IGameBuilder gameBuilder, SceneBuilder sceneBuilder)
    {
        // Only add the scene manager once.
        gameBuilder.Services.TryAddSingleton<SceneManager>(svc => new SceneManager(svc, svc.GetRequiredService<ILogger<SceneManager>>(), svc.GetServices<SceneBuilder>()));
        gameBuilder.Services.TryAddScoped<CurrentScene>();

        gameBuilder.AddSystem<SceneManager>();

        // Add the SceneBuilder so that the scene manager can import all added ISceneConfiguration.
        gameBuilder.Services.AddTransient(_ => sceneBuilder);

        return gameBuilder;
    } 
}
