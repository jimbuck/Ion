using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

public static class BuilderExtensions
{
	/// <summary>
	/// Registers the DirectSound (NAudio) audio backend: an <see cref="IAudioManager"/> that plays through the default
	/// output device and an <see cref="ISoundEffect"/> loader that decodes whole files. Pair it with <see cref="UseAudio"/>.
	/// </summary>
	public static IServiceCollection AddAudio(this IServiceCollection services)
	{
		services
			.AddSingleton<AudioManager>()
			.AddSingleton<IAudioManager>(sp => sp.GetRequiredService<AudioManager>())
			.AddSingleton<IAssetLoader, SoundEffectLoader>()
			.AddSingleton<AudioSystem>();

		return services;
	}

	public static IIonApplication UseAudio(this IIonApplication app)
	{
		return app
			.UseSystem<AudioSystem>();
	}

	/// <summary>
	/// Registers the headless audio backend: a <see cref="NullAudioManager"/> (also resolvable as <see cref="IAudioManager"/>)
	/// that records plays instead of opening an audio device, and a <see cref="NullSoundEffectLoader"/> that reads WAV
	/// headers only. Pair it with <see cref="UseNullAudio"/>.
	/// </summary>
	public static IServiceCollection AddNullAudio(this IServiceCollection services)
	{
		services
			.AddSingleton<NullAudioManager>()
			.AddSingleton<IAudioManager>(sp => sp.GetRequiredService<NullAudioManager>())
			.AddSingleton<IAssetLoader, NullSoundEffectLoader>();

		return services;
	}

	/// <summary>
	/// Adds the headless audio backend's systems. It has none today; call it so switching backends stays symmetric.
	/// </summary>
	public static IIonApplication UseNullAudio(this IIonApplication app)
	{
		return app;
	}
}
