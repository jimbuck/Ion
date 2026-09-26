using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>How <see cref="ModelSpawnExtensions.SpawnModel(World, IModel, in Transform, ModelSpawnOptions?)"/> sets up the entities of a model.</summary>
/// <remarks>Create it with <c>new()</c> (or <see cref="Default"/>): <c>default(ModelSpawnOptions)</c> casts no shadows and has an empty layer mask.</remarks>
public record struct ModelSpawnOptions
{
	/// <summary>Whether the model's mesh renderers cast shadows (default true).</summary>
	public bool CastShadows;

	/// <summary>Whether the model's mesh renderers receive shadows (default true).</summary>
	public bool ReceiveShadows;

	/// <summary>The layers of the model's mesh renderers (default 1), matched against <see cref="Camera.CullingMask"/>.</summary>
	public uint LayerMask;

	/// <summary>Whether the root gets the model's name and each node entity its node's name as an <see cref="EntityName"/> (default true).</summary>
	public bool Names;

	/// <summary>The defaults: shadows cast and received, layer 1, names.</summary>
	public ModelSpawnOptions()
	{
		CastShadows = true;
		ReceiveShadows = true;
		LayerMask = 1;
		Names = true;
	}

	/// <summary>The defaults.</summary>
	public static ModelSpawnOptions Default => new();
}

/// <summary>A model recorded by <see cref="ModelSpawnExtensions.SpawnModel(Commands, IModel, in Transform, ModelSpawnOptions?)"/> for an entity the same commands create; instantiated at playback.</summary>
internal readonly record struct PendingModel(IModel Model, ModelSpawnOptions Options);

/// <summary>
/// Spawns an <see cref="IModel"/> (a loaded glTF, say) into a world as an entity hierarchy, the mapping of
/// <c>docs/design/ion-rendering3d.md</c> section 8: a root entity with the given <see cref="Transform"/>, then one entity per
/// node with the node's local transform, parented as the nodes are (the model's root nodes under the root entity). A node
/// with one primitive gets its <see cref="MeshRenderer"/> on its own entity; a node with several gets one child entity
/// per primitive (identity transform, one <see cref="MeshRenderer"/> each), since an entity holds one mesh renderer.
/// </summary>
/// <remarks>
/// The entities are ordinary ECS entities: move the root to move the model, tag it <see cref="Hidden"/> with
/// <see cref="Scene3DExtensions.SetHidden"/> (recursive) to hide it, and destroy it with
/// <see cref="HierarchyExtensions.DestroyRecursive"/>. The model's meshes and materials stay owned by the model asset.
/// </remarks>
public static class ModelSpawnExtensions
{
	/// <summary>Spawns <paramref name="model"/> at the origin (a structural change: outside a query, or use the <see cref="Commands"/> overload).</summary>
	/// <returns>The root entity.</returns>
	public static Entity SpawnModel(this World world, IModel model) => SpawnModel(world, model, Transform.Identity);

	/// <summary>Spawns <paramref name="model"/> with its root at <paramref name="transform"/> (a structural change: outside a query, or use the <see cref="Commands"/> overload).</summary>
	/// <returns>The root entity.</returns>
	public static Entity SpawnModel(this World world, IModel model, in Transform transform, ModelSpawnOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(model);
		EcsComponents.RegisterBuiltIns();
		var root = world.Create(transform);
		Instantiate(world, root, model, options ?? ModelSpawnOptions.Default);
		return root;
	}

	/// <summary>
	/// Records spawning <paramref name="model"/> with its root at <paramref name="transform"/>. Returns the root entity, a
	/// placeholder until playback (use it with the same commands, for example <see cref="Commands.SetParent"/> or
	/// <see cref="Commands.Add{T}"/>); the node entities are created when the commands are played back.
	/// </summary>
	public static Entity SpawnModel(this Commands commands, IModel model, in Transform transform, ModelSpawnOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(commands);
		ArgumentNullException.ThrowIfNull(model);
		return commands.Create(transform, new PendingModel(model, options ?? ModelSpawnOptions.Default));
	}

	/// <summary>Records spawning <paramref name="model"/> at the origin; see <see cref="SpawnModel(Commands, IModel, in Transform, ModelSpawnOptions?)"/>.</summary>
	public static Entity SpawnModel(this Commands commands, IModel model) => SpawnModel(commands, model, Transform.Identity);

	/// <summary>Creates the node entities of <paramref name="model"/> under <paramref name="root"/>.</summary>
	internal static void Instantiate(World world, Entity root, IModel model, in ModelSpawnOptions options)
	{
		if (options.Names && !string.IsNullOrEmpty(model.Name) && !world.Has<EntityName>(root)) world.Add(root, new EntityName(model.Name));

		var nodes = model.Nodes;
		var spawned = new bool[nodes.Count];
		foreach (var index in model.RootNodes) Spawn(world, root, nodes, spawned, index, options);
	}

	private static void Spawn(World world, Entity parent, IReadOnlyList<ModelNode> nodes, bool[] spawned, int index, in ModelSpawnOptions options)
	{
		if ((uint)index >= (uint)nodes.Count || spawned[index]) return;
		spawned[index] = true;
		var node = nodes[index];
		var entity = world.Create(node.LocalTransform);
		if (options.Names && !string.IsNullOrEmpty(node.Name)) world.Add(entity, new EntityName(node.Name));
		world.SetParent(entity, parent);

		var primitives = node.Primitives;
		if (primitives.Length == 1)
		{
			world.Add(entity, Renderer(primitives[0], options));
		}
		else
		{
			foreach (var primitive in primitives)
			{
				var child = world.Create(Transform.Identity, Renderer(primitive, options));
				world.SetParent(child, entity);
			}
		}

		foreach (var child in node.Children) Spawn(world, entity, nodes, spawned, child, options);
	}

	private static MeshRenderer Renderer(in ModelPrimitive primitive, in ModelSpawnOptions options) => new(primitive.Mesh, primitive.Material)
	{
		CastShadows = options.CastShadows,
		ReceiveShadows = options.ReceiveShadows,
		LayerMask = options.LayerMask,
	};
}
