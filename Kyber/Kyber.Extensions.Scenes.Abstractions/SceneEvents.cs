namespace Kyber.Extensions.Scenes;

public record struct ChangeSceneEvent(string NextScene);

public record struct SceneChangedEvent(string CurrentScene, string PreviousScene);
