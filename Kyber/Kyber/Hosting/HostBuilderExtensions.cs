using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Systems;
using Kyber.Graphics;

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
			var gameBuilder = new GameBuilder(services);
			gameBuilder.DirectAddSystem<EventSystem>();

			gameBuilder.DirectAddSystem<Window>();
			gameBuilder.DirectAddSystem<GraphicsDevice>();
			configure(gameBuilder);
			gameBuilder.AddSystem<ExitSystem>();
			gameBuilder.AddSystem<ViewResizeSystem>();
			
			services.AddSingleton<IGameConfig>(gameBuilder.Config);
			services.AddSingleton<Game>(services => ActivatorUtilities.CreateInstance<Game>(services, gameBuilder.Build(services)));

			services.AddSingleton<Window>();
            services.AddSingleton<GraphicsDevice>();
			services.AddSingleton<IGraphicsDevice>(s => s.GetRequiredService<GraphicsDevice>());

			services.AddSingleton<EventSystem>();
			services.AddSingleton<IEventEmitter>(s => s.GetRequiredService<EventSystem>());
            services.AddTransient<IEventListener>(svcs => svcs.GetRequiredService<EventSystem>().CreateListener());
            services.AddSingleton<IInputState, InputState>();

            //services.AddSingleton(serviceProvider => InternalGame.Instance.Content);
            //services.AddSingleton(serviceProvider => InternalGame.Instance.SpriteBatch);

            services.AddHostedService<HostedKyberService>();
		});
    }
}