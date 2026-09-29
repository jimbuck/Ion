using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// The 3D scene settings of a world: its <see cref="SceneEnvironment"/> (ambient light and skybox), stored as a singleton
/// (a component on one entity of the world), which the 3D extraction passes to the renderer with
/// <c>IMeshBatch.SetEnvironment</c> whenever it changes. Also the visibility helper of the 3D extraction.
/// </summary>
/// <remarks>
/// The 3D components are the graphics abstractions' types used as components (<c>docs/design/ion-rendering3d.md</c>,
/// section 8): <see cref="Transform"/> (local, with <see cref="GlobalTransform"/> computed by the propagation),
/// <see cref="MeshRenderer"/>, <see cref="Camera"/>, <see cref="DirectionalLight"/>, <see cref="PointLight"/> and
/// <see cref="SpotLight"/>. Entities tagged <see cref="Hidden"/> are not extracted.
/// </remarks>
public static class Scene3DExtensions
{
	private static readonly QueryDescription Environments = new QueryDescription().WithAll<SceneEnvironment>();

	/// <summary>
	/// Sets the world's <see cref="SceneEnvironment"/>. Creates the singleton entity the first time (a structural change:
	/// outside a query), and only writes the component afterwards (allowed inside a query).
	/// </summary>
	/// <returns>The entity holding the environment.</returns>
	public static Entity SetEnvironment(this World world, in SceneEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(world);
		var entity = FindEnvironment(world);
		if (entity == Entity.Null) return world.Create(environment);
		world.Set(entity, environment);
		return entity;
	}

	/// <summary>The world's <see cref="SceneEnvironment"/>, or false when none was set.</summary>
	public static bool TryGetEnvironment(this World world, out SceneEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(world);
		foreach (ref var chunk in world.Query(in Environments))
		{
			if (chunk.Count == 0) continue;
			environment = chunk.GetFirst<SceneEnvironment>();
			return true;
		}

		environment = default;
		return false;
	}

	/// <summary>
	/// Removes the world's <see cref="SceneEnvironment"/> (a structural change: outside a query). The renderer then goes
	/// back to the default environment.
	/// </summary>
	/// <returns>Whether the world had one.</returns>
	public static bool RemoveEnvironment(this World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		var entity = FindEnvironment(world);
		if (entity == Entity.Null) return false;
		// The singleton entity created by SetEnvironment holds nothing else; keep an entity the game gave other components.
		if (world.GetSignature(entity).Count == 1) world.Destroy(entity);
		else world.Remove<SceneEnvironment>(entity);
		return true;
	}

	/// <summary>
	/// Hides (adds <see cref="Hidden"/>) or shows (removes it from) <paramref name="entity"/> and, when
	/// <paramref name="recursive"/>, all its descendants (a spawned model, for example). Structural: outside a query.
	/// </summary>
	public static void SetHidden(this World world, Entity entity, bool hidden, bool recursive = true)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (!world.IsAlive(entity)) return;
		if (hidden)
		{
			if (!world.Has<Hidden>(entity)) world.Add(entity, new Hidden());
		}
		else if (world.Has<Hidden>(entity))
		{
			world.Remove<Hidden>(entity);
		}

		if (!recursive || !world.Has<Children>(entity)) return;
		// Moving an entity to another archetype copies its Children value, which shares the array: iterate a copy of the
		// struct, whose items the recursion does not change.
		var children = world.Get<Children>(entity);
		for (var i = 0; i < children.Count; i++) SetHidden(world, children[i], hidden, recursive: true);
	}

	private static Entity FindEnvironment(World world)
	{
		foreach (ref var chunk in world.Query(in Environments))
		{
			if (chunk.Count > 0) return chunk.Entity(0);
		}

		return Entity.Null;
	}
}
