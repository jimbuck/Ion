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

	/// <summary>
	/// Reserved: the 0.2 trace timer scope around every stage. The game loop now records a span per stage itself.
	/// </summary>
	public const int Trace = -1000;

	/// <summary>The window: initialization (Init) and event pumping (First).</summary>
	public const int Window = -950;

	/// <summary>Input: the per-frame input snapshot (First).</summary>
	public const int Input = -940;

	/// <summary>Metrics: the trace capture key (First), after input.</summary>
	public const int Metrics = -930;

	/// <summary>Asset hot reload: reloading changed assets (First), after input and before any user step.</summary>
	public const int AssetReload = -920;

	/// <summary>Graphics: device initialization (Init) and the frame scope (Render).</summary>
	public const int Graphics = -900;

	/// <summary>Audio: device initialization (Init).</summary>
	public const int Audio = -880;

	/// <summary>
	/// Networking (<c>Ion.Extensions.Networking</c>): starting the transport (Init); draining it, decoding packets into the
	/// typed message channels and applying received snapshots to the world (First), before any game step reads them; the
	/// tick scope around every fixed step (FixedUpdate: the tick is incremented when it opens, and when it closes, after
	/// every other fixed step including the ECS command playback, the replicated components are captured into the snapshot
	/// ring, in a <c>finally</c> so a throwing step never leaves a tick without a snapshot); and the interpolation of remote
	/// entities (Render, inside the graphics frame scope and before the extraction). Prediction and reconciliation run at
	/// <c>Network + 10</c> (FixedUpdate and First). The send step is <see cref="NetworkSend"/>.
	/// </summary>
	public const int Network = -870;

	/// <summary>
	/// Networking (Last and Destroy): delta-encoding the newest snapshot for each peer, packing the queued messages and
	/// flushing the transport (Last), and disconnecting the peers (Destroy). In the teardown band, after the game's own
	/// Last steps and before the ECS playback (<see cref="Ecs"/>) and event stepping (<see cref="Events"/>).
	/// </summary>
	public const int NetworkSend = 870;

	/// <summary>
	/// The 3D renderer: initialization (Init) and the frame scope (Render). Its scope opens before the sprite batch's and
	/// closes after it, so when it closes every 3D and 2D submission of the frame is in and its render graph draws the
	/// 3D passes and then the 2D overlay.
	/// </summary>
	public const int Rendering3D = -860;

	/// <summary>The sprite batch: initialization (Init) and the batch scope (Render).</summary>
	public const int SpriteBatch = -850;

	/// <summary>
	/// Physics (FixedUpdate): the 2D and 3D physics steps push the entities' changed transforms and bodies into the physics
	/// world, step it by the fixed delta, pull the simulated transforms back and emit the collision and trigger events. In
	/// the engine setup band, so every fixed step of the game (order <see cref="Default"/>) sees the result of the physics
	/// step it follows and its changes are simulated by the next one (the order of Unity's and Bevy's fixed schedules),
	/// and before the scenes (<see cref="Scenes"/>), whose own physics steps use the same order inside the scene's
	/// schedule.
	/// </summary>
	public const int Physics = -700;

	/// <summary>Coroutines: stepping the shared runner (Update).</summary>
	public const int Coroutines = -600;

	/// <summary>
	/// The UI frame scope (Update): <c>Ion.Extensions.UI</c> opens its frame here (tree commands applied, input read
	/// against the previous frame's hit-test tree) and closes it at the end of Update (layout, hit-test tree and inspectable
	/// tree rebuilt). In the setup band after <see cref="Coroutines"/> and before <see cref="Scenes"/>, so scene steps and
	/// the game's own Update steps can both build UI.
	/// </summary>
	public const int UiFrame = -550;

	/// <summary>Scenes: the active scene's schedule, in every stage.</summary>
	public const int Scenes = -500;

	/// <summary>
	/// ECS transform propagation (<c>Transform2D</c>/<c>Transform</c> and <c>Parent</c> to the global transforms): in Last,
	/// after the frame's gameplay, and again in Render before <see cref="Extract"/> so a frame draws what its Update did.
	/// At the start of the user band: after the scene's steps (<see cref="Scenes"/>), before the game's own steps at
	/// <see cref="Default"/>.
	/// </summary>
	public const int TransformPropagation = -400;

	/// <summary>
	/// ECS render extraction (Render): sprites and cameras copied from the world into the renderer. At -300, the plan's
	/// "user band minus 300": inside the sprite batch scope (<see cref="SpriteBatch"/>), after <see cref="TransformPropagation"/>,
	/// and before the game's own Render steps at <see cref="Default"/> (which therefore draw over the extracted sprites).
	/// </summary>
	public const int Extract = -300;

	/// <summary>ECS sprite animation (Update): after the game's Update steps at <see cref="Default"/>.</summary>
	public const int SpriteAnimation = 400;

	/// <summary>
	/// UI drawing (Render): <c>Ion.Extensions.UI</c> submits the frame's widgets to the sprite batch here, inside the sprite
	/// batch scope (<see cref="SpriteBatch"/>), after the game's own Render steps at <see cref="Default"/>, and
	/// before <see cref="MetricsOverlay"/> (so the overlay stays on top of the UI).
	/// </summary>
	public const int Ui = 700;

	/// <summary>
	/// The physics debug drawing (Render): the 2D colliders and joints drawn through the sprite batch, inside its scope
	/// (<see cref="SpriteBatch"/>) and after the extraction and the game's own drawing, so the outlines are on top; the 3D
	/// colliders submitted to the 3D renderer, whose scope (<see cref="Rendering3D"/>) is open too. Runs before <see cref="Ui"/> so the UI stays on top.
	/// </summary>
	public const int PhysicsDebugDraw = 650;

	/// <summary>The metrics overlay (Render), inside the sprite batch scope and after the game's own drawing.</summary>
	public const int MetricsOverlay = 800;

	/// <summary>The window close check that turns a closed window into an exit request (Render).</summary>
	public const int WindowClose = 900;

	/// <summary>
	/// ECS: the command buffer (<c>Commands</c>) is played back at the end of every stage, after every other step of the
	/// stage but the event stepping (<see cref="Events"/>), so structural changes recorded during a stage are visible to
	/// the next one and never invalidate a running query.
	/// </summary>
	public const int Ecs = 950;

	/// <summary>
	/// The web server module (Last): the requests and WebSocket messages queued by <c>Ion.Extensions.Web</c>'s server
	/// threads are handed to the game's <c>[Http]</c> and <c>[WebSocket]</c> methods on the game thread here, at the end of
	/// the frame. After every gameplay, render and ECS command step (<see cref="Ecs"/>), so a handler sees the finished
	/// frame and its changes are visible from the next frame's First stage on (input it injects through the scripted path
	/// is applied then too); before the remote protocol (<see cref="Remote"/>), so a remote read in the same frame sees
	/// them, and before the event stepping (<see cref="Events"/>), so events a handler emits are read in the next frame.
	/// </summary>
	public const int Web = 960;

	/// <summary>
	/// The remote inspection protocol (Last): requests queued by the transports are applied on the game thread here, at the
	/// end of the frame. After every gameplay, render and ECS command step of the frame (so a read sees the finished frame,
	/// a screenshot the rendered image, and a mutation is visible from the next frame's First stage on), and before the
	/// event stepping (<see cref="Events"/>), so <c>events.tail</c> still sees the frame's events.
	/// </summary>
	public const int Remote = 970;

	/// <summary>Events: stepping the frame buffers after every other Last step.</summary>
	public const int Events = 1000;
}
