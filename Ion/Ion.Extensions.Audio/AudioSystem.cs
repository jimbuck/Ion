namespace Ion.Extensions.Audio;

/// <summary>
/// Runs the audio manager: starts the output in Init, flushes the frame's commands to the audio thread in Last (and
/// advances the null output by the game clock), and stops the output in Destroy. All at order
/// <see cref="StageOrder.Audio"/> (Destroy at its mirror in the teardown band).
/// </summary>
/// <remarks>
/// The Last step runs in the engine setup band, before user Last steps: commands issued in Init, First, FixedUpdate,
/// Update and Render reach the audio thread the same frame, commands issued in a user Last step the next frame.
/// </remarks>
internal sealed class AudioSystem(AudioManager audio)
{
	/// <summary>The order of the Last step that flushes commands.</summary>
	public const int FlushOrder = StageOrder.Audio;

	[Init(Order = StageOrder.Audio)]
	public void Init() => audio.Start();

	[Last(Order = FlushOrder)]
	public void Last(GameTime dt) => audio.Update(dt.Elapsed);

	[Destroy(Order = -StageOrder.Audio)]
	public void Destroy() => audio.Stop();
}
