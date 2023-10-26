using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Kyber.Scenes;
using Kyber.Builder;

namespace Kyber.Hosting.Scenes;

public static class IGameBuilderExtensions
{
    //public static GameApplication AddScene(this GameApplication gameBuilder, Action<ISceneBuilder> configure)
    //{
    //    if (string.IsNullOrWhiteSpace(configure.Method.Name)) throw new ArgumentException("Scenes configured via methods must use named methods OR specify a name.");

    //    return AddScene(gameBuilder, configure.Method.Name, configure);
    //}

    //public static GameApplication AddScene(this GameApplication gameBuilder, string name, Action<ISceneBuilder> configure)
    //{
    //    var sceneBuilder = new SceneBuilder(name, gameBuilder.Services);
    //    configure(sceneBuilder);

    //    return _addScene(gameBuilder, sceneBuilder);
    //}

    //public static GameApplication AddScene<T>(this GameApplication gameBuilder) where T : class, ISceneConfiguration
    //{
    //    return AddScene<T>(gameBuilder, typeof(T).Name);
    //}

    //public static GameApplication AddScene<T>(this GameApplication gameBuilder, string name) where T : class, ISceneConfiguration
    //{
    //    var sceneBuilder = new SceneBuilder(name, gameBuilder.Services);
    //    var configMethod = Activator.CreateInstance<T>();
    //    if (configMethod is not null) configMethod.Configure(sceneBuilder);

    //    return _addScene(gameBuilder, sceneBuilder);
    //}

    //private static GameApplication _addScene(GameApplication gameBuilder, SceneBuilder sceneBuilder)
    //{
    //    // Only add the scene manager once.
    //    gameBuilder.Services.TryAddSingleton<SceneManager>(svc => new SceneManager(svc, svc.GetRequiredService<ILogger<SceneManager>>(), svc.GetServices<SceneBuilder>()));
    //    gameBuilder.Services.TryAddScoped<ICurrentScene, CurrentScene>();

    //    //gameBuilder.AddSystem<SceneManager>();

    //    // Add the SceneBuilder so that the scene manager can import all added ISceneConfiguration.
    //    gameBuilder.Services.AddTransient(_ => sceneBuilder);

    //    return gameBuilder;
    //} 
}
