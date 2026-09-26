using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ion;

/// <summary>
/// Registration of the shared <see cref="InputTracker"/> and of input recording and playback.
/// </summary>
public static class InputServiceCollectionExtensions
{
	/// <summary>
	/// Registers the application's <see cref="InputTracker"/> singleton (if not already registered): created with the
	/// <see cref="ILoopContext"/> and <see cref="InputConfig"/> (<c>Ion:Input</c>), then every registered
	/// <see cref="IInputTrackerHook"/> is attached to it. Called by the graphics backends, whose <see cref="IInputState"/>
	/// reads from it.
	/// </summary>
	public static IServiceCollection AddInputTracker(this IServiceCollection services)
	{
		services.AddOptions<InputConfig>();
		services.TryAddSingleton(static sp =>
		{
			var tracker = new InputTracker(sp.GetService<ILoopContext>(), sp.GetService<IOptions<InputConfig>>()?.Value);
			foreach (var hook in sp.GetServices<IInputTrackerHook>()) hook.Attach(tracker);
			return tracker;
		});

		return services;
	}

	/// <summary>
	/// Records every frame's input to <paramref name="path"/> (<see cref="InputRecordingFormat"/>) with an
	/// <see cref="InputRecorder"/>. The file is completed when the application is disposed. Works with any backend whose
	/// input state is built on <see cref="InputTracker"/> (both Ion backends are).
	/// </summary>
	public static IServiceCollection AddInputRecording(this IServiceCollection services, string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		services.AddSingleton(sp => new InputRecorder(path));
		services.AddSingleton<IInputTrackerHook>(static sp => sp.GetRequiredService<InputRecorder>());
		return services.AddInputTracker();
	}

	/// <summary>
	/// Replays the recording at <paramref name="path"/> with an <see cref="InputPlayer"/>: from the first frame, the
	/// application's input is the recorded input at the recorded frame numbers, and device (or scripted) input is ignored
	/// until the recording ends.
	/// </summary>
	public static IServiceCollection AddInputPlayback(this IServiceCollection services, string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		services.AddSingleton(sp => new InputPlayer(path));
		services.AddSingleton<IInputTrackerHook>(static sp => sp.GetRequiredService<InputPlayer>());
		return services.AddInputTracker();
	}

	/// <summary>
	/// Registers the application's <see cref="ScriptedInput"/> (once) and attaches it to the <see cref="InputTracker"/>:
	/// events queued on it from any thread are applied at the start of the next frame, alongside device input. The remote
	/// protocol's <c>input.send</c> uses it.
	/// </summary>
	public static IServiceCollection AddScriptedInput(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (!services.Any(static d => d.ServiceType == typeof(ScriptedInput)))
		{
			services.AddSingleton(new ScriptedInput());
			services.AddSingleton<IInputTrackerHook>(static sp => sp.GetRequiredService<ScriptedInput>());
		}

		return services.AddInputTracker();
	}
}
