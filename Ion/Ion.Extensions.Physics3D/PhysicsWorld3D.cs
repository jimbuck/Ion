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

using Ion.Extensions.Graphics;

using ConvexHullShape = BepuPhysics.Collidables.ConvexHull;

namespace Ion.Extensions.Physics3D;

/// <summary>
/// The BepuPhysics v2 simulation of one scope (the root, or a scene) and its adapter to the scope's ECS
/// <see cref="World"/>: every entity with a <see cref="Collider3D"/> and a <see cref="Transform"/> is a body (a Bepu static
/// unless it has a <see cref="RigidBody3D"/>), every <see cref="Joint3D"/> a constraint.
/// </summary>
/// <remarks>
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
	private static readonly QueryDescription Joints = new QueryDescription().WithAll<Joint3D>();

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
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			ref var transform0 = ref chunk.GetFirst<Transform>();
			var hasRigid = chunk.Has<RigidBody3D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody3D>() : ref Unsafe.NullRef<RigidBody3D>();

			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var collider = ref Unsafe.Add(ref collider0, i);
				ref readonly var transform = ref Unsafe.Add(ref transform0, i);
				var rigid = hasRigid ? Unsafe.Add(ref rigid0, i) : RigidBody3D.Static();

				var slot = collider.BodySlot;
				if (slot <= 0 || slot >= _highWater || !Slots[slot].Alive || Slots[slot].Entity != entity)
				{
					collider.BodySlot = CreateBody(entity, collider, transform, rigid);
					continue;
				}

				ref var body = ref Slots[slot];
				body.Seen = _stamp;

				// Structural changes (type, shape, continuity) rebuild the body.
				if (rigid.Type != body.Rigid.Type || rigid.IsBullet != body.Rigid.IsBullet || !Collider3D.SameShape(collider, body.Collider))
				{
					DestroyBody(slot, emitEnds: true);
					collider.BodySlot = CreateBody(entity, collider, transform, rigid);
					continue;
				}

				Push(ref body, transform, rigid, dt);
			}
		}
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

	private int CreateBody(Entity entity, in Collider3D collider, in Transform transform, in RigidBody3D rigid)
	{
		Validate(entity, collider, rigid);
		var slot = AllocateSlot();
		ref var body = ref Slots[slot];
		body = default;
		body.Alive = true;
		body.Entity = entity;
		body.Collider = collider;
		body.Rigid = rigid;
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;
		body.Seen = _stamp;
		body.Generation++;

		TypedIndex shape;
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

		body.Shape = shape;
		var pose = BodyPose(transform.Position, transform.Rotation, body.Center);
		if (rigid.Type == RigidBodyType3D.Static)
		{
			body.IsStatic = true;
			body.Static = _sim.Statics.Add(new StaticDescription(pose, shape));
			SetHandleSlot(ref StaticHandleToSlot, body.Static.Value, slot);
		}
		else
		{
			var continuity = rigid.IsBullet ? ContinuousDetection.Continuous(1e-3f, 1e-3f) : ContinuousDetection.Discrete;
			var collidable = new CollidableDescription(shape, continuity);
			var velocity = new BodyVelocity(rigid.LinearVelocity, rigid.AngularVelocity);
			var description = rigid.Type == RigidBodyType3D.Kinematic
				? BodyDescription.CreateKinematic(pose, velocity, collidable, _activity)
				: BodyDescription.CreateDynamic(pose, velocity, Inertia(collider, rigid), collidable, _activity);
			body.Body = _sim.Bodies.Add(description);
			SetHandleSlot(ref BodyHandleToSlot, body.Body.Value, slot);
		}

		_bodyCount++;
		return slot;
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

		if (body.OwnsShape) _sim.Shapes.Remove(body.Shape);
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

	private int BodySlotOf(Entity entity)
	{
		if (!_ecs.IsAlive(entity) || !_ecs.TryGet<Collider3D>(entity, out var collider)) return 0;
		var slot = collider.BodySlot;
		return slot > 0 && slot < _highWater && Slots[slot].Alive && Slots[slot].Entity == entity ? slot : 0;
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

		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var count = chunk.Count;
			ref var collider0 = ref chunk.GetFirst<Collider3D>();
			ref var transform0 = ref chunk.GetFirst<Transform>();
			var hasRigid = chunk.Has<RigidBody3D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody3D>() : ref Unsafe.NullRef<RigidBody3D>();

			for (var i = 0; i < count; i++)
			{
				var slot = Unsafe.Add(ref collider0, i).BodySlot;
				ref var body = ref Slots[slot];
				if (body.Moved != _stamp || body.IsStatic) continue;

				var reference = _sim.Bodies[body.Body];
				ref readonly var pose = ref reference.Pose;
				ref var transform = ref Unsafe.Add(ref transform0, i);
				transform.Rotation = pose.Orientation;
				transform.Position = body.Center == Vector3.Zero ? pose.Position : pose.Position - Vector3.Transform(body.Center, pose.Orientation);
				body.Position = transform.Position;
				body.Rotation = transform.Rotation;

				if (hasRigid)
				{
					ref var rigid = ref Unsafe.Add(ref rigid0, i);
					rigid.LinearVelocity = reference.Velocity.Linear;
					rigid.AngularVelocity = reference.Velocity.Angular;
					body.Rigid = rigid;
				}
			}
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

	// Called from the narrow phase (possibly on worker threads, one buffer per worker).
	internal void RecordPair(int workerIndex, int slotA, int slotB, Vector3 point, Vector3 normalAToB, bool sensor, bool sensorIsA)
	{
		if (slotA > slotB)
		{
			(slotA, slotB) = (slotB, slotA);
			normalAToB = -normalAToB;
			sensorIsA = !sensorIsA;
		}

		ref readonly var a = ref Slots[slotA];
		ref readonly var b = ref Slots[slotB];
		WorkerPairs[workerIndex].Add(new PairRecord
		{
			Key = ((ulong)(uint)slotA << 32) | (uint)slotB,
			SlotA = slotA,
			SlotB = slotB,
			EntityA = a.Entity,
			EntityB = b.Entity,
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
		_hulls.Add(new HullEntry(hull, shape, center));
		return new ConvexHullId(_hulls.Count);
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
	public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit3D hit, uint mask = uint.MaxValue)
	{
		hit = default;
		if (direction.LengthSquared() == 0 || !(maxDistance > 0)) return false;
		var normalized = Vector3.Normalize(direction);
		var handler = new RayHandler(this, mask);
		Simulation.RayCast(origin, normalized, maxDistance, _pool, ref handler);
		if (!handler.Found) return false;
		var slot = SlotOf(handler.Collidable);
		hit = new RayHit3D(slot > 0 ? Slots[slot].Entity : Entity.Null, origin + normalized * handler.T, handler.Normal, handler.T);
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

	internal bool Passes(CollidableReference collidable, uint mask)
	{
		var slot = SlotOf(collidable);
		return slot > 0 && (Slots[slot].Collider.Layer & mask) != 0;
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

			if (body.Collider.IsSensor) color = DebugColor3D.Sensor;
			var world = Matrix4x4.CreateFromQuaternion(pose.Orientation) * Matrix4x4.CreateTranslation(pose.Position);
			ref readonly var collider = ref body.Collider;
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
					// The hull mesh is in the entity's space: undo the body's center offset.
					sink.Shape(DebugShape3D.Hull, Matrix4x4.CreateTranslation(-body.Center) * world, color, collider.Hull);
					break;
				default:
					sink.Shape(DebugShape3D.Box, Matrix4x4.CreateScale(collider.Size) * world, color, default);
					break;
			}
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

	private readonly record struct HullEntry(ConvexHullShape Hull, TypedIndex Shape, Vector3 Center);

	internal struct PairRecord
	{
		public ulong Key;
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

	private struct RayHandler(PhysicsWorld3D world, uint mask) : IRayHitHandler
	{
		public bool Found;
		public float T = float.MaxValue;
		public Vector3 Normal;
		public CollidableReference Collidable;

		public readonly bool AllowTest(CollidableReference collidable) => world.Passes(collidable, mask);

		public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

		public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal, CollidableReference collidable, int childIndex)
		{
			if (t >= T) return;
			T = t;
			maximumT = t;
			Normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : normal;
			Collidable = collidable;
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
