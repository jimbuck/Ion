using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Assets;

public static class BuilderExtensions
{
	/// <summary>
	/// Registers the asset managers (a global cache and one per scene scope), the <see cref="IAssetWatcher"/> for hot
	/// reload (watching the assets root when <c>Ion:Assets:HotReload</c> is true, the default in Debug builds) and the
	/// <see cref="AssetReloadSystem"/>.
	/// </summary>
	public static IServiceCollection AddAssets(this IServiceCollection services)
	{
		services
			.AddSingleton<GlobalAssetManager>()
			.AddScoped<IAssetManager, ScopedAssetManager>()
			.AddSingleton(static sp => new AssetWatcher(
				sp.GetRequiredService<IPersistentStorage>().Assets.GetPath(),
				AssetWatcher.IsHotReloadEnabled(sp.GetService<IConfiguration>()),
				sp.GetService<ILogger<AssetWatcher>>()))
			.AddSingleton<IAssetWatcher>(static sp => sp.GetRequiredService<AssetWatcher>())
			.AddSingleton(static sp => new AssetReloadSystem(
				sp.GetRequiredService<IAssetWatcher>(),
				sp.GetRequiredService<GlobalAssetManager>(),
				sp.GetRequiredService<IEventEmitter>()));

		return services;
	}

	/// <summary>
	/// Adds <see cref="AssetReloadSystem"/> to the First stage, so changed asset files are reloaded at the start of the next
	/// frame.
	/// </summary>
	public static IIonApplication UseAssets(this IIonApplication app)
	{
		return app.UseSystem<AssetReloadSystem>();
	}
}
