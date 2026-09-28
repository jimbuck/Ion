namespace Ion.Extensions.Scenes;

/// <summary>The scene that is active right now (a singleton, updated when a scene loads).</summary>
public interface ICurrentScene
{
	/// <summary>True when no scene is loaded (the opposite of <see cref="HasScene"/>).</summary>
	bool IsRoot { get; }

	/// <summary>True once a scene is loaded. Every <c>int</c> is a valid scene id, 0 included, so check this rather than the id.</summary>
	bool HasScene { get; }

	/// <summary>The id of the active scene. Only meaningful when <see cref="HasScene"/> is true (it is 0 before the first scene loads).</summary>
	int SceneId { get; }
}

/// <summary>The default <see cref="ICurrentScene"/>, set by the scene system when a scene loads.</summary>
public sealed class CurrentScene : ICurrentScene
{
	/// <summary>The value of <see cref="SceneId"/> before any scene loads. It is also a valid scene id: use <see cref="HasScene"/> to tell them apart.</summary>
	[Obsolete("0 is a valid scene id; use ICurrentScene.HasScene (or IsRoot) to tell whether a scene is loaded.")]
	public static readonly int Root = 0;

	/// <inheritdoc/>
	public int SceneId { get; private set; }

	/// <inheritdoc/>
	public bool HasScene { get; private set; }

	/// <inheritdoc/>
	public bool IsRoot => !HasScene;

	internal void Set(int sceneId)
	{
		SceneId = sceneId;
		HasScene = true;
	}

	/// <inheritdoc/>
	public override string ToString() => HasScene ? $"Scene{SceneId}" : "NoScene";
}
