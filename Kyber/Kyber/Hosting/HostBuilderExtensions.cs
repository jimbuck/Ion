using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Graphics;
using Kyber.Assets;
using Kyber.Storage;

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
			services.AddSingleton<IWindow, Window>();
			services.AddSingleton<IGraphicsContext, GraphicsContext>();
			services.AddSingleton<IPersistentStorage, PersistentStorage>();


			services.AddScoped<IAssetManager, AssetManager>();
			services.AddSingleton<Texture2DLoader>();

			services.AddSingleton<ISpriteRenderer, SpriteRenderer>();

			services.AddSingleton<IEventEmitter, EventEmitter>();
            services.AddTransient<IEventListener>(svcs => ((EventEmitter)svcs.GetRequiredService<IEventEmitter>()).CreateListener());
            services.AddSingleton<IInputState, InputState>();

			var gameBuilder = new GameBuilder(services);
			configure(gameBuilder);

			services.AddSingleton<IGameConfig>(gameBuilder.Config);
			services.AddSingleton<Game>(services => ActivatorUtilities.CreateInstance<Game>(services, gameBuilder.Build(services)));

			services.AddHostedService<HostedKyberService>();
		});
    }
}