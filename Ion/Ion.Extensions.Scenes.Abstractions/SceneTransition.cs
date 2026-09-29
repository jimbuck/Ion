namespace Ion.Extensions.Scenes;

/// <summary>How a scene change is drawn while its <see cref="SceneTransition"/> runs.</summary>
public enum TransitionKind : byte
{
	/// <summary>No transition: the scene changes at the start of the next frame.</summary>
	None = 0,

	/// <summary>
	/// A fade to a color and back. The engine's <c>SceneFadeSystem</c> (added by <c>UseIon()</c>, or <c>UseSceneFade()</c>)
	/// draws it over the frame with the 2D renderer.
	/// </summary>
	Fade = 1,

	/// <summary>
	/// A transition the game draws itself: the scene system runs its phases and exposes its progress
	/// (<c>SceneSystem.Transition</c>), and the game's own Render step draws it, telling its transitions apart by
	/// <see cref="SceneTransition.Style"/>.
	/// </summary>
	Custom = 2,
}

/// <summary>The phase of a running scene transition.</summary>
public enum TransitionPhase : byte
{
	/// <summary>No transition runs.</summary>
	None = 0,

	/// <summary>The old scene is being covered; it keeps running until the phase ends, then the new scene loads.</summary>
	Out = 1,

	/// <summary>The new scene is loaded and running, and is being uncovered.</summary>
	In = 2,
}

/// <summary>
/// How a scene change is animated: a <see cref="Kind"/>, the duration of the out phase (the old scene is covered, and
/// keeps running) and of the in phase (the new scene, loaded when the out phase ends, is uncovered), in seconds of game
/// time. An unmanaged value, so it travels in <see cref="ChangeSceneEvent"/>.
/// </summary>
/// <param name="Kind">How the transition is drawn.</param>
/// <param name="OutDuration">The seconds the out phase lasts (0 or less: the old scene unloads at once).</param>
/// <param name="InDuration">The seconds the in phase lasts (0 or less: the new scene shows at once).</param>
/// <param name="Style">A value of the game's for <see cref="TransitionKind.Custom"/> transitions (which one to draw); unused by the built-in kinds.</param>
public readonly record struct SceneTransition(TransitionKind Kind, float OutDuration, float InDuration, int Style = 0)
{
	/// <summary>No transition: the scene changes at the start of the next frame.</summary>
	public static SceneTransition None => default;

	/// <summary>Whether this is no transition at all (<see cref="TransitionKind.None"/>, or both durations 0).</summary>
	public bool IsNone => Kind == TransitionKind.None || (!(OutDuration > 0f) && !(InDuration > 0f));

	/// <summary>A fade out and back in, each taking half of <paramref name="duration"/> seconds.</summary>
	public static SceneTransition Fade(float duration) => Fade(duration / 2f, duration / 2f);

	/// <summary>A fade out over <paramref name="outDuration"/> seconds and back in over <paramref name="inDuration"/> seconds.</summary>
	public static SceneTransition Fade(float outDuration, float inDuration) => new(TransitionKind.Fade, outDuration, inDuration);

	/// <summary>A transition the game draws itself (see <see cref="TransitionKind.Custom"/>), identified by <paramref name="style"/>.</summary>
	public static SceneTransition Custom(int style, float outDuration, float inDuration) => new(TransitionKind.Custom, outDuration, inDuration, style);
}

/// <summary>
/// The state of the running scene transition, for the code that draws it: the <see cref="Transition"/>, its
/// <see cref="Phase"/>, the <see cref="Progress"/> through that phase and the resulting <see cref="Coverage"/>.
/// </summary>
/// <param name="Transition">The transition that runs (<see cref="SceneTransition.None"/> when none does).</param>
/// <param name="Phase">The phase it is in.</param>
/// <param name="Progress">How far through <paramref name="Phase"/> it is, from 0 to 1 (0 when no transition runs).</param>
public readonly record struct SceneTransitionState(SceneTransition Transition, TransitionPhase Phase, float Progress)
{
	/// <summary>Whether a transition runs.</summary>
	public bool IsActive => Phase != TransitionPhase.None;

	/// <summary>
	/// How much of the frame the transition covers, from 0 (the scene is fully visible) to 1 (fully covered): it rises
	/// from 0 to 1 during the out phase and falls back to 0 during the in phase. A fade draws its color at this opacity.
	/// </summary>
	public float Coverage => Phase switch
	{
		TransitionPhase.Out => Progress,
		TransitionPhase.In => 1f - Progress,
		_ => 0f,
	};
}
