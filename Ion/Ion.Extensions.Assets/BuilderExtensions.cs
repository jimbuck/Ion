using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Assets;

public static class BuilderExtensions
{
	public static IServiceCollection AddAssets(this IServiceCollection services)
	{
		services
			.AddSingleton<GlobalAssetManager>()
			.AddScoped<IAssetManager, ScopedAssetManager>();

		return services;
	}
}
