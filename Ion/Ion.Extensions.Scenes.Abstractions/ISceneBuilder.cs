using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Scenes;

/// <summary>
/// Configures one scene's schedule. Systems (<c>UseSystem</c>) and function steps (<c>scene.Update(...)</c>, ...) follow
/// the same ordering rules as the application's; the scene's schedule runs inside the application's scene system step
/// (order <see cref="StageOrder.Scenes"/>) in every stage.
/// </summary>
public interface ISceneBuilder : IScheduleBuilder
{
	/// <summary>The scene id.</summary>
	int SceneId { get; }

	/// <summary>The application configuration.</summary>
	IConfiguration Configuration { get; }

	/// <summary>The scene's services (a scope of the application's).</summary>
	new IServiceProvider Services { get; }

	/// <summary>Adds a legacy middleware to the scene's Init stage (warning ION010); prefer <c>scene.Init(...)</c> or a system.</summary>
	ISceneBuilder UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's First stage (warning ION010); prefer <c>scene.First(...)</c> or a system.</summary>
	ISceneBuilder UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's FixedUpdate stage (warning ION010); prefer <c>scene.FixedUpdate(...)</c> or a system.</summary>
	ISceneBuilder UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's Update stage (warning ION010); prefer <c>scene.Update(...)</c> or a system.</summary>
	ISceneBuilder UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's Render stage (warning ION010); prefer <c>scene.Render(...)</c> or a system.</summary>
	ISceneBuilder UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's Last stage (warning ION010); prefer <c>scene.Last(...)</c> or a system.</summary>
	ISceneBuilder UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>Adds a legacy middleware to the scene's Destroy stage (warning ION010); prefer <c>scene.Destroy(...)</c> or a system.</summary>
	ISceneBuilder UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);
}
