namespace Ion.Extensions.Scenes;

/// <summary>
/// Asks the scene system to load the scene <see cref="NextSceneId"/>. Without a <see cref="Transition"/> the change
/// happens at the start of the next frame; with one, the transition's out phase starts then, the old scene keeps running
/// until it ends, and the new scene loads before the in phase.
/// </summary>
/// <param name="NextSceneId">The scene to load (any <c>int</c>, 0 included).</param>
/// <param name="Transition">How the change is animated (<see cref="SceneTransition.None"/> by default).</param>
public record struct ChangeSceneEvent(int NextSceneId, SceneTransition Transition = default);

/// <summary>Scene helpers for the event bus.</summary>
public static class SceneEventExtensions
{
	/// <summary>Asks the scene system to load the scene <paramref name="nextSceneId"/>.</summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene(this IEvents events, int nextSceneId)
	{
		ArgumentNullException.ThrowIfNull(events);
		events.Emit(new ChangeSceneEvent(nextSceneId));
	}

	/// <summary>
	/// Asks the scene system to load the scene <paramref name="nextSceneId"/> with <paramref name="transition"/> (for
	/// example <c>SceneTransition.Fade(0.5f)</c>).
	/// </summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene(this IEvents events, int nextSceneId, SceneTransition transition)
	{
		ArgumentNullException.ThrowIfNull(events);
		events.Emit(new ChangeSceneEvent(nextSceneId, transition));
	}

	/// <summary>Asks the scene system to load the scene identified by an enum value.</summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene<TScene>(this IEvents events, TScene nextSceneId) where TScene : struct, Enum =>
		events.EmitChangeScene(Convert.ToInt32(nextSceneId, System.Globalization.CultureInfo.InvariantCulture));

	/// <summary>Asks the scene system to load the scene identified by an enum value, with <paramref name="transition"/>.</summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene<TScene>(this IEvents events, TScene nextSceneId, SceneTransition transition) where TScene : struct, Enum =>
		events.EmitChangeScene(Convert.ToInt32(nextSceneId, System.Globalization.CultureInfo.InvariantCulture), transition);
}
