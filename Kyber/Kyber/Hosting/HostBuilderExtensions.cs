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
			services.AddSingleton<IWindow, Window>();
			services.AddSingleton<IGraphicsDevice, GraphicsDevice>();
			services.AddSingleton<ISpriteRenderer, SpriteRenderer>();

			services.AddSingleton<IEventEmitter, EventEmitter>();
            services.AddTransient<IEventListener>(svcs => ((EventEmitter)svcs.GetRequiredService<IEventEmitter>()).CreateListener());
            services.AddSingleton<IInputState, InputState>();

			//services.AddSingleton(serviceProvider => InternalGame.Instance.Content);
			//services.AddSingleton(serviceProvider => InternalGame.Instance.SpriteBatch);

			var gameBuilder = new GameBuilder(services);
			gameBuilder.AddSingletonSystem<EventSystem>();
			gameBuilder.AddSingletonSystem<WindowSystems>();
			gameBuilder.AddSingletonSystem<GraphicsDeviceInitializerSystem>();
			gameBuilder.AddSingletonSystem<SpriteRendererBeginSystem>();
			configure(gameBuilder);
			gameBuilder.AddSingletonSystem<SpriteRendererEndSystem>();
			gameBuilder.AddSingletonSystem<ExitSystem>();
			gameBuilder.AddSingletonSystem<WindowResizeSystem>();

			services.AddSingleton<IGameConfig>(gameBuilder.Config);
			services.AddSingleton<Game>(services => ActivatorUtilities.CreateInstance<Game>(services, gameBuilder.Build(services)));

			services.AddHostedService<HostedKyberService>();
		});
    }
}