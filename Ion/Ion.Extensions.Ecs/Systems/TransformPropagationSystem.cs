using System.Numerics;
using System.Runtime.CompilerServices;

using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Computes <see cref="GlobalTransform2D"/> from <see cref="Transform2D"/> and <see cref="GlobalTransform"/> from
/// <see cref="Transform"/>, down the <see cref="Parent"/>/<see cref="Children"/> hierarchy, and the <see cref="Aabb2D"/> of
/// sprites.
/// </summary>
/// <remarks>
/// <para>
/// Runs at <see cref="StageOrder.TransformPropagation"/> in Last (after the frame's gameplay, so the next frame reads fresh
/// world transforms) and again in Render before the extraction (<see cref="StageOrder.Extract"/>), so a frame draws what
/// its own Update did and entities created this frame are not drawn at the origin; the second pass only recomputes what
/// changed since the first. The Render pass also updates <see cref="Aabb2D"/>.
/// </para>
/// <para>
/// It first adds the missing <see cref="GlobalTransform2D"/>, <see cref="GlobalTransform"/> and (for sprites)
/// <see cref="Aabb2D"/> components with Arch's bulk operations, then walks the tree: roots (entities without a
/// <see cref="Parent"/>) in query order, children in their <see cref="Children"/> order, so the order is deterministic.
/// An entity is recomputed only when its local transform differs from the one its global transform was computed from,
/// or its parent was recomputed (a dirty-tree walk without change flags). Children need a transform of the same kind as
/// their parent; an entity whose parent has none is not reached.
/// </para>
/// </remarks>
public sealed class TransformPropagationSystem(World world)
{
	private static readonly QueryDescription Missing2D = new QueryDescription().WithAll<Transform2D>().WithNone<GlobalTransform2D>();
	private static readonly QueryDescription Missing3D = new QueryDescription().WithAll<Transform>().WithNone<GlobalTransform>();
	private static readonly QueryDescription MissingBounds = new QueryDescription().WithAll<Sprite, GlobalTransform2D>().WithNone<Aabb2D>();
	private static readonly QueryDescription Roots2D = new QueryDescription().WithAll<Transform2D, GlobalTransform2D>().WithNone<Parent>();
	private static readonly QueryDescription Roots3D = new QueryDescription().WithAll<Transform, GlobalTransform>().WithNone<Parent>();
	private static readonly QueryDescription Bounds = new QueryDescription().WithAll<Sprite, GlobalTransform2D, Aabb2D>();

	private int _updated;
	private int _visited;

	/// <summary>The world.</summary>
	public World World => world;

	/// <summary>The number of global transforms recomputed by the last <see cref="Propagate()"/>.</summary>
	public int LastUpdated { get; private set; }

	/// <summary>The number of entities visited by the last <see cref="Propagate()"/>.</summary>
	public int LastVisited { get; private set; }

	/// <summary>Propagates after the frame's gameplay (Last).</summary>
	[Last(Order = StageOrder.TransformPropagation)]
	public void PropagateLast(GameTime dt) => Propagate();

	/// <summary>Propagates what changed since Last and updates the sprite bounds, before the extraction (Render).</summary>
	[Render(Order = StageOrder.TransformPropagation)]
	public void PropagateBeforeRender(GameTime dt)
	{
		Propagate();
		UpdateBounds();
	}

	/// <summary>Adds missing global transforms and recomputes the ones that changed.</summary>
	public void Propagate()
	{
		if (world.CountEntities(in Missing2D) > 0) world.Add(in Missing2D, new GlobalTransform2D());
		if (world.CountEntities(in Missing3D) > 0) world.Add(in Missing3D, new GlobalTransform());

		_updated = 0;
		_visited = 0;
		Propagate2D();
		Propagate3D();
		LastUpdated = _updated;
		LastVisited = _visited;
	}

	/// <summary>Adds missing <see cref="Aabb2D"/> components to sprites and computes every sprite's bounds.</summary>
	public void UpdateBounds()
	{
		if (world.CountEntities(in MissingBounds) > 0) world.Add(in MissingBounds, new Aabb2D());

		foreach (ref var chunk in world.Query(in Bounds))
		{
			var sprites = chunk.GetSpan<Sprite>();
			var globals = chunk.GetSpan<GlobalTransform2D>();
			var bounds = chunk.GetSpan<Aabb2D>();
			for (var i = 0; i < chunk.Count; i++)
			{
				ref readonly var sprite = ref sprites[i];
				ref readonly var global = ref globals[i];
				bounds[i] = BoundsOf(sprite, global);
			}
		}
	}

	/// <summary>The world bounds of <paramref name="sprite"/> drawn at <paramref name="global"/>.</summary>
	public static Aabb2D BoundsOf(in Sprite sprite, in GlobalTransform2D global) =>
		Aabb2D.FromSprite(global.Position, global.Rotation, sprite.ResolveSize() * global.Scale, sprite.Origin);

