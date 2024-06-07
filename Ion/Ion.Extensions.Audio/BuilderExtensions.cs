using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

public static class BuilderExtensions
{
	public static IServiceCollection AddAudio(this IServiceCollection services)
	{
		services
			.AddSingleton<IAudioManager, AudioManager>()
			.AddSingleton<IAssetLoader, SoundEffectLoader>()
			.AddSingleton<AudioSystem>();

		return services;
	}

	public static IIonApplication UseAudio(this IIonApplication app)
	{
		return app
			.UseSystem<AudioSystem>();
	}
}
