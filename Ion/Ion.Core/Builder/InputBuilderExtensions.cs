namespace Ion;

/// <summary>
/// Input recording, playback and scripted input on the application builder (see
/// <see cref="InputServiceCollectionExtensions"/> for the service collection forms).
/// </summary>
public static class InputBuilderExtensions
{
	/// <summary>Records every frame's input to <paramref name="path"/> (see <see cref="InputServiceCollectionExtensions.AddInputRecording"/>).</summary>
	public static IonApplicationBuilder AddInputRecording(this IonApplicationBuilder builder, string path)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddInputRecording(path);
		return builder;
	}

	/// <summary>Replays the recording at <paramref name="path"/> (see <see cref="InputServiceCollectionExtensions.AddInputPlayback"/>).</summary>
	public static IonApplicationBuilder AddInputPlayback(this IonApplicationBuilder builder, string path)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddInputPlayback(path);
		return builder;
	}

	/// <summary>Registers the application's <see cref="ScriptedInput"/> (see <see cref="InputServiceCollectionExtensions.AddScriptedInput"/>).</summary>
	public static IonApplicationBuilder AddScriptedInput(this IonApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddScriptedInput();
		return builder;
	}
}
