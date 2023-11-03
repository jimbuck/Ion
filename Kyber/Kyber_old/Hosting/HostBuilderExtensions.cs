﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Builder;

namespace Kyber.Hosting;

public static class KyberHostBuilderExtensions
{
    /// <summary>
    /// Configures the services for Kyber. 
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <param name="configure">Callback to configure the basic options for the game.</param>
    /// <returns>The host builder instance.</returns>
    public static IHostBuilder ConfigureKyber(this IHostBuilder hostBuilder, Action<GameApplication> configure)
    {
        return hostBuilder.ConfigureServices((hostContext, services) =>
        {
			//services.AddSingleton<IPersistentStorage, PersistentStorage>();
			//services.AddSingleton<IEventEmitter, EventEmitter>();
			//services.AddTransient<IEventListener, EventListener>();
			//services.AddSingleton<IInputState, InputState>();

			//services.AddSingleton<IWindow, Window>();
			//services.AddSingleton<IGraphicsContext, GraphicsContext>();
			//services.AddScoped<IAssetManager, AssetManager>();
			//services.AddSingleton<Texture2DLoader>();

			//services.AddSingleton<ISpriteBatch, SpriteBatch>();




			//services.AddScoped<World>(svcs => World.Create());

			//var gameBuilder = new GameApplication(services);
			//configure(gameBuilder);

			//services.AddSingleton<IGameConfig>(gameBuilder.Config);
			//services.AddSingleton<GameLoop>(services => gameBuilder.Build(services));

			//services.AddHostedService<HostedKyberService>();
		});
    }
}