	private void Propagate2D()
	{
		foreach (ref var chunk in world.Query(in Roots2D))
		{
			var locals = chunk.GetSpan<Transform2D>();
			var globals = chunk.GetSpan<GlobalTransform2D>();
			var hasChildren = chunk.Has<Children>();
			var children = hasChildren ? chunk.GetSpan<Children>() : default;
			var count = chunk.Count;
			_visited += count;

			for (var i = 0; i < count; i++)
			{
				ref readonly var local = ref locals[i];
				ref var global = ref globals[i];

				if (global.Version == 0 || !Same(local, global.Local))
				{
					global.Matrix = local.ToMatrix();
					global.Position = local.Position;
					global.Rotation = local.Rotation;
					global.Scale = local.Scale;
					global.Local = local;
					global.Version = NextVersion(global.Version);
					_updated++;
				}

				if (hasChildren && children[i].Count > 0) PropagateChildren2D(global, children[i]);
			}
		}
	}

	private void PropagateChildren2D(in GlobalTransform2D parent, in Children children)
	{
		foreach (var child in children.AsSpan())
		{
			// One entity lookup: its chunk and index, then the components by index.
			ref var data = ref world.IsAlive(child, out var alive);
			if (!alive) continue;
			ref var chunk = ref data.Archetype.GetChunk(data.Slot.ChunkIndex);
			if (!chunk.Has<Transform2D>() || !chunk.Has<GlobalTransform2D>()) continue;
			var index = data.Slot.Index;
			ref var local = ref Unsafe.Add(ref chunk.GetFirst<Transform2D>(), index);
			ref var global = ref Unsafe.Add(ref chunk.GetFirst<GlobalTransform2D>(), index);
			_visited++;

			if (global.Version == 0 || global.ParentVersion != parent.Version || !Same(local, global.Local))
			{
				var matrix = local.ToMatrix() * parent.Matrix;
				global.Matrix = matrix;
				global.Position = new Vector2(matrix.M31, matrix.M32);
				global.Rotation = parent.Rotation + local.Rotation;
				global.Scale = parent.Scale * local.Scale;
				global.Local = local;
				global.ParentVersion = parent.Version;
				global.Version = NextVersion(global.Version);
				_updated++;
			}

			if (chunk.Has<Children>())
			{
				ref var grandchildren = ref Unsafe.Add(ref chunk.GetFirst<Children>(), index);
				if (grandchildren.Count > 0) PropagateChildren2D(global, grandchildren);
			}
		}
	}

	private void Propagate3D()
	{
		foreach (ref var chunk in world.Query(in Roots3D))
		{
			var locals = chunk.GetSpan<Transform>();
			var globals = chunk.GetSpan<GlobalTransform>();
			var hasChildren = chunk.Has<Children>();
			var children = hasChildren ? chunk.GetSpan<Children>() : default;
			var count = chunk.Count;
			_visited += count;

			for (var i = 0; i < count; i++)
			{
				ref readonly var local = ref locals[i];
				ref var global = ref globals[i];

				if (global.Version == 0 || !Same(local, global.Local))
				{
					global.Matrix = local.ToMatrix();
					global.Local = local;
					global.Version = NextVersion(global.Version);
					_updated++;
				}

				if (hasChildren && children[i].Count > 0) PropagateChildren3D(global, children[i]);
			}
		}
	}

	private void PropagateChildren3D(in GlobalTransform parent, in Children children)
	{
		foreach (var child in children.AsSpan())
		{
			ref var data = ref world.IsAlive(child, out var alive);
			if (!alive) continue;
			ref var chunk = ref data.Archetype.GetChunk(data.Slot.ChunkIndex);
			if (!chunk.Has<Transform>() || !chunk.Has<GlobalTransform>()) continue;
			var index = data.Slot.Index;
			ref var local = ref Unsafe.Add(ref chunk.GetFirst<Transform>(), index);
			ref var global = ref Unsafe.Add(ref chunk.GetFirst<GlobalTransform>(), index);
			_visited++;

			if (global.Version == 0 || global.ParentVersion != parent.Version || !Same(local, global.Local))
			{
				global.Matrix = local.ToMatrix() * parent.Matrix;
				global.Local = local;
				global.ParentVersion = parent.Version;
				global.Version = NextVersion(global.Version);
				_updated++;
			}

			if (chunk.Has<Children>())
			{
				ref var grandchildren = ref Unsafe.Add(ref chunk.GetFirst<Children>(), index);
				if (grandchildren.Count > 0) PropagateChildren3D(global, grandchildren);
			}
		}
	}

	private static bool Same(in Transform2D a, in Transform2D b) => a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale;

	private static bool Same(in Transform a, in Transform b) => a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale;

	private static uint NextVersion(uint version) => version == uint.MaxValue ? 1 : version + 1;
}
