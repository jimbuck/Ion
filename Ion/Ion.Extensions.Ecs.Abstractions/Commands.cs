using Arch.Buffer;
using Arch.Core;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Structural changes to a <see cref="World"/> recorded now and applied later: the ECS module plays them back at the
/// end of every stage (<c>StageOrder.Ecs</c>), so creating and destroying entities and adding and removing components
/// never invalidates a running query. Inject it into a system (or a <c>[Query]</c> method) next to its
/// <see cref="World"/>: each world (the root one and each scene's) has its own.
/// </summary>
/// <remarks>
/// Built on Arch's <see cref="CommandBuffer"/> (see <see cref="Buffer"/>). An entity returned by <see cref="Create"/> is a
/// placeholder until playback: use it only with the same <see cref="Commands"/> before <see cref="Flush"/>. Not thread safe.
/// </remarks>
public sealed class Commands
{
	private CommandBuffer _buffer = new();
	private readonly List<(Entity Child, Entity Parent)> _parents = [];
	private readonly List<Entity> _unparents = [];
	private readonly List<Entity> _destroyTrees = [];
	private readonly QueryDescription _pending = new QueryDescription().WithAll<PendingParent>();
	private readonly QueryDescription _pendingModels = new QueryDescription().WithAll<PendingModel>();
	private readonly List<Entity> _scratch = [];
	private int _count;

	/// <summary>Creates a command buffer for <paramref name="world"/>.</summary>
	public Commands(World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		EcsComponents.RegisterBuiltIns();
		World = world;
	}

	/// <summary>The world the commands are played back on.</summary>
	public World World { get; }

	/// <summary>Arch's buffer behind these commands, for operations not wrapped here.</summary>
	public CommandBuffer Buffer => _buffer;

	/// <summary>The number of commands recorded since the last <see cref="Flush"/>.</summary>
	public int Count => _count;

	/// <summary>Whether nothing was recorded since the last <see cref="Flush"/>.</summary>
	public bool IsEmpty => _count == 0;

	/// <summary>The number of times <see cref="Flush"/> applied commands.</summary>
	public long Playbacks { get; private set; }

	/// <summary>Records the creation of an entity with <paramref name="types"/> (set their values with <see cref="Set{T}"/>).</summary>
	public Entity Create(ComponentType[] types)
	{
		ArgumentNullException.ThrowIfNull(types);
		_count++;
		return _buffer.Create(types);
	}

	/// <summary>Records the creation of an entity with one component.</summary>
	public Entity Create<T0>(in T0 c0)
	{
		var entity = Create(Signature<T0>.Types);
		_buffer.Set(in entity, in c0);
		return entity;
	}

	/// <summary>Records the creation of an entity with two components.</summary>
	public Entity Create<T0, T1>(in T0 c0, in T1 c1)
	{
		var entity = Create(Signature<T0, T1>.Types);
		_buffer.Set(in entity, in c0);
		_buffer.Set(in entity, in c1);
		return entity;
	}

	/// <summary>Records the creation of an entity with three components.</summary>
	public Entity Create<T0, T1, T2>(in T0 c0, in T1 c1, in T2 c2)
	{
		var entity = Create(Signature<T0, T1, T2>.Types);
		_buffer.Set(in entity, in c0);
		_buffer.Set(in entity, in c1);
		_buffer.Set(in entity, in c2);
		return entity;
	}

	/// <summary>Records the creation of an entity with four components.</summary>
	public Entity Create<T0, T1, T2, T3>(in T0 c0, in T1 c1, in T2 c2, in T3 c3)
	{
		var entity = Create(Signature<T0, T1, T2, T3>.Types);
		_buffer.Set(in entity, in c0);
		_buffer.Set(in entity, in c1);
		_buffer.Set(in entity, in c2);
		_buffer.Set(in entity, in c3);
		return entity;
	}

	/// <summary>Records the destruction of <paramref name="entity"/>.</summary>
	public void Destroy(Entity entity)
	{
		_count++;
		_buffer.Destroy(in entity);
	}

	/// <summary>Records the destruction of <paramref name="entity"/> and all its descendants (an existing entity), after the other commands.</summary>
	public void DestroyRecursive(Entity entity)
	{
		_count++;
		_destroyTrees.Add(entity);
	}

	/// <summary>Records adding <paramref name="component"/> to <paramref name="entity"/>.</summary>
	public void Add<T>(Entity entity, in T component = default!)
	{
		_count++;
		_buffer.Add(in entity, in component);
	}

