﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Events;

namespace Kyber.Hosting;

public static class KyberHostBuilderExtensions
{
    /// <summary>
    /// Configures the services for Kyber. 
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <param name="configure">Callback to configure the basic options for the game.</param>
    /// <returns>The host builder instance.</returns>
    public static IHostBuilder ConfigureKyber(this IHostBuilder hostBuilder, Action<IGameBuilder> configure)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
            var startupConfig = new StartupConfig();
            var gameBuilder = new GameBuilder(services);
            configure(gameBuilder);

            services.AddSingleton<IStartupConfig>(gameBuilder.Config);

            services.AddSingleton<Game>(services => ActivatorUtilities.CreateInstance<Game>(services, gameBuilder.Build(services)));
            services.AddSingleton<Window>();
            services.AddSingleton<GraphicsDevice>();
            services.AddSingleton<IEventEmitter, EventSystem>();
            services.AddTransient<IEventListener>(svcs => ((EventSystem)svcs.GetRequiredService<IEventEmitter>()).CreateListener());
            services.AddSingleton<InputState>();

            //services.AddSingleton(serviceProvider => InternalGame.Instance.Content);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.SpriteBatch);

            services.AddHostedService<HostedKyberService>();
        });
    }
}