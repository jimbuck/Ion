using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Audio;

public static class BuilderExtensions
{
	/// <summary>The configuration section <see cref="AudioConfig"/> is bound from.</summary>
	public const string ConfigSection = "Ion:Audio";

	/// <summary>
	/// Registers the engine audio: the <see cref="AudioMixer"/>, an <see cref="AudioManager"/> (also resolvable as
	/// <see cref="IAudioManager"/>) playing through OpenAL, and an <see cref="ISoundEffect"/> loader that decodes WAV,
	/// OGG Vorbis and MP3 at load time. <see cref="AudioConfig"/> keeps its defaults. Pair it with <see cref="UseAudio"/>.
	/// </summary>
	public static IServiceCollection AddAudio(this IServiceCollection services) => AddAudio(services, null, null);

	/// <summary>
	/// Registers the engine audio, binding <see cref="AudioConfig"/> from <c>Ion:Audio</c> of <paramref name="config"/>
	/// and then applying <paramref name="configure"/>. The output is chosen by <see cref="AudioConfig.Backend"/>: OpenAL
	/// by default, falling back to the null output (with a warning) when no device or OpenAL library is available.
	/// Pair it with <see cref="UseAudio"/>.
	/// </summary>
	public static IServiceCollection AddAudio(this IServiceCollection services, IConfiguration? config, Action<AudioConfig>? configure = null)
	{
		_addConfig(services, config, configure);

		services
			.AddSingleton(sp => new AudioMixer(sp.GetRequiredService<IOptions<AudioConfig>>().Value))
			.AddSingleton<IAudioOutput>(sp => _createOutput(sp))
			.AddSingleton(sp => new AudioManager(sp.GetRequiredService<AudioMixer>(), sp.GetRequiredService<IAudioOutput>(), sp.GetService<ILogger<AudioManager>>()))
			.AddSingleton<IAudioManager>(sp => sp.GetRequiredService<AudioManager>())
			.AddSingleton<IAssetLoader, SoundEffectLoader>()
			.AddSingleton<AudioSystem>();

		return services;
	}

	/// <summary>
	/// Adds the audio system: starts the output (Init), flushes each frame's commands to the audio thread (Last) and
	/// stops the output (Destroy).
	/// </summary>
	public static IIonApplication UseAudio(this IIonApplication app)
	{
		return app
			.UseSystem<AudioSystem>();
	}

	/// <summary>
	/// Registers the headless audio: the real <see cref="AudioMixer"/> on a <see cref="NullAudioOutput"/> driven by the
	/// game clock, a <see cref="NullAudioManager"/> (also resolvable as <see cref="AudioManager"/> and
	/// <see cref="IAudioManager"/>) that records plays, and a <see cref="NullSoundEffectLoader"/>. No audio device is
	/// opened. <see cref="AudioConfig"/> keeps its defaults. Pair it with <see cref="UseNullAudio"/>.
	/// </summary>
	public static IServiceCollection AddNullAudio(this IServiceCollection services) => AddNullAudio(services, null, null);

	/// <summary>
	/// Registers the headless audio like <see cref="AddNullAudio(IServiceCollection)"/>, binding <see cref="AudioConfig"/>
	/// from <c>Ion:Audio</c> of <paramref name="config"/> and then applying <paramref name="configure"/>
	/// (<see cref="AudioConfig.Backend"/> is ignored: the output is always null).
	/// </summary>
	public static IServiceCollection AddNullAudio(this IServiceCollection services, IConfiguration? config, Action<AudioConfig>? configure = null)
	{
		_addConfig(services, config, configure);

		services
			.AddSingleton(sp => new AudioMixer(sp.GetRequiredService<IOptions<AudioConfig>>().Value))
			.AddSingleton(sp => new NullAudioOutput(sp.GetRequiredService<IOptions<AudioConfig>>().Value.BufferFrames))
			.AddSingleton<IAudioOutput>(sp => sp.GetRequiredService<NullAudioOutput>())
			.AddSingleton(sp => new NullAudioManager(sp.GetRequiredService<AudioMixer>(), sp.GetRequiredService<NullAudioOutput>(), sp.GetService<ILogger<AudioManager>>()))
			.AddSingleton<AudioManager>(sp => sp.GetRequiredService<NullAudioManager>())
			.AddSingleton<IAudioManager>(sp => sp.GetRequiredService<NullAudioManager>())
			.AddSingleton<IAssetLoader, NullSoundEffectLoader>()
			.AddSingleton<AudioSystem>();

		return services;
	}

	/// <summary>
	/// Adds the audio system for the headless audio: flushes each frame's commands and renders the frame's audio on the
	/// null output, driven by the game clock.
	/// </summary>
	public static IIonApplication UseNullAudio(this IIonApplication app)
	{
		return app
			.UseSystem<AudioSystem>();
	}

	private static void _addConfig(IServiceCollection services, IConfiguration? config, Action<AudioConfig>? configure)
	{
		services.AddOptions<AudioConfig>();
		if (config is not null) services.Configure<AudioConfig>(config.GetSection(ConfigSection));
		if (configure is not null) services.Configure(configure);
	}

	private static IAudioOutput _createOutput(IServiceProvider sp)
	{
		var config = sp.GetRequiredService<IOptions<AudioConfig>>().Value;
		return config.Backend switch
		{
			AudioBackend.Null => new NullAudioOutput(config.BufferFrames),
			_ => new OpenAlAudioOutput(config.BufferFrames, config.BufferCount, config.Device, sp.GetService<ILogger<OpenAlAudioOutput>>()),
		};
	}
}