	/// <summary>Records removing the <typeparamref name="T"/> component from <paramref name="entity"/>.</summary>
	public void Remove<T>(Entity entity)
	{
		_count++;
		_buffer.Remove<T>(in entity);
	}

	/// <summary>Records setting the <typeparamref name="T"/> component of <paramref name="entity"/> (which has it, or gets it from a <see cref="Create"/> or <see cref="Add{T}"/> of the same commands).</summary>
	public void Set<T>(Entity entity, in T component)
	{
		_count++;
		_buffer.Set(in entity, in component);
	}

	/// <summary>
	/// Records parenting <paramref name="child"/> to <paramref name="parent"/> (see <see cref="HierarchyExtensions.SetParent"/>),
	/// applied after the other commands. The child may be an entity these commands create; the parent must exist already.
	/// </summary>
	public void SetParent(Entity child, Entity parent)
	{
		_count++;
		if (child.Id < 0) _buffer.Add(in child, new PendingParent(parent));
		else _parents.Add((child, parent));
	}

	/// <summary>Records detaching <paramref name="child"/> (an existing entity) from its parent.</summary>
	public void RemoveParent(Entity child)
	{
		_count++;
		_unparents.Add(child);
	}

	/// <summary>
	/// Applies every recorded command to <see cref="World"/>, in order: Arch's buffer (creates, sets, adds, removes,
	/// destroys), then the parents of created entities, the models recorded with <c>SpawnModel</c>, the other hierarchy
	/// commands, then <see cref="DestroyRecursive"/>. Called by the ECS module at the end of
	/// every stage; call it yourself outside a query to apply commands early.
	/// </summary>
	public void Flush()
	{
		if (_count == 0) return;
		_count = 0;
		Playbacks++;

		_buffer.Playback(World, dispose: true);

		if (World.CountEntities(in _pending) > 0)
		{
			_scratch.Clear();
			foreach (ref var chunk in World.Query(in _pending))
			{
				var entities = chunk.Entities;
				for (var i = 0; i < chunk.Count; i++) _scratch.Add(entities[i]);
			}

			foreach (var entity in _scratch)
			{
				var parent = World.Get<PendingParent>(entity).Value;
				World.Remove<PendingParent>(entity);
				if (World.IsAlive(parent)) World.SetParent(entity, parent);
			}

			_scratch.Clear();
		}

		if (World.CountEntities(in _pendingModels) > 0)
		{
			_scratch.Clear();
			foreach (ref var chunk in World.Query(in _pendingModels))
			{
				var entities = chunk.Entities;
				for (var i = 0; i < chunk.Count; i++) _scratch.Add(entities[i]);
			}

			foreach (var entity in _scratch)
			{
				var pending = World.Get<PendingModel>(entity);
				World.Remove<PendingModel>(entity);
				ModelSpawnExtensions.Instantiate(World, entity, pending.Model, pending.Options);
			}

			_scratch.Clear();
		}

		foreach (var (child, parent) in _parents)
		{
			if (World.IsAlive(child) && World.IsAlive(parent)) World.SetParent(child, parent);
		}

		foreach (var child in _unparents) World.RemoveParent(child);
		foreach (var entity in _destroyTrees) World.DestroyRecursive(entity);

		_parents.Clear();
		_unparents.Clear();
		_destroyTrees.Clear();
	}

	/// <summary>Discards every recorded command.</summary>
	public void Clear()
	{
		_buffer.Dispose();
		_buffer = new CommandBuffer();
		_parents.Clear();
		_unparents.Clear();
		_destroyTrees.Clear();
		_count = 0;
	}

	private static class Signature<T0>
	{
		public static readonly ComponentType[] Types = [Component<T0>.ComponentType];
	}

	private static class Signature<T0, T1>
	{
		public static readonly ComponentType[] Types = [Component<T0>.ComponentType, Component<T1>.ComponentType];
	}

	private static class Signature<T0, T1, T2>
	{
		public static readonly ComponentType[] Types = [Component<T0>.ComponentType, Component<T1>.ComponentType, Component<T2>.ComponentType];
	}

	private static class Signature<T0, T1, T2, T3>
	{
		public static readonly ComponentType[] Types = [Component<T0>.ComponentType, Component<T1>.ComponentType, Component<T2>.ComponentType, Component<T3>.ComponentType];
	}
}
