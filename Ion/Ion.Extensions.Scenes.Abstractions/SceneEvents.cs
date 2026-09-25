namespace Ion.Extensions.Scenes;

/// <summary>Asks the scene system to load the scene <see cref="NextSceneId"/> at the start of the next frame.</summary>
public record struct ChangeSceneEvent(int NextSceneId);

/// <summary>Scene helpers for the event bus.</summary>
public static class EventEmitterExtensions
{
	/// <summary>Asks the scene system to load the scene <paramref name="nextSceneId"/>.</summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene(this IEvents events, int nextSceneId)
	{
		ArgumentNullException.ThrowIfNull(events);
		events.Emit(new ChangeSceneEvent(nextSceneId));
	}

	/// <summary>Asks the scene system to load the scene identified by an enum value.</summary>
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene<TScene>(this IEvents events, TScene nextSceneId) where TScene : struct, Enum =>
		events.EmitChangeScene(Convert.ToInt32(nextSceneId, System.Globalization.CultureInfo.InvariantCulture));

	/// <inheritdoc cref="EmitChangeScene(IEvents, int)"/>
	[Obsolete("IEventEmitter is an adapter over IEvents and will be removed in the next release. Inject IEvents and call EmitChangeScene on it.")]
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene(this IEventEmitter eventEmitter, int nextSceneId)
	{
		ArgumentNullException.ThrowIfNull(eventEmitter);
		eventEmitter.Emit(new ChangeSceneEvent(nextSceneId));
	}

	/// <inheritdoc cref="EmitChangeScene{TScene}(IEvents, TScene)"/>
	[Obsolete("IEventEmitter is an adapter over IEvents and will be removed in the next release. Inject IEvents and call EmitChangeScene on it.")]
	[EmitsEvent(typeof(ChangeSceneEvent))]
	public static void EmitChangeScene<TScene>(this IEventEmitter eventEmitter, TScene nextSceneId) where TScene : struct, Enum =>
		eventEmitter.EmitChangeScene(Convert.ToInt32(nextSceneId, System.Globalization.CultureInfo.InvariantCulture));
}
