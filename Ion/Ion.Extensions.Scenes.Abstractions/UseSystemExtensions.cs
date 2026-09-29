using System.Diagnostics.CodeAnalysis;

namespace Ion.Extensions.Scenes;

/// <summary>
/// Registers systems on a scene's schedule.
/// </summary>
public static class UseSystemExtensions
{
	/// <summary>
	/// Adds a system to the scene's schedule (see <c>UseSystem</c> on the application). It is resolved from the scene's
	/// scope when the scene loads, so scoped systems get one instance per scene load.
	/// </summary>
	public static ISceneBuilder UseSystem<[DynamicallyAccessedMembers(SystemAccessibility.Members)] TSystem>(this ISceneBuilder scene)
	{
		return UseSystem(scene, typeof(TSystem));
	}

	/// <inheritdoc cref="UseSystem{TSystem}(ISceneBuilder)"/>
	public static ISceneBuilder UseSystem(this ISceneBuilder scene, [DynamicallyAccessedMembers(SystemAccessibility.Members)] Type systemType)
	{
		ArgumentNullException.ThrowIfNull(scene);
		scene.Schedule.AddSystem(systemType, systemType);
		return scene;
	}
}
