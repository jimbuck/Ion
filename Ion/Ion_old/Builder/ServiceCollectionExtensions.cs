using Microsoft.Extensions.DependencyInjection;

namespace Ion.Builder;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddGameConfig(this IServiceCollection services, Action<IGameConfig> config)
	{
		return services.AddSingleton<IGameConfig>(_ =>
		{
			var gameConfig = new GameConfig();
			config(gameConfig);
			return gameConfig;
		});
	}
}
