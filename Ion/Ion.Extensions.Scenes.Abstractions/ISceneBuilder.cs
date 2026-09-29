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
}
