using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Arch.Core;

using Ion.Extensions.Ecs;

using B2 = Box2D.NET.Bindings.B2;

[assembly: DisableRuntimeMarshalling]

namespace Ion.Extensions.Physics2D;

/// <summary>
/// The Box2D v3 world of one scope (the root, or a scene) and its adapter to the scope's ECS <see cref="World"/>: every
/// entity with a <c>Transform2D</c> and a <see cref="RigidBody2D"/>, or a root one with a <see cref="Collider2D"/>, is a
/// body (static without a rigid body); a child collider without a rigid body is a part of the nearest ancestor body (a
/// compound body); every <see cref="Joint2D"/> is a joint.
/// </summary>
/// <remarks>
/// <para>
/// Compound bodies: a part's shape is placed on its body by the <c>Transform2D</c>s between them, as the transform
/// propagation places it (ancestors' positions, rotations, and scales applied to positions; a collider's own geometry is
/// not scaled), and contributes to the body's mass. Contacts, sensor events and queries report the part's entity. A rigid
/// body without a collider of its own is a body only while it has parts. A child collider with no body above it is a
/// static body of its own at its world position.
/// </para>
/// <para>
/// A rigid body on a child entity is simulated in world space: its pose is its <c>Transform2D</c> composed with its
/// ancestors', and the simulated pose is written back relative to its parent (after its ancestors' bodies are written),
/// so it follows a parent the game moves (a kinematic child is driven along) and stays where the simulation put it
/// otherwise.
/// </para>
/// <para>
/// <see cref="Step"/> (run by <see cref="Physics2DSystem"/> in FixedUpdate at <see cref="StageOrder.Physics"/>) does, in
/// order: create the bodies of new colliders and push what game code changed since the last step (transforms, velocities,
/// body settings, collider shapes; a kinematic body is driven to its transform, any other body teleported), destroy the
/// bodies of entities that lost their collider or were destroyed, create, recreate or destroy joints, step Box2D by the
/// fixed delta with <see cref="Physics2DConfig.SubSteps"/>, pull the transforms and velocities of the bodies that moved
/// back into <c>Transform2D</c> and <see cref="RigidBody2D"/>, and emit <see cref="Collision2D"/> and
/// <see cref="Trigger2D"/> events.
/// </para>
/// <para>
/// Deterministic: the only inputs are the components, the fixed delta and the order entities are iterated in (Arch's,
/// itself deterministic); Box2D runs single-threaded. Bit-identical results across processor architectures need natives
/// built without fused multiply-add contraction (see <c>docs/design/ion-physics.md</c>).
/// </para>
/// <para>Nothing is allocated per step once the slot tables have grown to the scene.</para>
/// </remarks>
public sealed unsafe class PhysicsWorld2D : IPhysicsWorld2D, IDisposable
{
	private static readonly QueryDescription Bodies = new QueryDescription().WithAll<Collider2D, Transform2D>();
	private static readonly QueryDescription BareBodies = new QueryDescription().WithAll<RigidBody2D, Transform2D>().WithNone<Collider2D>();
	private static readonly QueryDescription Joints = new QueryDescription().WithAll<Joint2D>();

	// The deepest hierarchy searched for a part's body.
	private const int MaxPartDepth = 64;

	// Box2D's world table is global and not thread-safe to create and destroy worlds in.
	private static readonly Lock WorldTableGate = new();

	private readonly World _ecs;
	private readonly IEvents? _events;
	private readonly Physics2DConfig _config;
	private readonly float _toPhysics;
	private readonly float _toWorld;
	private B2.WorldId _id;

	private BodySlot[] _bodies = new BodySlot[64];
	private int _bodyHighWater = 1; // slot 0 means "none"
	private int[] _freeBodies = new int[16];
	private int _freeBodyCount;
	private int _bodyCount;

	// Compound parts (a part's Collider2D.BodySlot is the negated part slot), and the bodies of rigid bodies without a
	// collider of their own (bodies of their parts).
	private PartSlot[] _parts = new PartSlot[16];
	private int _partHighWater = 1;
	private int[] _freeParts = new int[8];
	private int _freePartCount;
	private int _partCount;
	private readonly Dictionary<Entity, int> _bare = [];

	// Rigid bodies on child entities that moved this step, written back after the root bodies, shallowest first.
	private ChildBody[] _childBodies = new ChildBody[8];
	private int _childBodyCount;

	private JointSlot[] _joints = new JointSlot[16];
	private int _jointHighWater = 1;
	private int[] _freeJoints = new int[8];
	private int _freeJointCount;
	private int _jointCount;

	// Box2D shape index (ShapeId.index1) to the slot that created it, kept after the shape is destroyed so that the end
	// events Box2D reports for destroyed shapes still name their entity.
	private ShapeRef[] _shapes = new ShapeRef[64];

	private uint _stamp;
	private long _steps;
	private bool _disposed;

	/// <summary>Creates a world for the entities of <paramref name="ecs"/>, emitting events on <paramref name="events"/> (none when null).</summary>
	public PhysicsWorld2D(World ecs, IEvents? events, Physics2DConfig? config = null)
	{
		ArgumentNullException.ThrowIfNull(ecs);
		_ecs = ecs;
		_events = events;
		_config = config ?? new Physics2DConfig();
		if (!(_config.UnitsPerMeter > 0)) throw new ArgumentOutOfRangeException(nameof(config), _config.UnitsPerMeter, "Physics2DConfig.UnitsPerMeter must be positive.");
		if (_config.SubSteps < 1) throw new ArgumentOutOfRangeException(nameof(config), _config.SubSteps, "Physics2DConfig.SubSteps must be at least 1.");
		_toPhysics = 1f / _config.UnitsPerMeter;
		_toWorld = _config.UnitsPerMeter;
		DebugDraw = _config.DebugDraw;
		Physics2DComponents.Register();

		var def = B2.DefaultWorldDef();
		def.gravity = ToB2(_config.Gravity * _toPhysics);
		def.enableSleep = _config.EnableSleep;
		def.enableContinuous = _config.EnableContinuous;
		def.contactHertz = _config.ContactHertz;
		def.contactDampingRatio = _config.ContactDampingRatio;
		if (_config.RestitutionThreshold > 0) def.restitutionThreshold = _config.RestitutionThreshold * _toPhysics;
		if (_config.MaxLinearSpeed > 0) def.maximumLinearSpeed = _config.MaxLinearSpeed * _toPhysics;
		def.workerCount = 1;
		lock (WorldTableGate) _id = B2.CreateWorld(&def);
	}

	/// <summary>The ECS world whose entities this world simulates.</summary>
	public World Entities => _ecs;

	/// <summary>The settings the world was created with.</summary>
	public Physics2DConfig Config => _config;

	/// <inheritdoc/>
	public Vector2 Gravity
	{
		get => FromB2(B2.WorldGetGravity(Id)) * _toWorld;
		set => B2.WorldSetGravity(Id, ToB2(value * _toPhysics));
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

	/// <summary>The number of awake bodies (Box2D skips sleeping ones).</summary>
	public int AwakeBodyCount => B2.WorldGetAwakeBodyCount(Id);

	private B2.WorldId Id
	{
		get
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			return _id;
		}
	}

	/// <summary>
	/// Runs one fixed step of <paramref name="dt"/> seconds: synchronizes the entities into the world, steps it, writes the
	/// results back and emits the events (see the remarks of <see cref="PhysicsWorld2D"/>).
	/// </summary>
	public void Step(float dt)
	{
		var id = Id;
		if (!(dt > 0)) return;

		_stamp++;
		PushBodies(dt);
		RemoveUnseenBodies();
		SyncJoints();

		B2.WorldStep(id, dt, _config.SubSteps);
		_steps++;

		PullBodies();
		EmitEvents();
	}

