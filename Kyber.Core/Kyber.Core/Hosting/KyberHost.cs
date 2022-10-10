using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Core.Hosting;

/// <summary>
/// Helper methods for building and configuring a Kyber game.
/// </summary>
public static class KyberHost
{
    /// <summary>
    /// Creates a basic HostBuilder with the following presets:
    ///   - Sets the content root to the current directory.
    ///   - Adds command line args to the host configuration.
    ///   - Adds command line args to the app configuration.
    ///   - Enables console and debug logging.
    /// </summary>
    /// <param name="args">Command line args passed.</param>
    /// <returns>A HostBuilder ready for additional configuration.</returns>
    public static IHostBuilder CreateDefaultBuilder(params string[] args)
    {
        var builder = new HostBuilder();

        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.ConfigureHostConfiguration(config =>
        {
            if (args != null && args.Length > 0) config.AddCommandLine(args);
        });
        builder.ConfigureAppConfiguration((hostingContext, config) =>
        {
            if (args != null && args.Length > 0) config.AddCommandLine(args);
        });

        builder.ConfigureLogging(config =>
        {
            config.AddSimpleConsole(options =>
            {
                //options.TimestampFormat = "HH:mm:ss";
                options.SingleLine = true;
            })
            .AddDebug();
        });

        return builder;
    }

    /// <summary>
    /// Configures the services for Kyber. 
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <param name="configure">Callback to configure the basic options for the game.</param>
    /// <returns>The host builder instance.</returns>
    public static IHostBuilder ConfigureMonoGame<T>(this IHostBuilder hostBuilder)//, Func<BaseGameConfig, BaseGameConfig> configure) where T : BaseGame
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            //services.AddSingleton(serviceProvider => configure(new BaseGameConfig()));
            //services.AddSingleton<BaseGame, T>();
            //services.AddSingleton<Game>(serviceProvider => InternalGame.Instance);

            //services.AddSingleton(serviceProvider => InternalGame.Instance.Graphics);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.LaunchParameters);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.Window);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.GraphicsDevice);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.Services);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.Content);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.SpriteBatch);

            //services.AddHostedService<HostedMonogameService>();

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

    /// <summary>
    /// Runs the configured instance of Kyber directly (not as a hosted service).
    /// </summary>
    /// <param name="host">The built and configured host.</param>
    public static void RunMonoGame(this IHost host)
    {
        //Console.WriteLine("Creating InternalGame");
        //var internalGame = ActivatorUtilities.CreateInstance<InternalGame>(host.Services);
        //internalGame.Run();
    }
}
