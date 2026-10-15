using System.Numerics;
using System.Runtime.CompilerServices;

using Arch.Core;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;

using BepuUtilities;
using BepuUtilities.Memory;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

using ConvexHullShape = BepuPhysics.Collidables.ConvexHull;

namespace Ion.Extensions.Physics3D;

/// <summary>
/// The BepuPhysics v2 simulation of one scope (the root, or a scene) and its adapter to the scope's ECS
/// <see cref="World"/>: every entity with a <see cref="Transform"/> and a <see cref="RigidBody3D"/>, or a root one with a
/// <see cref="Collider3D"/>, is a body (a Bepu static without a rigid body); a child collider without a rigid body is a
/// part of the nearest ancestor body (a compound body); every <see cref="Joint3D"/> is a constraint.
/// </summary>
/// <remarks>
/// <para>
/// Compound bodies: a part's shape is placed on its body by the <see cref="Transform"/>s between them, as the transform
/// propagation places it (ancestors' positions, rotations, and scales applied to positions; a collider's own geometry is
/// not scaled). A dynamic compound body's <see cref="RigidBody3D.Mass"/> is shared by
/// its shapes in proportion to their volumes, which places the center of mass. Its friction and restitution are its own
/// collider's (or its first part's); layers, masks, sensors and events are each shape's own, and contacts, sensor events
/// and ray casts report the part's entity (overlap queries report the body's). Adding, removing, moving or reshaping a
/// part rebuilds the body. A rigid body without a collider of its own is a body only while it has parts. A child collider
/// with no body above it is a static body of its own at its world position.
/// </para>
/// <para>
/// A rigid body on a child entity is simulated in world space: its pose is its <see cref="Transform"/> composed with its
/// ancestors', and a dynamic one's simulated pose is written back relative to its parent (after its ancestors' bodies are
/// written). A kinematic child is driven to where its transform puts it under its parent, so it is carried along.
/// </para>
/// <para>
/// <see cref="Step"/> (run by <see cref="Physics3DSystem"/> in FixedUpdate at <see cref="StageOrder.Physics"/>) creates the
/// bodies of new colliders and pushes what game code changed (transforms, velocities, body settings, colliders; kinematic
/// bodies are driven to their transform, other bodies teleported), removes the bodies of entities that lost their
/// collider, synchronizes the joints, steps the simulation by the fixed delta, writes the moved bodies' poses and
/// velocities back and emits <see cref="Collision3D"/> and <see cref="Trigger3D"/> events.
/// </para>
/// <para>
/// Single-threaded by default: the step runs on the calling thread. With <see cref="Physics3DConfig.ThreadCount"/> above 1
/// a Bepu <see cref="ThreadDispatcher"/> runs it on worker threads while the caller waits, in Bepu's deterministic mode, and
/// the events are sorted before they are emitted, so the results do not depend on the thread count.
/// </para>
/// </remarks>
public sealed class PhysicsWorld3D : IPhysicsWorld3D, IDisposable
{
	private static readonly QueryDescription Bodies = new QueryDescription().WithAll<Collider3D, Transform>();
	private static readonly QueryDescription BareBodies = new QueryDescription().WithAll<RigidBody3D, Transform>().WithNone<Collider3D>();
	private static readonly QueryDescription Joints = new QueryDescription().WithAll<Joint3D>();

	// The deepest hierarchy searched for a part's body.
	private const int MaxPartDepth = 64;

	private readonly World _ecs;
	private readonly IEvents? _events;
	private readonly Physics3DConfig _config;
	private readonly BufferPool _pool;
	private readonly ThreadDispatcher? _dispatcher;
	private readonly Simulation _sim;
	private readonly BodyActivityDescription _activity;

	internal BodySlot[] Slots = new BodySlot[64];
	private int _highWater = 1;
	private int[] _free = new int[16];
	private int _freeCount;
	private int _bodyCount;

	// Bepu handles to slots (handles are small dense integers).
	internal int[] BodyHandleToSlot = new int[64];
	internal int[] StaticHandleToSlot = new int[64];

	private JointSlot[] _joints = new JointSlot[16];
	private int _jointHighWater = 1;
	private int[] _freeJoints = new int[8];
	private int _freeJointCount;
	private int _jointCount;

	private readonly List<HullEntry> _hulls = [];

	// The parts found this step, sorted by body entity then part entity, and where each body's run starts; the bodies of
	// rigid bodies without a collider of their own.
	private PartRecord[] _gathered = new PartRecord[16];
	private int _gatheredCount;
	private readonly Dictionary<Entity, int> _partsOf = [];
	private readonly Dictionary<Entity, int> _bare = [];
	private int _partCount;

	// Rigid bodies on child entities that moved this step, written back after the root bodies, shallowest first.
	private ChildBody[] _childBodies = new ChildBody[8];
	private int _childBodyCount;

	// Contact tracking: one buffer per worker filled by the narrow phase, merged and sorted after the step.
	internal readonly PairBuffer[] WorkerPairs;
	private PairRecord[] _previous = new PairRecord[64];
	private int _previousCount;
	private PairRecord[] _current = new PairRecord[64];
	private int _currentCount;

	private Vector3 _gravity;
	private uint _stamp;
	private long _steps;
	private bool _disposed;

	/// <summary>Creates a world for the entities of <paramref name="ecs"/>, emitting events on <paramref name="events"/> (none when null).</summary>
	public PhysicsWorld3D(World ecs, IEvents? events, Physics3DConfig? config = null)
	{
		ArgumentNullException.ThrowIfNull(ecs);
		_ecs = ecs;
		_events = events;
		_config = config ?? new Physics3DConfig();
		if (_config.SubSteps < 1) throw new ArgumentOutOfRangeException(nameof(config), _config.SubSteps, "Physics3DConfig.SubSteps must be at least 1.");
		if (_config.Iterations < 1) throw new ArgumentOutOfRangeException(nameof(config), _config.Iterations, "Physics3DConfig.Iterations must be at least 1.");
		Physics3DComponents.Register();

		_gravity = _config.Gravity;
		DebugDraw = _config.DebugDraw;
		_activity = new BodyActivityDescription(_config.SleepThreshold > 0 ? _config.SleepThreshold : -1f);
		_pool = new BufferPool();
		_dispatcher = _config.ThreadCount > 1 ? new ThreadDispatcher(_config.ThreadCount) : null;
		WorkerPairs = new PairBuffer[Math.Max(1, _dispatcher?.ThreadCount ?? 1)];
		for (var i = 0; i < WorkerPairs.Length; i++) WorkerPairs[i] = new PairBuffer();

		_sim = Simulation.Create(_pool, new NarrowPhaseCallbacks(this), new PoseIntegratorCallbacks(this), new SolveDescription(_config.Iterations, _config.SubSteps));
		if (_dispatcher is not null) _sim.Deterministic = true;
	}

	/// <summary>The ECS world whose entities this world simulates.</summary>
	public World Entities => _ecs;

	/// <summary>The settings the world was created with.</summary>
	public Physics3DConfig Config => _config;

