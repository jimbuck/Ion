using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Core.Hosting;

public static class KyberHostBuilderExtensions
{
    /// <summary>
    /// Configures the services for Kyber. 
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <param name="configure">Callback to configure the basic options for the game.</param>
    /// <returns>The host builder instance.</returns>
    public static IHostBuilder ConfigureKyber(this IHostBuilder hostBuilder, Action<IGameBuilder, IServiceCollection> configure)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            var gameBuilder = new GameBuilder();
            configure(gameBuilder, services);

            gameBuilder.Configure(hostContext, services);

            services.AddSingleton<StartupConfig>(gameBuilder.Config);

            services.AddSingleton<Game>();
            services.AddSingleton<Window>();
            services.AddSingleton<GraphicsDevice>();

            //services.AddSingleton(serviceProvider => InternalGame.Instance.Content);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.SpriteBatch);

            services.AddHostedService<HostedKyberService>();
        });
    }

    /// <summary>
    /// Adds SceneManager plus GlobalContentManager and IContentManager (scoped) to the available services.
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <returns>The host builder instance.</returns>
    public static IHostBuilder ConfigureScenes(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            //services.AddSingleton<SceneManager>();

            //services.AddSingleton(serviceProvider =>
            //{
            //    var gameServices = serviceProvider.GetRequiredService<GameServiceContainer>();
            //    var config = serviceProvider.GetRequiredService<BaseGameConfig>();

            //    return new GlobalContentManager(gameServices, config.RootDirectory);
            //});
            //services.AddScoped<IContentManager, ScopedContentManager>();
        });
    }
}