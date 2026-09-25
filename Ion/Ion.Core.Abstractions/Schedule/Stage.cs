namespace Ion;

/// <summary>
/// A stage of the game loop. The values match <see cref="GameLoopStage"/> (without <see cref="GameLoopStage.None"/>), so
/// the two convert with a cast.
/// </summary>
public enum Stage
{
	/// <summary>Once before the first frame (for a scene: when the scene loads).</summary>
	Init = 1,
	/// <summary>At the start of every frame.</summary>
	First,
	/// <summary>Zero or more times per frame, with the fixed-step <see cref="GameTime"/>.</summary>
	FixedUpdate,
	/// <summary>Once per frame.</summary>
	Update,
	/// <summary>Once per frame, after Update.</summary>
	Render,
	/// <summary>At the end of every frame.</summary>
	Last,
	/// <summary>Once after the last frame (for a scene: when the scene unloads).</summary>
	Destroy,
}

/// <summary>
/// Well-known step orders. Steps in a stage run by ascending order; user steps default to <see cref="Default"/>. Engine
/// steps use two reserved bands so that user steps always run between engine setup and engine teardown, whatever the
/// registration order: <see cref="EngineSetupFirst"/> to <see cref="EngineSetupLast"/> (inclusive) before, and
/// <see cref="EngineTeardownFirst"/> to <see cref="EngineTeardownLast"/> after.
/// </summary>
public static class StageOrder
{
	/// <summary>The first order of the engine setup band.</summary>
	public const int EngineSetupFirst = -1000;

	/// <summary>The last order of the engine setup band.</summary>
	public const int EngineSetupLast = -500;

	/// <summary>The default order of a step.</summary>
	public const int Default = 0;

	/// <summary>The first order of the engine teardown band.</summary>
	public const int EngineTeardownFirst = 500;

	/// <summary>The last order of the engine teardown band.</summary>
	public const int EngineTeardownLast = 1000;

	/// <summary>Debug trace timers: a scope around every stage (Debug builds).</summary>
	public const int Trace = -1000;

	/// <summary>The window: initialization (Init) and event pumping (First).</summary>
	public const int Window = -950;

	/// <summary>Input: the per-frame input snapshot (First).</summary>
	public const int Input = -940;

	/// <summary>Asset hot reload: reloading changed assets (First), after input and before any user step.</summary>
	public const int AssetReload = -920;

	/// <summary>Graphics: device initialization (Init) and the frame scope (Render).</summary>
	public const int Graphics = -900;

	/// <summary>Audio: device initialization (Init).</summary>
	public const int Audio = -880;

	/// <summary>The sprite batch: initialization (Init) and the batch scope (Render).</summary>
	public const int SpriteBatch = -850;

	/// <summary>Coroutines: stepping the shared runner (Update).</summary>
	public const int Coroutines = -600;

	/// <summary>Scenes: the active scene's schedule, in every stage.</summary>
	public const int Scenes = -500;

	/// <summary>The window close check that turns a closed window into an exit request (Render).</summary>
	public const int WindowClose = 900;

	/// <summary>Events: stepping the frame buffers after every other Last step.</summary>
	public const int Events = 1000;
}
