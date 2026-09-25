namespace Ion.Extensions.Scenes;


public record struct ChangeSceneEvent(int NextSceneId);

public static class EventEmitterExtensions
{
	public static void EmitChangeScene(this IEventEmitter eventEmitter, int nextSceneId) => eventEmitter.Emit(new ChangeSceneEvent(nextSceneId));

	/// <summary>Asks the scene system to load the scene identified by an enum value.</summary>
	public static void EmitChangeScene<TScene>(this IEventEmitter eventEmitter, TScene nextSceneId) where TScene : struct, Enum =>
		eventEmitter.Emit(new ChangeSceneEvent(Convert.ToInt32(nextSceneId, System.Globalization.CultureInfo.InvariantCulture)));
}