	/// <summary>The BepuPhysics simulation (for what the adapter does not cover; do not add or remove what the adapter owns).</summary>
	public Simulation Simulation
	{
		get
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			return _sim;
		}
	}

	/// <inheritdoc/>
	public Vector3 Gravity
	{
		get => _gravity;
		set => _gravity = value;
	}

	/// <inheritdoc/>
	public int BodyCount => _bodyCount;

	/// <inheritdoc/>
	public int JointCount => _jointCount;

	/// <summary>The number of compound parts: child colliders whose shapes are on an ancestor's body.</summary>
	public int PartCount => _partCount;

	/// <inheritdoc/>
	public long StepCount => _steps;

	/// <inheritdoc/>
	public bool DebugDraw { get; set; }

	/// <summary>The number of awake dynamic and kinematic bodies.</summary>
	public int AwakeBodyCount => Simulation.Bodies.ActiveSet.Count;

	/// <summary>
	/// Runs one fixed step of <paramref name="dt"/> seconds: synchronizes the entities into the simulation, steps it, writes
	/// the results back and emits the events (see the remarks of <see cref="PhysicsWorld3D"/>).
	/// </summary>
	public void Step(float dt)
	{
		var sim = Simulation;
		if (!(dt > 0)) return;

		_stamp++;
		PushBodies(dt);
		RemoveUnseenBodies();
		SyncJoints();

		for (var i = 0; i < WorkerPairs.Length; i++) WorkerPairs[i].Count = 0;
		sim.Timestep(dt, _dispatcher);
		_steps++;

		PullBodies();
		EmitEvents();
	}

	/// <summary>Steps the Bepu simulation alone, without the ECS synchronization or events (the benchmarks' baseline).</summary>
	internal void SimulateOnly(float dt)
	{
		for (var i = 0; i < WorkerPairs.Length; i++) WorkerPairs[i].Count = 0;
		Simulation.Timestep(dt, _dispatcher);
	}

	// ---- Push ----

	private void PushBodies(float dt)
	{
		GatherParts();

		// Bodies with a collider: roots, and entities with a rigid body (on a child, at its world pose).
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var hasRigid = chunk.Has<RigidBody3D>();
			var parented = chunk.Has<Parent>();
			if (!hasRigid && parented) continue;   // parts, below
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			ref var transform0 = ref chunk.GetFirst<Transform>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody3D>() : ref Unsafe.NullRef<RigidBody3D>();

			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var collider = ref Unsafe.Add(ref collider0, i);
				var rigid = hasRigid ? Unsafe.Add(ref rigid0, i) : RigidBody3D.Static();
				var transform = parented ? WorldOf(entity, Unsafe.Add(ref transform0, i)) : Unsafe.Add(ref transform0, i);
				collider.BodySlot = PushBody(entity, collider.BodySlot, hasCollider: true, collider, transform, rigid, PartsOf(entity), dt);
			}
		}

		// Rigid bodies without a collider of their own: bodies of their parts.
		if (_partsOf.Count > 0)
		{
			foreach (ref var chunk in _ecs.Query(in BareBodies))
			{
				var parented = chunk.Has<Parent>();
				var count = chunk.Count;
				ref var entity0 = ref chunk.Entity(0);
				ref var transform0 = ref chunk.GetFirst<Transform>();
				ref var rigid0 = ref chunk.GetFirst<RigidBody3D>();
				for (var i = 0; i < count; i++)
				{
					var entity = Unsafe.Add(ref entity0, i);
					var parts = PartsOf(entity);
					if (parts.IsEmpty) continue;
					_bare.TryGetValue(entity, out var slot);
					var transform = parented ? WorldOf(entity, Unsafe.Add(ref transform0, i)) : Unsafe.Add(ref transform0, i);
					_bare[entity] = PushBody(entity, slot, hasCollider: false, default, transform, Unsafe.Add(ref rigid0, i), parts, dt);
				}
			}
		}

		// Parts point at their body; a part with no body above it is a static body of its own at its world pose.
		_partCount = 0;
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			if (chunk.Has<RigidBody3D>() || !chunk.Has<Parent>()) continue;
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var collider = ref Unsafe.Add(ref collider0, i);
				var owner = OwnerOf(entity, out var position, out var rotation);
				if (owner == Entity.Null)
				{
					var world = new Transform { Position = position, Rotation = rotation, Scale = Vector3.One };
					collider.BodySlot = PushBody(entity, collider.BodySlot, hasCollider: true, collider, world, RigidBody3D.Static(), default, dt);
					continue;
				}

				collider.BodySlot = BodySlotOf(owner);
				if (collider.BodySlot != 0) _partCount++;
			}
		}
	}

	/// <summary>Creates, updates or rebuilds one body (its own collider, if any, and its parts); returns its slot.</summary>
	private int PushBody(Entity entity, int slot, bool hasCollider, in Collider3D collider, in Transform transform, in RigidBody3D rigid, ReadOnlySpan<PartRecord> parts, float dt)
	{
		if (slot <= 0 || slot >= _highWater || !Slots[slot].Alive || Slots[slot].Entity != entity)
		{
			return CreateBody(entity, hasCollider, collider, transform, rigid, parts);
		}

		ref var body = ref Slots[slot];
		body.Seen = _stamp;

		// Structural changes (type, shape, continuity, parts; a compound's mass) rebuild the body.
		var compound = body.Children is not null;
		if (rigid.Type != body.Rigid.Type || rigid.IsBullet != body.Rigid.IsBullet || hasCollider != body.HasOwnCollider
			|| (hasCollider && !Collider3D.SameShape(collider, body.OwnCollider)) || !SameParts(body, parts)
			|| (compound && (rigid.Mass != body.Rigid.Mass || rigid.FixedRotation != body.Rigid.FixedRotation)))
		{
			DestroyBody(slot, emitEnds: true);
			return CreateBody(entity, hasCollider, collider, transform, rigid, parts);
		}

		Push(ref body, transform, rigid, dt);
		return slot;
	}

	/// <summary>Finds every part (a child collider without a rigid body) and its body, sorted by body then part.</summary>
	private void GatherParts()
	{
		_gatheredCount = 0;
		_partsOf.Clear();
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			if (chunk.Has<RigidBody3D>() || !chunk.Has<Parent>()) continue;
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				var owner = OwnerOf(entity, out var position, out var rotation);
				if (owner == Entity.Null) continue;
				if (_gatheredCount == _gathered.Length) Array.Resize(ref _gathered, _gathered.Length * 2);
				_gathered[_gatheredCount++] = new PartRecord(owner, entity, Unsafe.Add(ref collider0, i), new RigidPose(position, rotation));
			}
		}

		if (_gatheredCount == 0) return;
		_gathered.AsSpan(0, _gatheredCount).Sort(PartOrder);
		for (var i = 0; i < _gatheredCount; i++)
		{
			if (i == 0 || _gathered[i].Body != _gathered[i - 1].Body) _partsOf[_gathered[i].Body] = i;
		}
	}

	private ReadOnlySpan<PartRecord> PartsOf(Entity body)
	{
		if (!_partsOf.TryGetValue(body, out var start)) return default;
		var end = start;
		while (end < _gatheredCount && _gathered[end].Body == body) end++;
		return _gathered.AsSpan(start, end - start);
	}

	/// <summary>
	/// The nearest ancestor body of a part (an entity with a rigid body, or a root with a collider) and the part's pose in
	/// its space; or <see cref="Entity.Null"/>, with the part's world pose, when no ancestor is a body.
	/// </summary>
	private Entity OwnerOf(Entity entity, out Vector3 position, out Quaternion rotation)
	{
		ref readonly var transform = ref _ecs.Get<Transform>(entity);
		position = transform.Position;
		rotation = transform.Rotation;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) return Entity.Null;
			current = parent.Value;
			var hasTransform = _ecs.TryGet<Transform>(current, out var between);
			if ((_ecs.Has<RigidBody3D>(current) || (_ecs.Has<Collider3D>(current) && !_ecs.Has<Parent>(current))) && hasTransform)
			{
				// The body's own scale scales its parts' positions too (not its pose).
				position *= between.Scale;
				return current;
			}

			// An entity in between (a group, or another part): its transform places the part, as the propagation does.
			if (hasTransform)
			{
				position = between.Position + Vector3.Transform(between.Scale * position, between.Rotation);
				rotation = Quaternion.Concatenate(rotation, between.Rotation);
			}
		}

		return Entity.Null;
	}

	/// <summary>The world pose of a child entity: its transform composed with its ancestors' (as the transform propagation does).</summary>
	private Transform WorldOf(Entity entity, in Transform local)
	{
		var position = local.Position;
		var rotation = local.Rotation;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) break;
			current = parent.Value;
			if (!_ecs.TryGet<Transform>(current, out var ancestor)) continue;
			position = ancestor.Position + Vector3.Transform(ancestor.Scale * position, ancestor.Rotation);
			rotation = Quaternion.Concatenate(rotation, ancestor.Rotation);
		}

		return new Transform(position, rotation);
	}

	/// <summary>The local pose under <paramref name="entity"/>'s parent of a world pose: the inverse of <see cref="WorldOf"/> (shear aside).</summary>
	private void LocalOf(Entity entity, Vector3 worldPosition, Quaternion worldRotation, out Vector3 position, out Quaternion rotation)
	{
		var framePosition = Vector3.Zero;
		var frameRotation = Quaternion.Identity;
		var frameScale = Vector3.One;
		var first = true;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) break;
			current = parent.Value;
			if (!_ecs.TryGet<Transform>(current, out var ancestor)) continue;
			if (first)
			{
				framePosition = ancestor.Position;
				frameRotation = ancestor.Rotation;
				frameScale = ancestor.Scale;
				first = false;
				continue;
			}

			framePosition = ancestor.Position + Vector3.Transform(ancestor.Scale * framePosition, ancestor.Rotation);
			frameRotation = Quaternion.Concatenate(frameRotation, ancestor.Rotation);
			frameScale *= ancestor.Scale;
		}

		var inverse = Quaternion.Inverse(frameRotation);
		var relative = Vector3.Transform(worldPosition - framePosition, inverse);
		position = new Vector3(
			frameScale.X != 0 ? relative.X / frameScale.X : relative.X,
			frameScale.Y != 0 ? relative.Y / frameScale.Y : relative.Y,
			frameScale.Z != 0 ? relative.Z / frameScale.Z : relative.Z);
		rotation = Quaternion.Normalize(Quaternion.Concatenate(worldRotation, inverse));
	}

	private int DepthOf(Entity entity)
	{
		var depth = 0;
		var current = entity;
		while (depth < MaxPartDepth && _ecs.TryGet<Parent>(current, out var parent) && _ecs.IsAlive(parent.Value))
		{
			current = parent.Value;
			depth++;
		}

		return depth;
	}

	private static bool SameParts(in BodySlot body, ReadOnlySpan<PartRecord> parts)
	{
		var first = body.HasOwnCollider ? 1 : 0;
		var stored = body.Children is null ? 0 : body.Children.Length - first;
		if (stored != parts.Length) return false;
		for (var i = 0; i < parts.Length; i++)
		{
			ref readonly var child = ref body.Children![first + i];
			ref readonly var part = ref parts[i];
			if (child.Entity != part.Entity || child.Pose.Position != part.Pose.Position || child.Pose.Orientation != part.Pose.Orientation || !Collider3D.SameShape(child.Collider, part.Collider)) return false;
		}

		return true;
	}

	private void Push(ref BodySlot body, in Transform transform, in RigidBody3D rigid, float dt)
	{
		var moved = transform.Position != body.Position || transform.Rotation != body.Rotation;

		if (body.IsStatic)
		{
			if (!moved) return;
			var description = _sim.Statics.GetDescription(body.Static);
			description.Pose = BodyPose(transform.Position, transform.Rotation, body.Center);
			_sim.Statics.ApplyDescription(body.Static, description);
			body.Position = transform.Position;
			body.Rotation = transform.Rotation;
			return;
		}

		var reference = _sim.Bodies[body.Body];
		if (rigid.Mass != body.Rigid.Mass || rigid.FixedRotation != body.Rigid.FixedRotation)
		{
			// (A compound is rebuilt instead, see PushBody.)
			if (rigid.Type == RigidBodyType3D.Dynamic) reference.SetLocalInertia(Inertia(body.Collider, rigid));
		}

		var wake = false;
		if (rigid.LinearVelocity != body.Rigid.LinearVelocity)
		{
			reference.Velocity.Linear = rigid.LinearVelocity;
			wake = true;
		}

		if (rigid.AngularVelocity != body.Rigid.AngularVelocity)
		{
			reference.Velocity.Angular = rigid.AngularVelocity;
			wake = true;
		}

		if (rigid.Type == RigidBodyType3D.Kinematic)
		{
			if (moved)
			{
				// The velocity that reaches the target pose in one step (the angular part to first order in the rotation,
				// which keeps the input side free of trigonometry).
				ref var pose = ref reference.Pose;
				var target = BodyPose(transform.Position, transform.Rotation, body.Center);
				reference.Velocity.Linear = (target.Position - pose.Position) / dt;
				var delta = Quaternion.Concatenate(Quaternion.Inverse(pose.Orientation), target.Orientation);
				var sign = delta.W < 0 ? -1f : 1f;
				reference.Velocity.Angular = new Vector3(delta.X, delta.Y, delta.Z) * (2f * sign / dt);
				body.Driven = true;
				body.Position = transform.Position;
				body.Rotation = transform.Rotation;
				wake = true;
			}
			else if (body.Driven)
			{
				reference.Velocity.Linear = default;
				reference.Velocity.Angular = default;
				body.Driven = false;
			}
		}
		else if (moved)
		{
			reference.Pose = BodyPose(transform.Position, transform.Rotation, body.Center);
			reference.UpdateBounds();
			body.Position = transform.Position;
			body.Rotation = transform.Rotation;
			wake = true;
		}

		if (wake && !reference.Awake) reference.Awake = true;
		body.Rigid = rigid;
	}

	private int CreateBody(Entity entity, bool hasCollider, in Collider3D collider, in Transform transform, in RigidBody3D rigid, ReadOnlySpan<PartRecord> parts)
	{
		if (hasCollider) Validate(entity, collider, rigid);
		foreach (ref readonly var part in parts) Validate(part.Entity, part.Collider, rigid);
		var slot = AllocateSlot();
		ref var body = ref Slots[slot];
		var generation = body.Generation;
		body = default;
		body.Alive = true;
		body.Entity = entity;
		body.Collider = hasCollider ? collider : parts[0].Collider;
		body.HasOwnCollider = hasCollider;
		body.OwnCollider = collider;
		body.Rigid = rigid;
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;
		body.Seen = _stamp;
		body.Generation = generation + 1;

		TypedIndex shape;
		if (!parts.IsEmpty)
		{
			shape = CreateCompound(ref body, hasCollider, collider, rigid, parts, out var inertia);
			body.OwnsShape = true;
			var compoundPose = BodyPose(transform.Position, transform.Rotation, body.Center);
			AddToSimulation(ref body, slot, compoundPose, shape, rigid, inertia);
			_bodyCount++;
			return slot;
		}

		if (collider.Shape == ColliderShape3D.ConvexHull)
		{
			var hull = _hulls[collider.Hull.Value - 1];
			shape = hull.Shape;
			body.Center = hull.Center;
			body.OwnsShape = false;
		}
		else
		{
			shape = collider.Shape switch
			{
				ColliderShape3D.Sphere => _sim.Shapes.Add(new Sphere(collider.Radius)),
				ColliderShape3D.Capsule => _sim.Shapes.Add(new Capsule(collider.Radius, collider.Length)),
				ColliderShape3D.Cylinder => _sim.Shapes.Add(new Cylinder(collider.Radius, collider.Length)),
				_ => _sim.Shapes.Add(new Box(collider.Size.X, collider.Size.Y, collider.Size.Z)),
			};
			body.OwnsShape = true;
		}

		var pose = BodyPose(transform.Position, transform.Rotation, body.Center);
		AddToSimulation(ref body, slot, pose, shape, rigid, rigid.Type == RigidBodyType3D.Dynamic ? Inertia(collider, rigid) : default);
		_bodyCount++;
		return slot;
	}

	private void AddToSimulation(ref BodySlot body, int slot, RigidPose pose, TypedIndex shape, in RigidBody3D rigid, BodyInertia inertia)
	{
		body.Shape = shape;
		if (rigid.Type == RigidBodyType3D.Static)
		{
			body.IsStatic = true;
			body.Static = _sim.Statics.Add(new StaticDescription(pose, shape));
			SetHandleSlot(ref StaticHandleToSlot, body.Static.Value, slot);
			return;
		}

		var continuity = rigid.IsBullet ? ContinuousDetection.Continuous(1e-3f, 1e-3f) : ContinuousDetection.Discrete;
		var collidable = new CollidableDescription(shape, continuity);
		var velocity = new BodyVelocity(rigid.LinearVelocity, rigid.AngularVelocity);
		var description = rigid.Type == RigidBodyType3D.Kinematic
			? BodyDescription.CreateKinematic(pose, velocity, collidable, _activity)
			: BodyDescription.CreateDynamic(pose, velocity, inertia, collidable, _activity);
		body.Body = _sim.Bodies.Add(description);
		SetHandleSlot(ref BodyHandleToSlot, body.Body.Value, slot);
	}

	/// <summary>
	/// Builds the compound of a body's own collider (if any, child 0) and its parts, in the body entity's space, sharing a
	/// dynamic body's mass by volume; sets the body's center (the center of mass, or of the shapes' volume) and children.
	/// </summary>
	private TypedIndex CreateCompound(ref BodySlot body, bool hasCollider, in Collider3D collider, in RigidBody3D rigid, ReadOnlySpan<PartRecord> parts, out BodyInertia inertia)
	{
		var count = parts.Length + (hasCollider ? 1 : 0);
		var children = new CompoundPart[count];
		var totalVolume = 0f;
		for (var i = 0; i < count; i++)
		{
			children[i] = hasCollider && i == 0
				? new CompoundPart(body.Entity, collider, RigidPose.Identity)
				: new CompoundPart(parts[i - (hasCollider ? 1 : 0)].Entity, parts[i - (hasCollider ? 1 : 0)].Collider, parts[i - (hasCollider ? 1 : 0)].Pose);
			totalVolume += Volume(children[i].Collider);
		}

		var mass = rigid.Mass > 0 ? rigid.Mass : 1f;
		var dynamic = rigid.Type == RigidBodyType3D.Dynamic;
		var shapes = new TypedIndex[count];
		using var builder = new CompoundBuilder(_pool, _sim.Shapes, count);
		for (var i = 0; i < count; i++)
		{
			ref var child = ref children[i];
			var weight = totalVolume > 0 ? mass * Volume(child.Collider) / totalVolume : mass / count;
			var pose = child.Pose;
			if (child.Collider.Shape == ColliderShape3D.ConvexHull)
			{
				// A hull's points are around its own center.
				var hull = _hulls[child.Collider.Hull.Value - 1];
				pose.Position += Vector3.Transform(hull.Center, pose.Orientation);
				shapes[i] = hull.Shape;
			}
			else
			{
				shapes[i] = AddShape(child.Collider);
				child.OwnsShape = true;
			}

			if (dynamic) builder.Add(shapes[i], pose, Inertia(child.Collider, new RigidBody3D(RigidBodyType3D.Dynamic) { Mass = weight }));
			else builder.AddForKinematic(shapes[i], pose, weight);
		}

		Buffer<CompoundChild> built;
		Vector3 center;
		if (dynamic)
		{
			builder.BuildDynamicCompound(out built, out inertia, out center);
			if (rigid.FixedRotation) inertia.InverseInertiaTensor = default;
		}
		else
		{
			builder.BuildKinematicCompound(out built, out center);
			inertia = default;
		}

		body.Center = center;
		body.Children = children;
		body.ChildShapes = shapes;
		body.LayerUnion = 0;
		body.MaskUnion = 0;
		foreach (ref readonly var child in children.AsSpan())
		{
			body.LayerUnion |= child.Collider.Layer;
			body.MaskUnion |= child.Collider.Mask;
			body.AnySensor |= child.Collider.IsSensor;
		}

		// The compound's material is its first shape's; whether it is a sensor is decided per shape.
		body.Collider.IsSensor = false;
		return _sim.Shapes.Add(new Compound(built));
	}

	private TypedIndex AddShape(in Collider3D collider) => collider.Shape switch
	{
		ColliderShape3D.Sphere => _sim.Shapes.Add(new Sphere(collider.Radius)),
		ColliderShape3D.Capsule => _sim.Shapes.Add(new Capsule(collider.Radius, collider.Length)),
		ColliderShape3D.Cylinder => _sim.Shapes.Add(new Cylinder(collider.Radius, collider.Length)),
		_ => _sim.Shapes.Add(new Box(collider.Size.X, collider.Size.Y, collider.Size.Z)),
	};

	/// <summary>The volume of a collider's shape (for sharing a compound's mass).</summary>
	private float Volume(in Collider3D collider)
	{
		var r = collider.Radius;
		return collider.Shape switch
		{
			ColliderShape3D.Sphere => 4f / 3f * MathF.PI * r * r * r,
			ColliderShape3D.Capsule => MathF.PI * r * r * collider.Length + 4f / 3f * MathF.PI * r * r * r,
			ColliderShape3D.Cylinder => MathF.PI * r * r * collider.Length,
			ColliderShape3D.ConvexHull => _hulls[collider.Hull.Value - 1].Volume,
			_ => collider.Size.X * collider.Size.Y * collider.Size.Z,
		};
	}

	private BodyInertia Inertia(in Collider3D collider, in RigidBody3D rigid)
	{
		var mass = rigid.Mass > 0 ? rigid.Mass : 1f;
		BodyInertia inertia = collider.Shape switch
		{
			ColliderShape3D.Sphere => new Sphere(collider.Radius).ComputeInertia(mass),
			ColliderShape3D.Capsule => new Capsule(collider.Radius, collider.Length).ComputeInertia(mass),
			ColliderShape3D.Cylinder => new Cylinder(collider.Radius, collider.Length).ComputeInertia(mass),
			ColliderShape3D.ConvexHull => _hulls[collider.Hull.Value - 1].Hull.ComputeInertia(mass),
			_ => new Box(collider.Size.X, collider.Size.Y, collider.Size.Z).ComputeInertia(mass),
		};
		if (rigid.FixedRotation) inertia.InverseInertiaTensor = default;
		return inertia;
	}

	private void Validate(Entity entity, in Collider3D collider, in RigidBody3D rigid)
	{
		var ok = collider.Shape switch
		{
			ColliderShape3D.Box => collider.Size.X > 0 && collider.Size.Y > 0 && collider.Size.Z > 0,
			ColliderShape3D.Sphere => collider.Radius > 0,
			ColliderShape3D.Capsule or ColliderShape3D.Cylinder => collider.Radius > 0 && collider.Length >= 0,
			ColliderShape3D.ConvexHull => collider.Hull.Value > 0 && collider.Hull.Value <= _hulls.Count,
			_ => false,
		};
		if (!ok) throw new InvalidOperationException($"The {collider.Shape} collider of {entity} is invalid (sizes and radii must be positive, a hull must come from this world's CreateConvexHull).");
		if (rigid.Type == RigidBodyType3D.Dynamic && !(rigid.Mass >= 0)) throw new InvalidOperationException($"The rigid body of {entity} needs a non-negative mass.");
	}

	private static RigidPose BodyPose(Vector3 position, Quaternion rotation, Vector3 center) =>
		new(center == Vector3.Zero ? position : position + Vector3.Transform(center, rotation), rotation);

	private void RemoveUnseenBodies()
	{
		for (var slot = 1; slot < _highWater; slot++)
		{
			if (Slots[slot].Alive && Slots[slot].Seen != _stamp) DestroyBody(slot, emitEnds: true);
		}
	}

	private void DestroyBody(int slot, bool emitEnds)
	{
		ref var body = ref Slots[slot];

		// Joints first (Bepu constraints reference body handles).
		for (var j = 1; j < _jointHighWater; j++)
		{
			ref var joint = ref _joints[j];
			if (joint.Alive && (joint.SlotA == slot || joint.SlotB == slot)) DestroyJoint(j);
		}

		if (emitEnds) EndPairsOf(slot);

		if (body.IsStatic)
		{
			_sim.Statics.Remove(body.Static);
			StaticHandleToSlot[body.Static.Value] = 0;
		}
		else
		{
			_sim.Bodies.Remove(body.Body);
			BodyHandleToSlot[body.Body.Value] = 0;
		}

		if (body.Children is not null)
		{
			// The compound's child buffer, then the child shapes it does not own (hulls are shared).
			_sim.Shapes.RemoveAndDispose(body.Shape, _pool);
			for (var i = 0; i < body.Children.Length; i++)
			{
				if (body.Children[i].OwnsShape) _sim.Shapes.Remove(body.ChildShapes![i]);
			}

			body.Children = null;
			body.ChildShapes = null;
		}
		else if (body.OwnsShape)
		{
			_sim.Shapes.Remove(body.Shape);
		}

		if (!body.HasOwnCollider) _bare.Remove(body.Entity);
		body.Alive = false;
		body.Entity = Entity.Null;
		if (_freeCount == _free.Length) Array.Resize(ref _free, _free.Length * 2);
		_free[_freeCount++] = slot;
		_bodyCount--;
	}

	private int AllocateSlot()
	{
		if (_freeCount > 0) return _free[--_freeCount];
		if (_highWater == Slots.Length) Array.Resize(ref Slots, Slots.Length * 2);
		return _highWater++;
	}

	private static void SetHandleSlot(ref int[] table, int handle, int slot)
	{
		if (handle >= table.Length) Array.Resize(ref table, Math.Max(handle + 1, table.Length * 2));
		table[handle] = slot;
	}

	internal int SlotOf(CollidableReference collidable)
	{
		if (collidable.Mobility == CollidableMobility.Static)
		{
			var handle = collidable.StaticHandle.Value;
			return (uint)handle < (uint)StaticHandleToSlot.Length ? StaticHandleToSlot[handle] : 0;
		}

		var bodyHandle = collidable.BodyHandle.Value;
		return (uint)bodyHandle < (uint)BodyHandleToSlot.Length ? BodyHandleToSlot[bodyHandle] : 0;
	}

	// ---- Joints ----

	private void SyncJoints()
	{
		foreach (ref var chunk in _ecs.Query(in Joints))
		{
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var joint0 = ref chunk.GetFirst<Joint3D>();
			for (var i = 0; i < count; i++) SyncJoint(Unsafe.Add(ref entity0, i), ref Unsafe.Add(ref joint0, i));
		}

		for (var slot = 1; slot < _jointHighWater; slot++)
		{
			if (_joints[slot].Alive && _joints[slot].Seen != _stamp) DestroyJoint(slot);
		}
	}

	private void SyncJoint(Entity entity, ref Joint3D joint)
	{
		var slotA = BodySlotOf(joint.BodyA);
		var slotB = BodySlotOf(joint.BodyB);
		var slot = joint.JointSlot;
		if (slot > 0 && slot < _jointHighWater && _joints[slot].Alive && _joints[slot].Entity == entity)
		{
			ref var existing = ref _joints[slot];
			if (existing.SlotA == slotA && existing.SlotB == slotB && existing.GenerationA == Slots[slotA].Generation &&
				existing.GenerationB == Slots[slotB].Generation && Joint3D.SameDefinition(joint, existing.Definition))
			{
				existing.Seen = _stamp;
				return;
			}

			DestroyJoint(slot);
			joint.JointSlot = 0;
		}

		// Bepu constrains bodies only: both sides need a (dynamic or kinematic) body.
		if (slotA == 0 || slotB == 0 || slotA == slotB || Slots[slotA].IsStatic || Slots[slotB].IsStatic) return;

		slot = AllocateJointSlot();
		_joints[slot] = new JointSlot
		{
			Alive = true,
			Entity = entity,
			Definition = joint,
			SlotA = slotA,
			SlotB = slotB,
			GenerationA = Slots[slotA].Generation,
			GenerationB = Slots[slotB].Generation,
			Seen = _stamp,
			Constraint = CreateConstraint(joint, ref Slots[slotA], ref Slots[slotB]),
		};
		joint.JointSlot = slot;
		_jointCount++;
	}

	private ConstraintHandle CreateConstraint(in Joint3D joint, ref BodySlot a, ref BodySlot b)
	{
		var poseA = _sim.Bodies[a.Body].Pose;
		var poseB = _sim.Bodies[b.Body].Pose;
		var spring = new SpringSettings(joint.Frequency > 0 ? joint.Frequency : 30f, joint.DampingRatio >= 0 ? joint.DampingRatio : 1f);
		var offsetA = joint.LocalAnchorA - a.Center;
		var offsetB = joint.LocalAnchorB - b.Center;

		switch (joint.Type)
		{
			case JointType3D.Hinge:
			{
				var axisA = joint.Axis.LengthSquared() > 0 ? Vector3.Normalize(joint.Axis) : Vector3.UnitY;
				var worldAxis = Vector3.Transform(axisA, poseA.Orientation);
				var axisB = Vector3.Transform(worldAxis, Quaternion.Inverse(poseB.Orientation));
				return _sim.Solver.Add(a.Body, b.Body, new Hinge { LocalOffsetA = offsetA, LocalHingeAxisA = axisA, LocalOffsetB = offsetB, LocalHingeAxisB = axisB, SpringSettings = spring });
			}
			case JointType3D.Weld:
			{
				var inverseA = Quaternion.Inverse(poseA.Orientation);
				var localOffset = Vector3.Transform(poseB.Position - poseA.Position, inverseA);
				var localOrientation = Quaternion.Concatenate(poseB.Orientation, inverseA);
				return _sim.Solver.Add(a.Body, b.Body, new Weld { LocalOffset = localOffset, LocalOrientation = localOrientation, SpringSettings = spring });
			}
			case JointType3D.Distance:
			{
				var max = joint.MaxDistance;
				if (!(max > 0))
				{
					var worldA = poseA.Position + Vector3.Transform(offsetA, poseA.Orientation);
					var worldB = poseB.Position + Vector3.Transform(offsetB, poseB.Orientation);
					max = Vector3.Distance(worldA, worldB);
				}

				var min = Math.Clamp(joint.MinDistance, 0, max);
				return _sim.Solver.Add(a.Body, b.Body, new DistanceLimit(offsetA, offsetB, min, max, spring));
			}
			default:
				return _sim.Solver.Add(a.Body, b.Body, new BallSocket { LocalOffsetA = offsetA, LocalOffsetB = offsetB, SpringSettings = spring });
		}
	}

	private void DestroyJoint(int slot)
	{
		ref var joint = ref _joints[slot];
		if (_sim.Solver.ConstraintExists(joint.Constraint)) _sim.Solver.Remove(joint.Constraint);
		joint.Alive = false;
		joint.Entity = Entity.Null;
		if (_freeJointCount == _freeJoints.Length) Array.Resize(ref _freeJoints, _freeJoints.Length * 2);
		_freeJoints[_freeJointCount++] = slot;
		_jointCount--;
	}

	private int AllocateJointSlot()
	{
		if (_freeJointCount > 0) return _freeJoints[--_freeJointCount];
		if (_jointHighWater == _joints.Length) Array.Resize(ref _joints, _joints.Length * 2);
		return _jointHighWater++;
	}

	/// <summary>The body slot of an entity: its own body, its part's body, or the body of its parts (0: none).</summary>
	private int BodySlotOf(Entity entity)
	{
		if (!_ecs.IsAlive(entity)) return 0;
		if (!_ecs.TryGet<Collider3D>(entity, out var collider))
		{
			return _bare.TryGetValue(entity, out var bare) && bare > 0 && bare < _highWater && Slots[bare].Alive && Slots[bare].Entity == entity ? bare : 0;
		}

		var slot = collider.BodySlot;
		if (slot <= 0 || slot >= _highWater || !Slots[slot].Alive) return 0;
		ref readonly var body = ref Slots[slot];
		if (body.Entity == entity) return slot;
		if (body.Children is null) return 0;
		foreach (ref readonly var child in body.Children.AsSpan())
		{
			if (child.Entity == entity) return slot;
		}

		return 0;
	}

	/// <summary>The entity of child <paramref name="childIndex"/> of a body (its own entity for a body that is not a compound).</summary>
	internal Entity EntityOf(int slot, int childIndex)
	{
		ref readonly var body = ref Slots[slot];
		return body.Children is { } children && (uint)childIndex < (uint)children.Length ? children[childIndex].Entity : body.Entity;
	}

	/// <summary>The collider of child <paramref name="childIndex"/> of a body (its own collider for a body that is not a compound).</summary>
	internal ref readonly Collider3D ColliderOf(int slot, int childIndex)
	{
		ref readonly var body = ref Slots[slot];
		if (body.Children is { } children && (uint)childIndex < (uint)children.Length) return ref children[childIndex].Collider;
		return ref body.Collider;
	}

	/// <summary>The layers and mask a body collides with: its collider's, or the union of a compound's shapes' (refined per shape).</summary>
	internal (uint Layer, uint Mask) FilterOf(int slot)
	{
		ref readonly var body = ref Slots[slot];
		return body.Children is null ? (body.Collider.Layer, body.Collider.Mask) : (body.LayerUnion, body.MaskUnion);
	}

	/// <summary>Whether the body is a sensor, or a compound with a sensor among its shapes.</summary>
	internal bool MaySense(int slot) => Slots[slot].Children is null ? Slots[slot].Collider.IsSensor : Slots[slot].AnySensor;

	/// <summary>Whether the body is a compound (its events and filtering are per child).</summary>
	internal bool IsCompound(int slot) => Slots[slot].Children is not null;

	/// <summary>The pose of child <paramref name="childIndex"/> of a compound relative to the body's pose (identity otherwise).</summary>
	internal Vector3 ChildOffset(int slot, int childIndex)
	{
		ref readonly var body = ref Slots[slot];
		if (body.Children is null) return default;
		var compound = _sim.Shapes.GetShape<Compound>(body.Shape.Index);
		return (uint)childIndex < (uint)compound.ChildCount ? compound.Children[childIndex].LocalPosition : default;
	}

	// ---- Pull ----

	private void PullBodies()
	{
		var active = _sim.Bodies.ActiveSet;
		var any = false;
		for (var i = 0; i < active.Count; i++)
		{
			var slot = BodyHandleToSlot[active.IndexToHandle[i].Value];
			if (slot == 0) continue;
			Slots[slot].Moved = _stamp;
			any = true;
		}

		if (!any) return;
		_childBodyCount = 0;

		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var hasRigid = chunk.Has<RigidBody3D>();
			var parented = chunk.Has<Parent>();
			// Parts move with their body; a part that is a body of its own is static.
			if (!hasRigid && parented) continue;
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			ref var transform0 = ref chunk.GetFirst<Transform>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody3D>() : ref Unsafe.NullRef<RigidBody3D>();

			for (var i = 0; i < count; i++)
			{
				var slot = Unsafe.Add(ref collider0, i).BodySlot;
				if (slot <= 0) continue;
				if (parented)
				{
					DeferChild(Unsafe.Add(ref entity0, i), slot);
					continue;
				}

				Pull(ref Slots[slot], ref Unsafe.Add(ref transform0, i), ref hasRigid ? ref Unsafe.Add(ref rigid0, i) : ref Unsafe.NullRef<RigidBody3D>());
			}
		}

		if (_bare.Count > 0)
		{
			foreach (ref var chunk in _ecs.Query(in BareBodies))
			{
				var parented = chunk.Has<Parent>();
				var count = chunk.Count;
				ref var entity0 = ref chunk.Entity(0);
				ref var transform0 = ref chunk.GetFirst<Transform>();
				ref var rigid0 = ref chunk.GetFirst<RigidBody3D>();
				for (var i = 0; i < count; i++)
				{
					var entity = Unsafe.Add(ref entity0, i);
					if (!_bare.TryGetValue(entity, out var slot) || slot <= 0) continue;
					if (parented) DeferChild(entity, slot);
					else Pull(ref Slots[slot], ref Unsafe.Add(ref transform0, i), ref Unsafe.Add(ref rigid0, i));
				}
			}
		}

		if (_childBodyCount == 0) return;

		// Bodies on child entities, shallowest first, so each is written relative to its parent's new pose.
		var children = _childBodies.AsSpan(0, _childBodyCount);
		children.Sort(ChildOrder);
		foreach (ref readonly var child in children)
		{
			ref var body = ref Slots[child.Slot];
			var reference = _sim.Bodies[body.Body];
			ref var rigid = ref _ecs.TryGetRef<RigidBody3D>(child.Entity, out var hasRigid);
			if (hasRigid)
			{
				rigid.LinearVelocity = reference.Velocity.Linear;
				rigid.AngularVelocity = reference.Velocity.Angular;
				body.Rigid = rigid;
			}

			// A kinematic child goes where its transform says (relative to its parent, whose new pose it follows next step).
			if (body.Rigid.Type == RigidBodyType3D.Kinematic) continue;

			ref readonly var pose = ref reference.Pose;
			var worldPosition = body.Center == Vector3.Zero ? pose.Position : pose.Position - Vector3.Transform(body.Center, pose.Orientation);
			ref var transform = ref _ecs.Get<Transform>(child.Entity);
			LocalOf(child.Entity, worldPosition, pose.Orientation, out var position, out var rotation);
			transform.Position = position;
			transform.Rotation = rotation;

			// What the next push composes from this transform, so an unchanged child is not teleported by rounding.
			var world = WorldOf(child.Entity, transform);
			body.Position = world.Position;
			body.Rotation = world.Rotation;
		}
	}

	private void DeferChild(Entity entity, int slot)
	{
		if (Slots[slot].Moved != _stamp || Slots[slot].IsStatic) return;
		if (_childBodyCount == _childBodies.Length) Array.Resize(ref _childBodies, _childBodies.Length * 2);
		_childBodies[_childBodyCount++] = new ChildBody(entity, slot, DepthOf(entity));
	}

	private static readonly Comparison<ChildBody> ChildOrder = static (a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Entity.Id.CompareTo(b.Entity.Id);

	private readonly record struct ChildBody(Entity Entity, int Slot, int Depth);

	private void Pull(ref BodySlot body, ref Transform transform, ref RigidBody3D rigid)
	{
		if (body.Moved != _stamp || body.IsStatic) return;
		var reference = _sim.Bodies[body.Body];
		ref readonly var pose = ref reference.Pose;
		transform.Rotation = pose.Orientation;
		transform.Position = body.Center == Vector3.Zero ? pose.Position : pose.Position - Vector3.Transform(body.Center, pose.Orientation);
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;

		if (!Unsafe.IsNullRef(ref rigid))
		{
			rigid.LinearVelocity = reference.Velocity.Linear;
			rigid.AngularVelocity = reference.Velocity.Angular;
			body.Rigid = rigid;
		}
	}

	// ---- Events ----

	private void EmitEvents()
	{
		// Merge the workers' pairs, sort them by key and drop duplicates (a pair can report more than one manifold).
		_currentCount = 0;
		for (var w = 0; w < WorkerPairs.Length; w++)
		{
			var buffer = WorkerPairs[w];
			for (var i = 0; i < buffer.Count; i++)
			{
				if (_currentCount == _current.Length) Array.Resize(ref _current, _current.Length * 2);
				_current[_currentCount++] = buffer.Pairs[i];
			}
		}

		var current = _current.AsSpan(0, _currentCount);
		current.Sort(PairOrder);
		var unique = 0;
		for (var i = 0; i < current.Length; i++)
		{
			if (unique > 0 && current[unique - 1].Key == current[i].Key) continue;
			current[unique++] = current[i];
		}

		_currentCount = unique;
		current = current[..unique];
		var previous = _previous.AsSpan(0, _previousCount);

		// Both are sorted: walk them together.
		int p = 0, c = 0;
		while (p < previous.Length || c < current.Length)
		{
			if (c == current.Length || (p < previous.Length && previous[p].Key < current[c].Key))
			{
				Emit(previous[p], ContactPhase3D.End);
				p++;
			}
			else if (p == previous.Length || current[c].Key < previous[p].Key)
			{
				Emit(current[c], ContactPhase3D.Begin);
				c++;
			}
			else
			{
				p++;
				c++;
			}
		}

		(_previous, _current) = (_current, _previous);
		_previousCount = _currentCount;
	}

	private void EndPairsOf(int slot)
	{
		var kept = 0;
		for (var i = 0; i < _previousCount; i++)
		{
			ref readonly var pair = ref _previous[i];
			if (pair.SlotA == slot || pair.SlotB == slot) Emit(pair, ContactPhase3D.End);
			else _previous[kept++] = pair;
		}

		_previousCount = kept;
	}

	private void Emit(in PairRecord pair, ContactPhase3D phase)
	{
		if (_events is null) return;
		var a = pair.EntityA;
		var b = pair.EntityB;
		if (pair.Sensor)
		{
			// The sensor is the one flagged as such (A when both are).
			var aIsSensor = pair.SensorIsA;
			_events.Emit(new Trigger3D(aIsSensor ? a : b, aIsSensor ? b : a, phase));
		}
		else if (phase == ContactPhase3D.Begin)
		{
			_events.Emit(new Collision3D(a, b, phase, pair.Point, pair.Normal));
		}
		else
		{
			_events.Emit(new Collision3D(a, b, phase, default, default));
		}
	}

	// Called from the narrow phase (possibly on worker threads, one buffer per worker). The child indices name the shapes
	// of compound bodies (0 for a body that is not one).
	internal void RecordPair(int workerIndex, int slotA, int childA, int slotB, int childB, Vector3 point, Vector3 normalAToB, bool sensor, bool sensorIsA)
	{
		if (slotA > slotB)
		{
			(slotA, slotB) = (slotB, slotA);
			(childA, childB) = (childB, childA);
			normalAToB = -normalAToB;
			sensorIsA = !sensorIsA;
		}

		WorkerPairs[workerIndex].Add(new PairRecord
		{
			// Slots below 2^24 and children below 256 (the key orders pairs; children only matter within compounds).
			Key = ((UInt128)(uint)slotA << 64) | ((UInt128)(uint)slotB << 32) | (uint)((childA & 0xFFFF) << 16 | (childB & 0xFFFF)),
			SlotA = slotA,
			SlotB = slotB,
			EntityA = EntityOf(slotA, childA),
			EntityB = EntityOf(slotB, childB),
			Point = point,
			Normal = normalAToB,
			Sensor = sensor,
			SensorIsA = sensorIsA,
		});
	}

	// ---- Queries ----

	/// <inheritdoc/>
	public ConvexHullId CreateConvexHull(ReadOnlySpan<Vector3> points)
	{
		if (points.Length < 4) throw new ArgumentException("A convex hull needs at least 4 points.", nameof(points));
		// Bepu takes a mutable span (it may reorder the points): hulls are created at load time, so copy.
		var hull = new ConvexHullShape(points.ToArray(), _pool, out var center);
		var shape = Simulation.Shapes.Add(hull);
		_hulls.Add(new HullEntry(hull, shape, center, HullVolume(hull)));
		return new ConvexHullId(_hulls.Count);
	}

	/// <summary>The volume of a hull: its faces fanned into tetrahedra with its center (its points are around it).</summary>
	private static float HullVolume(in ConvexHullShape hull)
	{
		var volume = 0f;
		for (var face = 0; face < hull.FaceToVertexIndicesStart.Length; face++)
		{
			hull.GetVertexIndicesForFace(face, out var indices);
			if (indices.Length < 3) continue;
			hull.GetPoint(indices[0], out var a);
			for (var i = 2; i < indices.Length; i++)
			{
				hull.GetPoint(indices[i - 1], out var b);
				hull.GetPoint(indices[i], out var c);
				volume += MathF.Abs(Vector3.Dot(a, Vector3.Cross(b, c))) / 6f;
			}
		}

		return volume;
	}

	/// <summary>The triangles of a hull in the entity's local space (for drawing it).</summary>
	internal (Vector3[] Positions, uint[] Indices) GetHullMesh(ConvexHullId id)
	{
		var entry = _hulls[id.Value - 1];
		var hull = entry.Hull;
		var positions = new List<Vector3>();
		var indices = new List<uint>();
		for (var face = 0; face < hull.FaceToVertexIndicesStart.Length; face++)
		{
			hull.GetVertexIndicesForFace(face, out var faceIndices);
			var first = (uint)positions.Count;
			for (var i = 0; i < faceIndices.Length; i++)
			{
				hull.GetPoint(faceIndices[i], out var point);
				positions.Add(point + entry.Center);
			}

			for (var i = 2; i < faceIndices.Length; i++)
			{
				indices.Add(first);
				indices.Add(first + (uint)i - 1);
				indices.Add(first + (uint)i);
			}
		}

		return ([.. positions], [.. indices]);
	}

	/// <inheritdoc/>
	public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit3D hit, uint mask = uint.MaxValue, bool includeSensors = false)
	{
		hit = default;
		if (direction.LengthSquared() == 0 || !(maxDistance > 0)) return false;
		var normalized = Vector3.Normalize(direction);
		var handler = new RayHandler(this, mask, includeSensors);
		Simulation.RayCast(origin, normalized, maxDistance, _pool, ref handler);
		if (!handler.Found) return false;
		var slot = SlotOf(handler.Collidable);
		hit = new RayHit3D(slot > 0 ? EntityOf(slot, handler.Child) : Entity.Null, origin + normalized * handler.T, handler.Normal, handler.T);
		return true;
	}

	/// <inheritdoc/>
	public unsafe int OverlapBox(Vector3 min, Vector3 max, Span<Entity> results, uint mask = uint.MaxValue)
	{
		var slots = stackalloc int[QueryCapacity];
		var collector = new OverlapCollector(this, mask, slots, QueryCapacity);
		var box = new BoundingBox(Vector3.Min(min, max), Vector3.Max(min, max));
		Simulation.BroadPhase.GetOverlaps(box, _pool, ref collector);
		return Gather(slots, collector.Count, results, default, -1);
	}

	/// <inheritdoc/>
	public unsafe int OverlapSphere(Vector3 center, float radius, Span<Entity> results, uint mask = uint.MaxValue)
	{
		var slots = stackalloc int[QueryCapacity];
		var extent = new Vector3(MathF.Max(radius, 0));
		var collector = new OverlapCollector(this, mask, slots, QueryCapacity);
		var box = new BoundingBox(center - extent, center + extent);
		Simulation.BroadPhase.GetOverlaps(box, _pool, ref collector);
		return Gather(slots, collector.Count, results, center, MathF.Max(radius, 0));
	}

	private const int QueryCapacity = 256;

	private unsafe int Gather(int* slots, int count, Span<Entity> results, Vector3 center, float radius)
	{
		// Broad-phase candidates in slot order (deterministic), filtered by the sphere against each body's bounds.
		new Span<int>(slots, count).Sort();
		var found = 0;
		for (var i = 0; i < count && found < results.Length; i++)
		{
			var slot = slots[i];
			if (i > 0 && slots[i - 1] == slot) continue;
			ref readonly var body = ref Slots[slot];
			if (radius >= 0)
			{
				var bounds = body.IsStatic ? _sim.Statics[body.Static].BoundingBox : _sim.Bodies[body.Body].BoundingBox;
				var closest = Vector3.Clamp(center, bounds.Min, bounds.Max);
				if (Vector3.DistanceSquared(closest, center) > radius * radius) continue;
			}

			results[found++] = body.Entity;
		}

		return found;
	}

	internal bool Passes(CollidableReference collidable, uint mask, bool includeSensors = true)
	{
		var slot = SlotOf(collidable);
		if (slot == 0) return false;
		// A compound passes when any of its shapes may; ray casts refine it per shape.
		if (IsCompound(slot)) return (Slots[slot].LayerUnion & mask) != 0;
		ref readonly var collider = ref Slots[slot].Collider;
		return (collider.Layer & mask) != 0 && (includeSensors || !collider.IsSensor);
	}

	internal bool PassesChild(CollidableReference collidable, int childIndex, uint mask, bool includeSensors)
	{
		var slot = SlotOf(collidable);
		if (slot == 0) return false;
		ref readonly var collider = ref ColliderOf(slot, childIndex);
		return (collider.Layer & mask) != 0 && (includeSensors || !collider.IsSensor);
	}

	/// <inheritdoc/>
	public bool TryGetMass(Entity entity, out float mass, out Vector3 localCenter)
	{
		var slot = BodySlotOf(entity);
		mass = 0;
		localCenter = default;
		if (slot == 0) return false;
		ref readonly var body = ref Slots[slot];
		localCenter = body.Center;
		if (!body.IsStatic)
		{
			var inverse = _sim.Bodies[body.Body].LocalInertia.InverseMass;
			mass = inverse > 0 ? 1f / inverse : 0f;
		}

		return true;
	}

	/// <inheritdoc/>
	public bool TryGetBodyEntity(Entity entity, out Entity body)
	{
		var slot = BodySlotOf(entity);
		body = slot == 0 ? Entity.Null : Slots[slot].Entity;
		return slot != 0;
	}

	/// <inheritdoc/>
	public bool ApplyLinearImpulse(Entity entity, Vector3 impulse)
	{
		var slot = BodySlotOf(entity);
		if (slot == 0 || Slots[slot].IsStatic || Slots[slot].Rigid.Type != RigidBodyType3D.Dynamic) return false;
		var reference = _sim.Bodies[Slots[slot].Body];
		reference.ApplyLinearImpulse(impulse);
		reference.Awake = true;
		return true;
	}

	/// <inheritdoc/>
	public bool ApplyAngularImpulse(Entity entity, Vector3 impulse)
	{
		var slot = BodySlotOf(entity);
		if (slot == 0 || Slots[slot].IsStatic || Slots[slot].Rigid.Type != RigidBodyType3D.Dynamic) return false;
		var reference = _sim.Bodies[Slots[slot].Body];
		reference.ApplyAngularImpulse(impulse);
		reference.Awake = true;
		return true;
	}

	/// <inheritdoc/>
	public ulong ComputeStateHash()
	{
		var sim = Simulation;
		var hash = 14695981039346656037UL;
		for (var slot = 1; slot < _highWater; slot++)
		{
			ref readonly var body = ref Slots[slot];
			if (!body.Alive) continue;
			hash = Mix(hash, (uint)slot);
			RigidPose pose;
			BodyVelocity velocity = default;
			if (body.IsStatic)
			{
				pose = sim.Statics[body.Static].Pose;
			}
			else
			{
				var reference = sim.Bodies[body.Body];
				pose = reference.Pose;
				velocity = reference.Velocity;
			}

			hash = Mix(hash, pose.Position);
			hash = Mix(hash, pose.Orientation.X);
			hash = Mix(hash, pose.Orientation.Y);
			hash = Mix(hash, pose.Orientation.Z);
			hash = Mix(hash, pose.Orientation.W);
			hash = Mix(hash, velocity.Linear);
			hash = Mix(hash, velocity.Angular);
		}

		return hash;
	}

	private static ulong Mix(ulong hash, Vector3 value) => Mix(Mix(Mix(hash, value.X), value.Y), value.Z);

	private static ulong Mix(ulong hash, float value) => Mix(hash, (uint)BitConverter.SingleToInt32Bits(value));

	private static ulong Mix(ulong hash, uint value)
	{
		for (var i = 0; i < 4; i++)
		{
			hash ^= (byte)(value >> (8 * i));
			hash *= 1099511628211UL;
		}

		return hash;
	}

	// ---- Debug drawing ----

	/// <summary>Draws every collider through <paramref name="sink"/>: its shape, world matrix (shape scaled) and color.</summary>
	internal void Draw(IDebugShapeSink sink)
	{
		var sim = Simulation;
		for (var slot = 1; slot < _highWater; slot++)
		{
			ref readonly var body = ref Slots[slot];
			if (!body.Alive) continue;

			RigidPose pose;
			DebugColor3D color;
			if (body.IsStatic)
			{
				pose = sim.Statics[body.Static].Pose;
				color = DebugColor3D.Static;
			}
			else
			{
				var reference = sim.Bodies[body.Body];
				pose = reference.Pose;
				color = body.Rigid.Type == RigidBodyType3D.Kinematic ? DebugColor3D.Kinematic : reference.Awake ? DebugColor3D.Dynamic : DebugColor3D.Sleeping;
			}

			var world = Matrix4x4.CreateFromQuaternion(pose.Orientation) * Matrix4x4.CreateTranslation(pose.Position);
			if (body.Children is { } children)
			{
				var compound = sim.Shapes.GetShape<Compound>(body.Shape.Index);
				for (var i = 0; i < children.Length && i < compound.ChildCount; i++)
				{
					ref readonly var child = ref compound.Children[i];
					var local = Matrix4x4.CreateFromQuaternion(child.LocalOrientation) * Matrix4x4.CreateTranslation(child.LocalPosition);
					var hullCenter = children[i].Collider.Shape == ColliderShape3D.ConvexHull ? _hulls[children[i].Collider.Hull.Value - 1].Center : Vector3.Zero;
					DrawShape(sink, children[i].Collider, local * world, children[i].Collider.IsSensor ? DebugColor3D.Sensor : color, hullCenter);
				}

				continue;
			}

			if (body.Collider.IsSensor) color = DebugColor3D.Sensor;
			DrawShape(sink, body.Collider, world, color, body.Center);
		}
	}

	private static void DrawShape(IDebugShapeSink sink, in Collider3D collider, in Matrix4x4 world, DebugColor3D color, Vector3 hullCenter)
	{
		switch (collider.Shape)
		{
			case ColliderShape3D.Sphere:
				sink.Shape(DebugShape3D.Sphere, Matrix4x4.CreateScale(collider.Radius * 2) * world, color, default);
				break;
			case ColliderShape3D.Cylinder:
				sink.Shape(DebugShape3D.Cylinder, Matrix4x4.CreateScale(collider.Radius * 2, collider.Length, collider.Radius * 2) * world, color, default);
				break;
			case ColliderShape3D.Capsule:
				sink.Shape(DebugShape3D.Cylinder, Matrix4x4.CreateScale(collider.Radius * 2, collider.Length, collider.Radius * 2) * world, color, default);
				sink.Shape(DebugShape3D.Sphere, Matrix4x4.CreateScale(collider.Radius * 2) * Matrix4x4.CreateTranslation(0, collider.Length / 2, 0) * world, color, default);
				sink.Shape(DebugShape3D.Sphere, Matrix4x4.CreateScale(collider.Radius * 2) * Matrix4x4.CreateTranslation(0, -collider.Length / 2, 0) * world, color, default);
				break;
			case ColliderShape3D.ConvexHull:
				// The hull mesh is in the entity's space: undo the hull's center offset.
				sink.Shape(DebugShape3D.Hull, Matrix4x4.CreateTranslation(-hullCenter) * world, color, collider.Hull);
				break;
			default:
				sink.Shape(DebugShape3D.Box, Matrix4x4.CreateScale(collider.Size) * world, color, default);
				break;
		}
	}

	/// <summary>Removes every body and disposes the simulation, its memory and the thread dispatcher.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_sim.Dispose();
		_dispatcher?.Dispose();
		_pool.Clear();
	}

	internal struct BodySlot
	{
		public bool Alive;
		public bool IsStatic;
		public bool Driven;
		public bool OwnsShape;

		/// <summary>Whether the entity has a collider of its own (else it is a rigid body that is the body of its parts).</summary>
		public bool HasOwnCollider;

		/// <summary>A compound's shapes (the own collider first, then the parts), or null.</summary>
		public CompoundPart[]? Children;
		public TypedIndex[]? ChildShapes;
		public uint LayerUnion;
		public uint MaskUnion;
		public bool AnySensor;

		/// <summary>The body's own collider as synced (<see cref="Collider"/> is the material of a compound).</summary>
		public Collider3D OwnCollider;
		public int Generation;
		public Entity Entity;
		public BodyHandle Body;
		public StaticHandle Static;
		public TypedIndex Shape;
		public Vector3 Center;
		public Collider3D Collider;
		public RigidBody3D Rigid;
		public Vector3 Position;
		public Quaternion Rotation;
		public uint Seen;
		public uint Moved;
	}

	private struct JointSlot
	{
		public bool Alive;
		public Entity Entity;
		public Joint3D Definition;
		public int SlotA;
		public int SlotB;
		public int GenerationA;
		public int GenerationB;
		public ConstraintHandle Constraint;
		public uint Seen;
	}

	private readonly record struct HullEntry(ConvexHullShape Hull, TypedIndex Shape, Vector3 Center, float Volume);

	/// <summary>A part found this step: its body, its entity, its collider and its pose in the body's space.</summary>
	internal readonly record struct PartRecord(Entity Body, Entity Entity, Collider3D Collider, RigidPose Pose);

	/// <summary>One shape of a compound body: whose it is, its collider, and its pose in the body entity's space.</summary>
	internal struct CompoundPart(Entity entity, Collider3D collider, RigidPose pose)
	{
		public Entity Entity = entity;
		public Collider3D Collider = collider;
		public RigidPose Pose = pose;
		public bool OwnsShape;
	}

	private static readonly Comparison<PartRecord> PartOrder = static (x, y) => x.Body.Id != y.Body.Id ? x.Body.Id.CompareTo(y.Body.Id) : x.Entity.Id.CompareTo(y.Entity.Id);

	internal struct PairRecord
	{
		public UInt128 Key;
		public int SlotA;
		public int SlotB;
		public Entity EntityA;
		public Entity EntityB;
		public Vector3 Point;
		public Vector3 Normal;
		public bool Sensor;
		public bool SensorIsA;
	}

	// A cached delegate: sorting with a struct IComparer would box it on every call.
	private static readonly Comparison<PairRecord> PairOrder = static (x, y) => x.Key.CompareTo(y.Key);

	internal sealed class PairBuffer
	{
		public PairRecord[] Pairs = new PairRecord[64];
		public int Count;

		public void Add(in PairRecord pair)
		{
			if (Count == Pairs.Length) Array.Resize(ref Pairs, Pairs.Length * 2);
			Pairs[Count++] = pair;
		}
	}

	private struct RayHandler(PhysicsWorld3D world, uint mask, bool includeSensors) : IRayHitHandler
	{
		public bool Found;
		public float T = float.MaxValue;
		public Vector3 Normal;
		public CollidableReference Collidable;
		public int Child;

		// Sensors are skipped before the narrow phase, so the ray goes on to the closest collider behind them.
		public readonly bool AllowTest(CollidableReference collidable) => world.Passes(collidable, mask, includeSensors);

		// A compound's shapes are filtered one by one.
		public readonly bool AllowTest(CollidableReference collidable, int childIndex) => world.PassesChild(collidable, childIndex, mask, includeSensors);

		public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal, CollidableReference collidable, int childIndex)
		{
			if (t >= T) return;
			T = t;
			maximumT = t;
			Normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : normal;
			Collidable = collidable;
			Child = childIndex;
			Found = true;
		}
	}

	private unsafe struct OverlapCollector(PhysicsWorld3D world, uint mask, int* slots, int capacity) : IBreakableForEach<CollidableReference>
	{
		public int Count;

		public bool LoopBody(CollidableReference collidable)
		{
			if (!world.Passes(collidable, mask)) return true;
			if (Count >= capacity) return false;
			slots[Count++] = world.SlotOf(collidable);
			return true;
		}
	}
}

/// <summary>The shapes of the 3D debug drawing (unit meshes scaled by the world matrix).</summary>
internal enum DebugShape3D : byte
{
	Box,
	Sphere,
	Cylinder,
	Hull,
}

/// <summary>The colors of the 3D debug drawing.</summary>
internal enum DebugColor3D : byte
{
	Static,
	Kinematic,
	Dynamic,
	Sleeping,
	Sensor,
}

/// <summary>Where the 3D debug drawing goes (the 3D renderer, or a test recorder).</summary>
internal interface IDebugShapeSink
{
	void Shape(DebugShape3D shape, in Matrix4x4 world, DebugColor3D color, ConvexHullId hull);
}