	/// <summary>Steps Box2D alone, without the ECS synchronization or events (the benchmarks' baseline for the adapter's cost).</summary>
	internal void SimulateOnly(float dt) => B2.WorldStep(Id, dt, _config.SubSteps);

	// ---- Push: entities to Box2D ----

	private void PushBodies(float dt)
	{
		// Bodies with a collider: roots, and entities with a rigid body (on a child, at its world pose).
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var hasRigid = chunk.Has<RigidBody2D>();
			var parented = chunk.Has<Parent>();
			if (!hasRigid && parented) continue;   // parts, below
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider2D>();
			ref var transform0 = ref chunk.GetFirst<Transform2D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody2D>() : ref Unsafe.NullRef<RigidBody2D>();

			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var collider = ref Unsafe.Add(ref collider0, i);
				ref var transform = ref Unsafe.Add(ref transform0, i);
				var rigid = hasRigid ? Unsafe.Add(ref rigid0, i) : RigidBody2D.Static();
				PushOwnBody(entity, ref collider, parented ? WorldOf(entity, transform) : transform, rigid, dt);
			}
		}

		// Rigid bodies without a collider of their own that already have a body (it lives while they have parts).
		if (_bare.Count > 0)
		{
			foreach (ref var chunk in _ecs.Query(in BareBodies))
			{
				var parented = chunk.Has<Parent>();
				var count = chunk.Count;
				ref var entity0 = ref chunk.Entity(0);
				ref var transform0 = ref chunk.GetFirst<Transform2D>();
				ref var rigid0 = ref chunk.GetFirst<RigidBody2D>();
				for (var i = 0; i < count; i++)
				{
					var entity = Unsafe.Add(ref entity0, i);
					if (!_bare.TryGetValue(entity, out var slot)) continue;
					var transform = parented ? WorldOf(entity, Unsafe.Add(ref transform0, i)) : Unsafe.Add(ref transform0, i);
					Push(ref _bodies[slot], default, transform, Unsafe.Add(ref rigid0, i), dt);
				}
			}
		}

		// Parts: child colliders without a rigid body, on the nearest ancestor body.
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			if (chunk.Has<RigidBody2D>() || !chunk.Has<Parent>()) continue;
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider2D>();
			ref var transform0 = ref chunk.GetFirst<Transform2D>();
			for (var i = 0; i < count; i++) PushPart(Unsafe.Add(ref entity0, i), ref Unsafe.Add(ref collider0, i), Unsafe.Add(ref transform0, i), dt);
		}

		RemoveUnseenParts();

		// Shapes came and went: the masses of their bodies are recomputed once.
		for (var slot = 1; slot < _bodyHighWater; slot++)
		{
			ref var body = ref _bodies[slot];
			if (!body.Alive || !body.MassDirty) continue;
			ApplyMass(body.Body, body.Rigid);
			body.MassDirty = false;
		}
	}

	private void PushOwnBody(Entity entity, ref Collider2D collider, in Transform2D transform, in RigidBody2D rigid, float dt)
	{
		var slot = collider.BodySlot;
		if (slot <= 0 || slot >= _bodyHighWater || !_bodies[slot].Alive || _bodies[slot].Entity != entity)
		{
			collider.BodySlot = CreateBody(entity, collider, transform, rigid, hasCollider: true);
			return;
		}

		ref var body = ref _bodies[slot];
		body.Seen = _stamp;
		Push(ref body, collider, transform, rigid, dt);
	}

	private void PushPart(Entity entity, ref Collider2D collider, in Transform2D transform, float dt)
	{
		var owner = FindBody(entity, transform, out var position, out var rotation);
		if (owner <= 0)
		{
			// No body above it: a static body of its own, at its world position.
			PushOwnBody(entity, ref collider, new Transform2D(position, rotation), RigidBody2D.Static(), dt);
			return;
		}

		ref var body = ref _bodies[owner];
		body.Seen = _stamp;   // a body without a collider of its own lives while it has parts

		var partSlot = -collider.BodySlot;
		if (partSlot > 0 && partSlot < _partHighWater && _parts[partSlot].Alive && _parts[partSlot].Entity == entity)
		{
			ref var part = ref _parts[partSlot];
			part.Seen = _stamp;
			if (part.Owner == owner && SameId(part.OwnerBody, body.Body) && part.Position == position && part.Rotation == rotation && Collider2D.SameShape(collider, part.Collider)) return;

			// Moved within its body, reshaped, or onto another body: its shape is made again.
			DestroyPartShape(ref part);
			Attach(ref part, ref body, collider, position, rotation);
			return;
		}

		Validate(entity, collider);
		partSlot = AllocatePartSlot();
		ref var created = ref _parts[partSlot];
		created = new PartSlot { Alive = true, Entity = entity, Seen = _stamp };
		Attach(ref created, ref body, collider, position, rotation);
		collider.BodySlot = -partSlot;
		_partCount++;
	}

	private void Attach(ref PartSlot part, ref BodySlot body, in Collider2D collider, Vector2 position, float rotation)
	{
		part.Owner = body.Slot;
		part.OwnerBody = body.Body;
		part.Collider = collider;
		part.Position = position;
		part.Rotation = rotation;
		part.Shape = CreateShape(body.Body, body.Slot, Placed(collider, position, rotation), part.Entity);
		body.MassDirty = true;
	}

	private void DestroyPartShape(ref PartSlot part)
	{
		// The body's destruction may have taken the shape with it.
		if (!B2.ShapeIsValid(part.Shape)) return;
		B2.DestroyShape(part.Shape, true);
		if (part.Owner > 0 && part.Owner < _bodyHighWater && _bodies[part.Owner].Alive && SameId(_bodies[part.Owner].Body, part.OwnerBody)) _bodies[part.Owner].MassDirty = true;
	}

	private void RemoveUnseenParts()
	{
		for (var slot = 1; slot < _partHighWater; slot++)
		{
			ref var part = ref _parts[slot];
			if (!part.Alive || part.Seen == _stamp) continue;
			DestroyPartShape(ref part);
			part.Alive = false;
			part.Entity = Entity.Null;
			if (_freePartCount == _freeParts.Length) Array.Resize(ref _freeParts, _freeParts.Length * 2);
			_freeParts[_freePartCount++] = slot;
			_partCount--;
		}
	}

	private int AllocatePartSlot()
	{
		if (_freePartCount > 0) return _freeParts[--_freePartCount];
		if (_partHighWater == _parts.Length) Array.Resize(ref _parts, _parts.Length * 2);
		return _partHighWater++;
	}

	/// <summary>
	/// The body slot of a part's nearest ancestor body (an entity with a rigid body, or a root with a collider), and the
	/// part's pose in that body's space; or 0, with the part's world pose, when no ancestor is a body. Transforms compose as
	/// the transform propagation composes them: an ancestor's scale scales the positions below it.
	/// </summary>
	private int FindBody(Entity entity, in Transform2D transform, out Vector2 position, out float rotation)
	{
		position = transform.Position;
		rotation = transform.Rotation;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) return 0;
			current = parent.Value;
			var hasTransform = _ecs.TryGet<Transform2D>(current, out var between);
			if (_ecs.Has<RigidBody2D>(current) || (_ecs.Has<Collider2D>(current) && !_ecs.Has<Parent>(current)))
			{
				// The body's own scale scales its parts' positions too (not its pose).
				if (hasTransform) position *= between.Scale;
				return BodySlotOf(current);
			}

			// An entity in between (a group, or another part): its transform places the part.
			if (hasTransform)
			{
				position = between.Position + RotateBy(between.Rotation, between.Scale * position);
				rotation += between.Rotation;
			}
		}

		return 0;
	}

	/// <summary>The world pose of a child entity: its transform composed with its ancestors' (as the transform propagation does).</summary>
	private Transform2D WorldOf(Entity entity, in Transform2D local)
	{
		var position = local.Position;
		var rotation = local.Rotation;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) break;
			current = parent.Value;
			if (!_ecs.TryGet<Transform2D>(current, out var ancestor)) continue;
			position = ancestor.Position + RotateBy(ancestor.Rotation, ancestor.Scale * position);
			rotation += ancestor.Rotation;
		}

		return new Transform2D(position, rotation);
	}

	/// <summary>The local pose under <paramref name="entity"/>'s parent of a world pose: the inverse of <see cref="WorldOf"/> (shear aside).</summary>
	private void LocalOf(Entity entity, Vector2 worldPosition, float worldRotation, out Vector2 position, out float rotation)
	{
		// The parent's world frame: its position, rotation and accumulated scale.
		var framePosition = Vector2.Zero;
		var frameRotation = 0f;
		var frameScale = Vector2.One;
		var first = true;
		var current = entity;
		for (var depth = 0; depth < MaxPartDepth; depth++)
		{
			if (!_ecs.TryGet<Parent>(current, out var parent) || !_ecs.IsAlive(parent.Value)) break;
			current = parent.Value;
			if (!_ecs.TryGet<Transform2D>(current, out var ancestor)) continue;
			if (first)
			{
				framePosition = ancestor.Position;
				frameRotation = ancestor.Rotation;
				frameScale = ancestor.Scale;
				first = false;
				continue;
			}

			framePosition = ancestor.Position + RotateBy(ancestor.Rotation, ancestor.Scale * framePosition);
			frameRotation += ancestor.Rotation;
			frameScale *= ancestor.Scale;
		}

		var relative = RotateBy(-frameRotation, worldPosition - framePosition);
		position = new Vector2(frameScale.X != 0 ? relative.X / frameScale.X : relative.X, frameScale.Y != 0 ? relative.Y / frameScale.Y : relative.Y);
		rotation = worldRotation - frameRotation;
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

	/// <summary>The body slot of a body entity, creating the body of a rigid body without a collider of its own (0 without a <c>Transform2D</c>).</summary>
	private int BodySlotOf(Entity owner)
	{
		if (_ecs.TryGet<Collider2D>(owner, out var collider))
		{
			var slot = collider.BodySlot;
			return slot > 0 && slot < _bodyHighWater && _bodies[slot].Alive && _bodies[slot].Entity == owner ? slot : 0;
		}

		if (_bare.TryGetValue(owner, out var bare)) return bare;
		if (!_ecs.TryGet<Transform2D>(owner, out var transform) || !_ecs.TryGet<RigidBody2D>(owner, out var rigid)) return 0;
		var created = CreateBody(owner, default, transform, rigid, hasCollider: false);
		_bare[owner] = created;
		return created;
	}

	/// <summary>A part's collider with its geometry moved into its body's space (<paramref name="position"/>, <paramref name="rotation"/>).</summary>
	private static Collider2D Placed(in Collider2D collider, Vector2 position, float rotation)
	{
		var placed = collider;
		placed.Offset = position + RotateBy(rotation, collider.Offset);
		placed.Angle = collider.Angle + rotation;
		if (collider.Shape == ColliderShape2D.Capsule)
		{
			// The capsule's ends are offset by Offset when the shape is made.
			placed.PointA = RotateBy(rotation, collider.PointA);
			placed.PointB = RotateBy(rotation, collider.PointB);
		}

		return placed;
	}

	private static Vector2 RotateBy(float angle, Vector2 v)
	{
		var (sin, cos) = MathF.SinCos(angle);
		return new Vector2(cos * v.X - sin * v.Y, sin * v.X + cos * v.Y);
	}

	private void Push(ref BodySlot body, in Collider2D collider, in Transform2D transform, in RigidBody2D rigid, float dt)
	{
		var id = body.Body;
		ref readonly var synced = ref body.Rigid;
		var massChanged = false;

		if (rigid.Type != synced.Type)
		{
			B2.BodySetType(id, ToB2(rigid.Type));
			massChanged = true;
		}

		if (!body.Bare && !Collider2D.SameShape(collider, body.Collider))
		{
			B2.DestroyShape(body.Shape, true);
			body.Shape = CreateShape(id, body.Slot, collider, body.Entity);
			body.Collider = collider;
			massChanged = true;
		}

		if (rigid.LinearDamping != synced.LinearDamping) B2.BodySetLinearDamping(id, rigid.LinearDamping);
		if (rigid.AngularDamping != synced.AngularDamping) B2.BodySetAngularDamping(id, rigid.AngularDamping);
		if (rigid.GravityScale != synced.GravityScale) B2.BodySetGravityScale(id, rigid.GravityScale);
		if (rigid.FixedRotation != synced.FixedRotation) B2.BodySetFixedRotation(id, rigid.FixedRotation);
		if (rigid.IsBullet != synced.IsBullet) B2.BodySetBullet(id, rigid.IsBullet);
		if (rigid.Mass != synced.Mass) massChanged = true;
		if (massChanged) body.MassDirty = true;

		if (rigid.LinearVelocity != synced.LinearVelocity) B2.BodySetLinearVelocity(id, ToB2(rigid.LinearVelocity * _toPhysics));
		if (rigid.AngularVelocity != synced.AngularVelocity) B2.BodySetAngularVelocity(id, rigid.AngularVelocity);

		var moved = transform.Position != body.Position || transform.Rotation != body.Rotation;
		if (rigid.Type == RigidBodyType2D.Kinematic)
		{
			if (moved)
			{
				var target = new B2.Transform { p = ToB2(transform.Position * _toPhysics), q = B2.MakeRot(transform.Rotation) };
				B2.BodySetTargetTransform(id, target, dt);
				body.Driven = true;
				body.Position = transform.Position;
				body.Rotation = transform.Rotation;
			}
			else if (body.Driven)
			{
				// Arrived: a kinematic body keeps its velocity, so stop the one the target gave it.
				B2.BodySetLinearVelocity(id, default);
				B2.BodySetAngularVelocity(id, 0);
				body.Driven = false;
			}
		}
		else if (moved)
		{
			B2.BodySetTransform(id, ToB2(transform.Position * _toPhysics), B2.MakeRot(transform.Rotation));
			B2.BodySetAwake(id, true);
			body.Position = transform.Position;
			body.Rotation = transform.Rotation;
		}

		body.Rigid = rigid;
	}

	private int CreateBody(Entity entity, in Collider2D collider, in Transform2D transform, in RigidBody2D rigid, bool hasCollider)
	{
		if (hasCollider) Validate(entity, collider);
		var slot = AllocateBodySlot();

		var def = B2.DefaultBodyDef();
		def.type = ToB2(rigid.Type);
		def.position = ToB2(transform.Position * _toPhysics);
		def.rotation = B2.MakeRot(transform.Rotation);
		def.linearVelocity = ToB2(rigid.LinearVelocity * _toPhysics);
		def.angularVelocity = rigid.AngularVelocity;
		def.linearDamping = rigid.LinearDamping;
		def.angularDamping = rigid.AngularDamping;
		def.gravityScale = rigid.GravityScale;
		def.fixedRotation = rigid.FixedRotation;
		def.isBullet = rigid.IsBullet;
		def.enableSleep = _config.EnableSleep;
		def.userData = (void*)(nint)slot;
		var id = B2.CreateBody(Id, &def);

		ref var body = ref _bodies[slot];
		body = default;
		body.Alive = true;
		body.Slot = slot;
		body.Entity = entity;
		body.Body = id;
		body.Collider = collider;
		body.Rigid = rigid;
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;
		body.Seen = _stamp;
		body.Bare = !hasCollider;
		if (hasCollider) body.Shape = CreateShape(id, slot, collider, entity);
		body.MassDirty = rigid.Mass > 0;
		_bodyCount++;
		return slot;
	}

	private void ApplyMass(B2.BodyId id, in RigidBody2D rigid)
	{
		B2.BodyApplyMassFromShapes(id);
		if (!(rigid.Mass > 0) || rigid.Type != RigidBodyType2D.Dynamic) return;
		var mass = B2.BodyGetMassData(id);
		var scale = mass.mass > 0 ? rigid.Mass / mass.mass : 1f;
		mass.mass = rigid.Mass;
		mass.rotationalInertia *= scale;
		B2.BodySetMassData(id, mass);
	}

	private B2.ShapeId CreateShape(B2.BodyId body, int slot, in Collider2D collider, Entity entity)
	{
		var def = B2.DefaultShapeDef();
		def.density = collider.Density;
		def.material.friction = collider.Friction;
		def.material.restitution = collider.Restitution;
		def.isSensor = collider.IsSensor;
		def.enableSensorEvents = collider.EnableEvents;
		def.enableContactEvents = collider.EnableEvents && !collider.IsSensor;
		def.filter = new B2.Filter { categoryBits = collider.Layer, maskBits = collider.Mask, groupIndex = 0 };
		def.userData = (void*)(nint)slot;

		var offset = ToB2(collider.Offset * _toPhysics);
		B2.ShapeId shape;
		switch (collider.Shape)
		{
			case ColliderShape2D.Circle:
			{
				var circle = new B2.Circle { center = offset, radius = collider.Radius * _toPhysics };
				shape = B2.CreateCircleShape(body, &def, &circle);
				break;
			}
			case ColliderShape2D.Capsule:
			{
				var capsule = new B2.Capsule
				{
					center1 = ToB2((collider.PointA + collider.Offset) * _toPhysics),
					center2 = ToB2((collider.PointB + collider.Offset) * _toPhysics),
					radius = collider.Radius * _toPhysics,
				};
				shape = B2.CreateCapsuleShape(body, &def, &capsule);
				break;
			}
			case ColliderShape2D.Polygon:
			{
				var vertices = collider.GetVertices();
				var points = stackalloc B2.Vec2[vertices.Length];
				for (var i = 0; i < vertices.Length; i++) points[i] = ToB2(vertices[i] * _toPhysics);
				var hull = B2.ComputeHull(points, vertices.Length);
				if (hull.count == 0) throw new InvalidOperationException($"The polygon collider of {entity} has no valid convex hull (collinear or duplicate points, or points closer than Box2D's linear slop).");
				var polygon = B2.MakeOffsetRoundedPolygon(&hull, offset, B2.MakeRot(collider.Angle), collider.Radius * _toPhysics);
				shape = B2.CreatePolygonShape(body, &def, &polygon);
				break;
			}
			default:
			{
				var radius = collider.Radius * _toPhysics;
				var half = collider.Size * (0.5f * _toPhysics) - new Vector2(radius);
				var box = B2.MakeOffsetRoundedBox(half.X, half.Y, offset, B2.MakeRot(collider.Angle), radius);
				shape = B2.CreatePolygonShape(body, &def, &box);
				break;
			}
		}

		RememberShape(shape, entity);
		return shape;
	}

	private static void Validate(Entity entity, in Collider2D collider)
	{
		// Box2D asserts (aborts the process) on degenerate geometry: fail with a message instead.
		switch (collider.Shape)
		{
			case ColliderShape2D.Box when !(collider.Size.X > 0 && collider.Size.Y > 0) || collider.Radius < 0 || collider.Radius * 2 >= MathF.Min(collider.Size.X, collider.Size.Y):
				throw new InvalidOperationException($"The box collider of {entity} needs a positive size larger than twice its rounding radius (size {collider.Size}, radius {collider.Radius}).");
			case ColliderShape2D.Circle or ColliderShape2D.Capsule when !(collider.Radius > 0):
				throw new InvalidOperationException($"The {collider.Shape.ToString().ToLowerInvariant()} collider of {entity} needs a positive radius (radius {collider.Radius}).");
			case ColliderShape2D.Polygon when collider.VertexCount is < 3 or > Polygon2D.MaxVertices || collider.Radius < 0:
				throw new InvalidOperationException($"The polygon collider of {entity} needs 3 to {Polygon2D.MaxVertices} vertices and a non-negative radius ({collider.VertexCount} vertices, radius {collider.Radius}).");
		}

		if (!(collider.Density >= 0) || !(collider.Friction >= 0) || !(collider.Restitution >= 0))
		{
			throw new InvalidOperationException($"The collider of {entity} needs a non-negative density, friction and restitution.");
		}
	}

	private void RemoveUnseenBodies()
	{
		for (var slot = 1; slot < _bodyHighWater; slot++)
		{
			ref var body = ref _bodies[slot];
			if (body.Alive && body.Seen != _stamp) DestroyBody(slot);
		}
	}

	private void DestroyBody(int slot)
	{
		ref var body = ref _bodies[slot];
		// Box2D destroys the body's shapes and joints with it (and reports the end of its contacts).
		B2.DestroyBody(body.Body);
		if (body.Bare) _bare.Remove(body.Entity);
		body.Alive = false;
		body.Entity = Entity.Null;
		if (_freeBodyCount == _freeBodies.Length) Array.Resize(ref _freeBodies, _freeBodies.Length * 2);
		_freeBodies[_freeBodyCount++] = slot;
		_bodyCount--;
	}

	private int AllocateBodySlot()
	{
		if (_freeBodyCount > 0) return _freeBodies[--_freeBodyCount];
		if (_bodyHighWater == _bodies.Length) Array.Resize(ref _bodies, _bodies.Length * 2);
		return _bodyHighWater++;
	}

	private void RememberShape(B2.ShapeId shape, Entity entity)
	{
		var index = shape.index1;
		if (index >= _shapes.Length) Array.Resize(ref _shapes, Math.Max(index + 1, _shapes.Length * 2));
		_shapes[index] = new ShapeRef(shape.generation, entity);
	}

	private Entity EntityOf(B2.ShapeId shape)
	{
		var index = shape.index1;
		if ((uint)index >= (uint)_shapes.Length) return Entity.Null;
		ref readonly var entry = ref _shapes[index];
		return entry.Generation == shape.generation ? entry.Entity : Entity.Null;
	}

	// ---- Joints ----

	private void SyncJoints()
	{
		foreach (ref var chunk in _ecs.Query(in Joints))
		{
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var joint0 = ref chunk.GetFirst<Joint2D>();
			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var joint = ref Unsafe.Add(ref joint0, i);
				SyncJoint(entity, ref joint);
			}
		}

		for (var slot = 1; slot < _jointHighWater; slot++)
		{
			ref var joint = ref _joints[slot];
			if (joint.Alive && joint.Seen != _stamp) DestroyJoint(slot);
		}
	}

	private void SyncJoint(Entity entity, ref Joint2D joint)
	{
		var slot = joint.JointSlot;
		var owned = slot > 0 && slot < _jointHighWater && _joints[slot].Alive && _joints[slot].Entity == entity;
		var bodyA = BodyOf(joint.BodyA);
		var bodyB = BodyOf(joint.BodyB);

		if (owned)
		{
			ref var existing = ref _joints[slot];
			var valid = bodyA.index1 != 0 && bodyB.index1 != 0 && SameId(bodyA, existing.BodyA) && SameId(bodyB, existing.BodyB) &&
				B2.JointIsValid(existing.Joint) && Joint2D.SameDefinition(joint, existing.Definition);
			if (valid)
			{
				existing.Seen = _stamp;
				return;
			}

			DestroyJoint(slot);
			joint.JointSlot = 0;
		}

		// Both bodies must exist (they are created in the same step, before the joints).
		if (bodyA.index1 == 0 || bodyB.index1 == 0 || SameId(bodyA, bodyB)) return;

		slot = AllocateJointSlot();
		ref var created = ref _joints[slot];
		created = new JointSlot
		{
			Alive = true,
			Entity = entity,
			Definition = joint,
			BodyA = bodyA,
			BodyB = bodyB,
			Seen = _stamp,
			Joint = CreateJoint(joint, bodyA, bodyB, slot),
		};
		joint.JointSlot = slot;
		_jointCount++;
	}

	private B2.JointId CreateJoint(in Joint2D joint, B2.BodyId bodyA, B2.BodyId bodyB, int slot)
	{
		var anchorA = ToB2(joint.LocalAnchorA * _toPhysics);
		var anchorB = ToB2(joint.LocalAnchorB * _toPhysics);
		var referenceAngle = B2.RotGetAngle(B2.BodyGetRotation(bodyB)) - B2.RotGetAngle(B2.BodyGetRotation(bodyA));
		var id = Id;

		switch (joint.Type)
		{
			case JointType2D.Revolute:
			{
				var def = B2.DefaultRevoluteJointDef();
				def.bodyIdA = bodyA;
				def.bodyIdB = bodyB;
				def.localAnchorA = anchorA;
				def.localAnchorB = anchorB;
				def.referenceAngle = referenceAngle;
				def.enableSpring = joint.EnableSpring;
				def.hertz = joint.Hertz;
				def.dampingRatio = joint.DampingRatio;
				def.enableLimit = joint.EnableLimit;
				def.lowerAngle = joint.Lower;
				def.upperAngle = joint.Upper;
				def.enableMotor = joint.EnableMotor;
				def.motorSpeed = joint.MotorSpeed;
				def.maxMotorTorque = joint.MaxMotorForce * _toPhysics * _toPhysics;
				def.collideConnected = joint.CollideConnected;
				def.userData = (void*)(nint)slot;
				return B2.CreateRevoluteJoint(id, &def);
			}
			case JointType2D.Prismatic:
			{
				var def = B2.DefaultPrismaticJointDef();
				def.bodyIdA = bodyA;
				def.bodyIdB = bodyB;
				def.localAnchorA = anchorA;
				def.localAnchorB = anchorB;
				var axis = joint.Axis.LengthSquared() > 0 ? Vector2.Normalize(joint.Axis) : Vector2.UnitX;
				def.localAxisA = ToB2(axis);
				def.referenceAngle = referenceAngle;
				def.enableSpring = joint.EnableSpring;
				def.hertz = joint.Hertz;
				def.dampingRatio = joint.DampingRatio;
				def.enableLimit = joint.EnableLimit;
				def.lowerTranslation = joint.Lower * _toPhysics;
				def.upperTranslation = joint.Upper * _toPhysics;
				def.enableMotor = joint.EnableMotor;
				def.motorSpeed = joint.MotorSpeed * _toPhysics;
				def.maxMotorForce = joint.MaxMotorForce * _toPhysics;
				def.collideConnected = joint.CollideConnected;
				def.userData = (void*)(nint)slot;
				return B2.CreatePrismaticJoint(id, &def);
			}
			case JointType2D.Weld:
			{
				var def = B2.DefaultWeldJointDef();
				def.bodyIdA = bodyA;
				def.bodyIdB = bodyB;
				def.localAnchorA = anchorA;
				def.localAnchorB = anchorB;
				def.referenceAngle = referenceAngle;
				def.linearHertz = joint.EnableSpring ? joint.Hertz : 0;
				def.angularHertz = joint.EnableSpring ? joint.Hertz : 0;
				def.linearDampingRatio = joint.DampingRatio;
				def.angularDampingRatio = joint.DampingRatio;
				def.collideConnected = joint.CollideConnected;
				def.userData = (void*)(nint)slot;
				return B2.CreateWeldJoint(id, &def);
			}
			default:
			{
				var def = B2.DefaultDistanceJointDef();
				def.bodyIdA = bodyA;
				def.bodyIdB = bodyB;
				def.localAnchorA = anchorA;
				def.localAnchorB = anchorB;
				var length = joint.Length * _toPhysics;
				if (!(length > 0))
				{
					var worldA = FromB2(B2.BodyGetWorldPoint(bodyA, anchorA));
					var worldB = FromB2(B2.BodyGetWorldPoint(bodyB, anchorB));
					length = MathF.Max(Vector2.Distance(worldA, worldB), 0.005f);
				}

				def.length = length;
				def.enableSpring = joint.EnableSpring;
				def.hertz = joint.Hertz;
				def.dampingRatio = joint.DampingRatio;
				def.enableLimit = joint.EnableLimit;
				def.minLength = joint.EnableLimit ? joint.Lower * _toPhysics : 0;
				def.maxLength = joint.EnableLimit ? joint.Upper * _toPhysics : 100_000f;
				def.enableMotor = joint.EnableMotor;
				def.motorSpeed = joint.MotorSpeed * _toPhysics;
				def.maxMotorForce = joint.MaxMotorForce * _toPhysics;
				def.collideConnected = joint.CollideConnected;
				def.userData = (void*)(nint)slot;
				return B2.CreateDistanceJoint(id, &def);
			}
		}
	}

	private void DestroyJoint(int slot)
	{
		ref var joint = ref _joints[slot];
		// Destroying a body destroys its joints: only destroy what is still there.
		if (B2.JointIsValid(joint.Joint)) B2.DestroyJoint(joint.Joint);
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

	/// <summary>The Box2D body of an entity: its own, its part's body, or the body of its parts.</summary>
	private B2.BodyId BodyOf(Entity entity)
	{
		if (!_ecs.IsAlive(entity)) return default;
		if (!_ecs.TryGet<Collider2D>(entity, out var collider))
		{
			return _bare.TryGetValue(entity, out var bare) ? _bodies[bare].Body : default;
		}

		var slot = collider.BodySlot;
		if (slot < 0)
		{
			var partSlot = -slot;
			if (partSlot >= _partHighWater || !_parts[partSlot].Alive || _parts[partSlot].Entity != entity) return default;
			ref readonly var part = ref _parts[partSlot];
			ref readonly var owner = ref _bodies[part.Owner];
			return owner.Alive && SameId(owner.Body, part.OwnerBody) ? owner.Body : default;
		}

		if (slot == 0 || slot >= _bodyHighWater) return default;
		ref readonly var body = ref _bodies[slot];
		return body.Alive && body.Entity == entity ? body.Body : default;
	}

	private static bool SameId(B2.BodyId a, B2.BodyId b) => a.index1 == b.index1 && a.world0 == b.world0 && a.generation == b.generation;

	// ---- Pull: Box2D to entities ----

	private void PullBodies()
	{
		var events = B2.WorldGetBodyEvents(_id);
		var anyMoved = false;
		for (var i = 0; i < events.moveCount; i++)
		{
			ref readonly var move = ref events.moveEvents[i];
			var slot = (int)(nint)move.userData;
			if (slot <= 0 || slot >= _bodyHighWater) continue;
			ref var body = ref _bodies[slot];
			if (!body.Alive) continue;
			body.Moved = _stamp;
			body.Pulled = move.transform;
			anyMoved = true;
		}

		if (!anyMoved) return;
		_childBodyCount = 0;

		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var hasRigid = chunk.Has<RigidBody2D>();
			var parented = chunk.Has<Parent>();
			// Parts move with their body; a part that is a body of its own is static.
			if (!hasRigid && parented) continue;
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider2D>();
			ref var transform0 = ref chunk.GetFirst<Transform2D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody2D>() : ref Unsafe.NullRef<RigidBody2D>();

			for (var i = 0; i < count; i++)
			{
				var slot = Unsafe.Add(ref collider0, i).BodySlot;
				if (slot <= 0) continue;
				if (parented)
				{
					DeferChild(Unsafe.Add(ref entity0, i), slot);
					continue;
				}

				Pull(ref _bodies[slot], ref Unsafe.Add(ref transform0, i), ref hasRigid ? ref Unsafe.Add(ref rigid0, i) : ref Unsafe.NullRef<RigidBody2D>());
			}
		}

		if (_bare.Count > 0)
		{
			foreach (ref var chunk in _ecs.Query(in BareBodies))
			{
				var parented = chunk.Has<Parent>();
				var count = chunk.Count;
				ref var entity0 = ref chunk.Entity(0);
				ref var transform0 = ref chunk.GetFirst<Transform2D>();
				ref var rigid0 = ref chunk.GetFirst<RigidBody2D>();
				for (var i = 0; i < count; i++)
				{
					var entity = Unsafe.Add(ref entity0, i);
					if (!_bare.TryGetValue(entity, out var slot)) continue;
					if (parented) DeferChild(entity, slot);
					else Pull(ref _bodies[slot], ref Unsafe.Add(ref transform0, i), ref Unsafe.Add(ref rigid0, i));
				}
			}
		}

		if (_childBodyCount == 0) return;

		// Bodies on child entities, shallowest first, so each is written relative to its parent's new pose.
		var children = _childBodies.AsSpan(0, _childBodyCount);
		children.Sort(ChildOrder);
		foreach (ref readonly var child in children)
		{
			ref var body = ref _bodies[child.Slot];
			ref var transform = ref _ecs.Get<Transform2D>(child.Entity);
			ref var rigid = ref _ecs.TryGetRef<RigidBody2D>(child.Entity, out var hasRigid);
			if (hasRigid)
			{
				rigid.LinearVelocity = FromB2(B2.BodyGetLinearVelocity(body.Body)) * _toWorld;
				rigid.AngularVelocity = B2.BodyGetAngularVelocity(body.Body);
				body.Rigid = rigid;
			}

			// A kinematic child goes where its transform says (relative to its parent, whose new pose it follows next step).
			if (body.Rigid.Type == RigidBodyType2D.Kinematic) continue;

			LocalOf(child.Entity, FromB2(body.Pulled.p) * _toWorld, B2.RotGetAngle(body.Pulled.q), out var position, out var rotation);
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
		if (_bodies[slot].Moved != _stamp) return;
		if (_childBodyCount == _childBodies.Length) Array.Resize(ref _childBodies, _childBodies.Length * 2);
		_childBodies[_childBodyCount++] = new ChildBody(entity, slot, DepthOf(entity));
	}

	private static readonly Comparison<ChildBody> ChildOrder = static (a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Entity.Id.CompareTo(b.Entity.Id);

	private readonly record struct ChildBody(Entity Entity, int Slot, int Depth);

	private void Pull(ref BodySlot body, ref Transform2D transform, ref RigidBody2D rigid)
	{
		if (body.Moved != _stamp) return;
		transform.Position = FromB2(body.Pulled.p) * _toWorld;
		transform.Rotation = B2.RotGetAngle(body.Pulled.q);
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;

		if (!Unsafe.IsNullRef(ref rigid))
		{
			rigid.LinearVelocity = FromB2(B2.BodyGetLinearVelocity(body.Body)) * _toWorld;
			rigid.AngularVelocity = B2.BodyGetAngularVelocity(body.Body);
			body.Rigid = rigid;
		}
	}

	private void EmitEvents()
	{
		if (_events is null) return;

		var contacts = B2.WorldGetContactEvents(_id);
		for (var i = 0; i < contacts.beginCount; i++)
		{
			ref readonly var begin = ref contacts.beginEvents[i];
			var manifold = begin.manifold;
			var point = manifold.pointCount > 0 ? FromB2(manifold.points[0].point) * _toWorld : Vector2.Zero;
			_events.Emit(new Collision2D(EntityOf(begin.shapeIdA), EntityOf(begin.shapeIdB), ContactPhase.Begin, point, FromB2(manifold.normal)));
		}

		for (var i = 0; i < contacts.endCount; i++)
		{
			ref readonly var end = ref contacts.endEvents[i];
			_events.Emit(new Collision2D(EntityOf(end.shapeIdA), EntityOf(end.shapeIdB), ContactPhase.End, default, default));
		}

		var sensors = B2.WorldGetSensorEvents(_id);
		for (var i = 0; i < sensors.beginCount; i++)
		{
			ref readonly var begin = ref sensors.beginEvents[i];
			_events.Emit(new Trigger2D(EntityOf(begin.sensorShapeId), EntityOf(begin.visitorShapeId), ContactPhase.Begin));
		}

		for (var i = 0; i < sensors.endCount; i++)
		{
			ref readonly var end = ref sensors.endEvents[i];
			_events.Emit(new Trigger2D(EntityOf(end.sensorShapeId), EntityOf(end.visitorShapeId), ContactPhase.End));
		}
	}

	// ---- Queries ----

	/// <inheritdoc/>
	public bool RayCast(Vector2 origin, Vector2 translation, out RayHit2D hit, uint mask = uint.MaxValue, bool includeSensors = false)
	{
		// Box2D's closest-hit cast does not skip sensors: filter them in the callback (-1 ignores the shape and keeps the
		// ray's length) so the cast goes on to the closest collider that is not one.
		var context = new RayContext { IncludeSensors = includeSensors };
		B2.WorldCastRay(Id, ToB2(origin * _toPhysics), ToB2(translation * _toPhysics), Filter(mask), &ClosestHit, &context);
		if (!context.Hit)
		{
			hit = default;
			return false;
		}

		hit = new RayHit2D(EntityOf(context.Shape), FromB2(context.Point) * _toWorld, FromB2(context.Normal), context.Fraction);
		return true;
	}

	[UnmanagedCallersOnly]
	private static float ClosestHit(B2.ShapeId shape, B2.Vec2 point, B2.Vec2 normal, float fraction, void* context)
	{
		var ray = (RayContext*)context;
		if (!ray->IncludeSensors && B2.ShapeIsSensor(shape)) return -1;
		ray->Hit = true;
		ray->Shape = shape;
		ray->Point = point;
		ray->Normal = normal;
		ray->Fraction = fraction;
		return fraction;   // clip the ray: only closer shapes are reported next
	}

	private struct RayContext
	{
		public bool IncludeSensors;
		public bool Hit;
		public B2.ShapeId Shape;
		public B2.Vec2 Point;
		public B2.Vec2 Normal;
		public float Fraction;
	}

	/// <inheritdoc/>
	public int OverlapBox(Vector2 min, Vector2 max, Span<Entity> results, uint mask = uint.MaxValue)
	{
		var lower = Vector2.Min(min, max) * _toPhysics;
		var upper = Vector2.Max(min, max) * _toPhysics;
		var box = new B2.AABB { lowerBound = ToB2(lower), upperBound = ToB2(upper) };
		var shapes = stackalloc B2.ShapeId[QueryCapacity];
		{
			var context = new CollectorContext { Shapes = shapes, Capacity = QueryCapacity };
			B2.WorldOverlapAABB(Id, box, Filter(mask), &CollectShape, &context);
			var found = 0;
			for (var i = 0; i < context.Count && found < results.Length; i++)
			{
				// The tree holds enlarged bounds: keep the shapes whose own bounds overlap the box.
				var bounds = B2.ShapeGetAABB(shapes[i]);
				if (bounds.lowerBound.x > upper.X || bounds.lowerBound.y > upper.Y || bounds.upperBound.x < lower.X || bounds.upperBound.y < lower.Y) continue;
				found = AddUnique(results, found, EntityOf(shapes[i]));
			}

			return found;
		}
	}

	/// <inheritdoc/>
	public int OverlapCircle(Vector2 center, float radius, Span<Entity> results, uint mask = uint.MaxValue)
	{
		var point = ToB2(center * _toPhysics);
		var proxy = B2.MakeProxy(&point, 1, MathF.Max(radius, 0) * _toPhysics);
		return Overlap(&proxy, results, mask);
	}

	/// <inheritdoc/>
	public int OverlapPoint(Vector2 point, Span<Entity> results, uint mask = uint.MaxValue)
	{
		var p = ToB2(point * _toPhysics);
		var box = new B2.AABB { lowerBound = p, upperBound = p };
		var shapes = stackalloc B2.ShapeId[QueryCapacity];
		{
			var context = new CollectorContext { Shapes = shapes, Capacity = QueryCapacity };
			B2.WorldOverlapAABB(Id, box, Filter(mask), &CollectShape, &context);
			var found = 0;
			for (var i = 0; i < context.Count && found < results.Length; i++)
			{
				if (B2.ShapeTestPoint(shapes[i], p)) found = AddUnique(results, found, EntityOf(shapes[i]));
			}

			return found;
		}
	}

	private int Overlap(B2.ShapeProxy* proxy, Span<Entity> results, uint mask)
	{
		var shapes = stackalloc B2.ShapeId[QueryCapacity];
		{
			var context = new CollectorContext { Shapes = shapes, Capacity = QueryCapacity };
			B2.WorldOverlapShape(Id, proxy, Filter(mask), &CollectShape, &context);
			var found = 0;
			for (var i = 0; i < context.Count && found < results.Length; i++) found = AddUnique(results, found, EntityOf(shapes[i]));
			return found;
		}
	}

	private const int QueryCapacity = 256;

	private static int AddUnique(Span<Entity> results, int count, Entity entity)
	{
		if (entity == Entity.Null) return count;
		for (var i = 0; i < count; i++)
		{
			if (results[i] == entity) return count;
		}

		results[count] = entity;
		return count + 1;
	}

	private static B2.QueryFilter Filter(uint mask) => new() { categoryBits = ulong.MaxValue, maskBits = mask };

	[UnmanagedCallersOnly]
	private static bool CollectShape(B2.ShapeId shape, void* context)
	{
		var collector = (CollectorContext*)context;
		if (collector->Count >= collector->Capacity) return false;
		collector->Shapes[collector->Count++] = shape;
		return true;
	}

	private struct CollectorContext
	{
		public B2.ShapeId* Shapes;
		public int Capacity;
		public int Count;
	}

	// ---- Forces ----

	/// <inheritdoc/>
	public bool ApplyForce(Entity entity, Vector2 force)
	{
		var body = BodyOf(entity);
		if (body.index1 == 0) return false;
		B2.BodyApplyForceToCenter(body, ToB2(force * _toPhysics), true);
		return true;
	}

	/// <inheritdoc/>
	public bool ApplyLinearImpulse(Entity entity, Vector2 impulse)
	{
		var body = BodyOf(entity);
		if (body.index1 == 0) return false;
		B2.BodyApplyLinearImpulseToCenter(body, ToB2(impulse * _toPhysics), true);
		return true;
	}

	/// <inheritdoc/>
	public bool ApplyTorque(Entity entity, float torque)
	{
		var body = BodyOf(entity);
		if (body.index1 == 0) return false;
		B2.BodyApplyTorque(body, torque * _toPhysics * _toPhysics, true);
		return true;
	}

	/// <inheritdoc/>
	public bool ApplyAngularImpulse(Entity entity, float impulse)
	{
		var body = BodyOf(entity);
		if (body.index1 == 0) return false;
		B2.BodyApplyAngularImpulse(body, impulse * _toPhysics * _toPhysics, true);
		return true;
	}

	/// <inheritdoc/>
	public bool TryGetMass(Entity entity, out float mass, out Vector2 localCenter)
	{
		var body = BodyOf(entity);
		if (body.index1 == 0)
		{
			mass = 0;
			localCenter = default;
			return false;
		}

		var data = B2.BodyGetMassData(body);
		mass = data.mass;
		localCenter = FromB2(data.center) * _toWorld;
		return true;
	}

	/// <inheritdoc/>
	public bool TryGetBodyEntity(Entity entity, out Entity body)
	{
		var id = BodyOf(entity);
		if (id.index1 == 0)
		{
			body = Entity.Null;
			return false;
		}

		var slot = (int)(nint)B2.BodyGetUserData(id);
		body = _bodies[slot].Entity;
		return true;
	}

	/// <inheritdoc/>
	public ulong ComputeStateHash()
	{
		_ = Id;
		var hash = 14695981039346656037UL;
		for (var slot = 1; slot < _bodyHighWater; slot++)
		{
			ref readonly var body = ref _bodies[slot];
			if (!body.Alive) continue;
			var transform = B2.BodyGetTransform(body.Body);
			var velocity = B2.BodyGetLinearVelocity(body.Body);
			hash = Mix(hash, (uint)slot);
			hash = Mix(hash, transform.p.x);
			hash = Mix(hash, transform.p.y);
			hash = Mix(hash, transform.q.c);
			hash = Mix(hash, transform.q.s);
			hash = Mix(hash, velocity.x);
			hash = Mix(hash, velocity.y);
			hash = Mix(hash, B2.BodyGetAngularVelocity(body.Body));
		}

		return hash;
	}

	internal static ulong Mix(ulong hash, float value) => Mix(hash, (uint)BitConverter.SingleToInt32Bits(value));

	internal static ulong Mix(ulong hash, uint value)
	{
		for (var i = 0; i < 4; i++)
		{
			hash ^= (byte)(value >> (8 * i));
			hash *= 1099511628211UL;
		}

		return hash;
	}

	// ---- Debug drawing ----

	/// <summary>Draws every collider (static blue, kinematic green, dynamic red, sleeping gray, sensors yellow) and joint (white) with <paramref name="lines"/>.</summary>
	internal void Draw(ILineSink lines)
	{
		_ = Id;
		Span<Vector2> points = stackalloc Vector2[Polygon2D.MaxVertices];
		for (var slot = 1; slot < _bodyHighWater; slot++)
		{
			ref readonly var body = ref _bodies[slot];
			if (!body.Alive) continue;

			if (!body.Bare) DrawShape(lines, points, body.Body, body.Shape, body.Collider.IsSensor, body.Rigid.Type);
		}

		for (var slot = 1; slot < _partHighWater; slot++)
		{
			ref readonly var part = ref _parts[slot];
			if (!part.Alive || !B2.ShapeIsValid(part.Shape)) continue;
			ref readonly var owner = ref _bodies[part.Owner];
			DrawShape(lines, points, owner.Body, part.Shape, part.Collider.IsSensor, owner.Rigid.Type);
		}

		for (var slot = 1; slot < _jointHighWater; slot++)
		{
			ref readonly var joint = ref _joints[slot];
			if (!joint.Alive || !B2.JointIsValid(joint.Joint)) continue;
			var a = FromB2(B2.BodyGetWorldPoint(joint.BodyA, ToB2(joint.Definition.LocalAnchorA * _toPhysics))) * _toWorld;
			var b = FromB2(B2.BodyGetWorldPoint(joint.BodyB, ToB2(joint.Definition.LocalAnchorB * _toPhysics))) * _toWorld;
			var centerA = FromB2(B2.BodyGetPosition(joint.BodyA)) * _toWorld;
			var centerB = FromB2(B2.BodyGetPosition(joint.BodyB)) * _toWorld;
			lines.Line(centerA, a, DebugColor.Joint);
			lines.Line(a, b, DebugColor.Joint);
			lines.Line(b, centerB, DebugColor.Joint);
		}
	}

	/// <summary>Draws one shape of <paramref name="body"/> from Box2D's geometry (in the body's space).</summary>
	private void DrawShape(ILineSink lines, Span<Vector2> points, B2.BodyId body, B2.ShapeId shape, bool sensor, RigidBodyType2D type)
	{
		var transform = B2.BodyGetTransform(body);
		var position = FromB2(transform.p) * _toWorld;
		var rotation = new Vector2(transform.q.c, transform.q.s);
		var color = sensor ? DebugColor.Sensor
			: type switch
			{
				RigidBodyType2D.Static => DebugColor.Static,
				RigidBodyType2D.Kinematic => DebugColor.Kinematic,
				_ => B2.BodyIsAwake(body) ? DebugColor.Dynamic : DebugColor.Sleeping,
			};

		switch (B2.ShapeGetType(shape))
		{
			case B2.ShapeType.circleShape:
			{
				var circle = B2.ShapeGetCircle(shape);
				var center = position + Rotate(rotation, FromB2(circle.center) * _toWorld);
				var radius = circle.radius * _toWorld;
				DrawCircle(lines, center, radius, color);
				lines.Line(center, center + rotation * radius, color);
				break;
			}
			case B2.ShapeType.capsuleShape:
			{
				var capsule = B2.ShapeGetCapsule(shape);
				var a = position + Rotate(rotation, FromB2(capsule.center1) * _toWorld);
				var b = position + Rotate(rotation, FromB2(capsule.center2) * _toWorld);
				var radius = capsule.radius * _toWorld;
				DrawCircle(lines, a, radius, color);
				DrawCircle(lines, b, radius, color);
				var axis = b - a;
				var normal = axis.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(-axis.Y, axis.X)) * radius : Vector2.Zero;
				lines.Line(a + normal, b + normal, color);
				lines.Line(a - normal, b - normal, color);
				break;
			}
			default:
			{
				var polygon = B2.ShapeGetPolygon(shape);
				var count = Math.Min(polygon.count, Polygon2D.MaxVertices);
				for (var i = 0; i < count; i++) points[i] = position + Rotate(rotation, FromB2(polygon.vertices[i]) * _toWorld);
				for (var i = 0; i < count; i++) lines.Line(points[i], points[(i + 1) % count], color);
				break;
			}
		}
	}

	private static void DrawCircle(ILineSink lines, Vector2 center, float radius, DebugColor color)
	{
		const int Segments = 16;
		var previous = center + new Vector2(radius, 0);
		for (var i = 1; i <= Segments; i++)
		{
			var (sin, cos) = MathF.SinCos(i * (MathF.Tau / Segments));
			var next = center + new Vector2(cos, sin) * radius;
			lines.Line(previous, next, color);
			previous = next;
		}
	}

	private static Vector2 Rotate(Vector2 rotation, Vector2 v) => new(rotation.X * v.X - rotation.Y * v.Y, rotation.Y * v.X + rotation.X * v.Y);

	// ---- Conversions ----

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static B2.Vec2 ToB2(Vector2 v) => new() { x = v.X, y = v.Y };

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector2 FromB2(B2.Vec2 v) => new(v.x, v.y);

	private static B2.BodyType ToB2(RigidBodyType2D type) => type switch
	{
		RigidBodyType2D.Dynamic => B2.BodyType.dynamicBody,
		RigidBodyType2D.Kinematic => B2.BodyType.kinematicBody,
		_ => B2.BodyType.staticBody,
	};

	/// <summary>Destroys the Box2D world (its bodies, shapes and joints).</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		lock (WorldTableGate) B2.DestroyWorld(_id);
	}

	internal bool IsDisposed => _disposed;

	internal World Ecs => _ecs;

	private readonly List<(B2.BodyId Body, B2.Transform Pose)> _rewoundBodies = [];

	/// <summary>
	/// Moves every body whose entity's <see cref="Transform2D"/> is not where the body is (a world rewound by lag
	/// compensation) to that transform, without stepping, so queries see the rewound poses;
	/// <see cref="RestoreRewoundBodies"/> moves them back. Velocities, contacts and sleep are left alone.
	/// </summary>
	public void MatchBodiesToTransforms()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		RestoreRewoundBodies();
		for (var slot = 1; slot < _bodyHighWater; slot++)
		{
			ref var body = ref _bodies[slot];
			if (!body.Alive || !_ecs.IsAlive(body.Entity) || !_ecs.TryGet<Transform2D>(body.Entity, out var local)) continue;
			var transform = _ecs.Has<Parent>(body.Entity) ? WorldOf(body.Entity, local) : local;
			if (transform.Position == body.Position && transform.Rotation == body.Rotation) continue;
			_rewoundBodies.Add((body.Body, B2.BodyGetTransform(body.Body)));
			B2.BodySetTransform(body.Body, ToB2(transform.Position * _toPhysics), B2.MakeRot(transform.Rotation));
		}
	}

	/// <summary>Moves the bodies <see cref="MatchBodiesToTransforms"/> moved back to their poses.</summary>
	public void RestoreRewoundBodies()
	{
		for (var i = _rewoundBodies.Count - 1; i >= 0; i--)
		{
			var (body, pose) = _rewoundBodies[i];
			B2.BodySetTransform(body, pose.p, pose.q);
		}

		_rewoundBodies.Clear();
	}

	private struct BodySlot
	{
		public bool Alive;
		public bool Driven;

		/// <summary>A rigid body without a collider of its own: the body of its parts (no own shape).</summary>
		public bool Bare;

		/// <summary>Its shapes changed this step: the mass is recomputed.</summary>
		public bool MassDirty;
		public int Slot;
		public Entity Entity;
		public B2.BodyId Body;
		public B2.ShapeId Shape;
		public Collider2D Collider;
		public RigidBody2D Rigid;
		public Vector2 Position;
		public float Rotation;
		public uint Seen;
		public uint Moved;
		public B2.Transform Pulled;
	}

	/// <summary>A compound part: a child collider's shape on an ancestor's body.</summary>
	private struct PartSlot
	{
		public bool Alive;
		public Entity Entity;
		public int Owner;
		public B2.BodyId OwnerBody;
		public B2.ShapeId Shape;
		public Collider2D Collider;

		/// <summary>The part's pose in its body's space.</summary>
		public Vector2 Position;
		public float Rotation;
		public uint Seen;
	}

	private struct JointSlot
	{
		public bool Alive;
		public Entity Entity;
		public Joint2D Definition;
		public B2.BodyId BodyA;
		public B2.BodyId BodyB;
		public B2.JointId Joint;
		public uint Seen;
	}

	private readonly record struct ShapeRef(ushort Generation, Entity Entity);
}

/// <summary>The colors of the debug drawing.</summary>
internal enum DebugColor : byte
{
	Static,
	Kinematic,
	Dynamic,
	Sleeping,
	Sensor,
	Joint,
}

/// <summary>Where the debug drawing's lines go (the sprite batch, or a test recorder).</summary>
internal interface ILineSink
{
	void Line(Vector2 a, Vector2 b, DebugColor color);
}
