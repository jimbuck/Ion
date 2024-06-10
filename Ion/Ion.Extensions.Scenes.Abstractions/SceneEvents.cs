namespace Ion.Extensions.Scenes;


public record struct ChangeSceneEvent(int NextSceneId);

public static class EventEmitterExtensions
{
	public static void EmitChangeScene(this IEventEmitter eventEmitter, int nextSceneId) => eventEmitter.Emit(new ChangeSceneEvent(nextSceneId));
}
