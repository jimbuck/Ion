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
/// entity with a <see cref="Collider2D"/> and a <c>Transform2D</c> is a body (static unless it has a
/// <see cref="RigidBody2D"/>), every <see cref="Joint2D"/> a joint.
/// </summary>
/// <remarks>
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
	private static readonly QueryDescription Joints = new QueryDescription().WithAll<Joint2D>();

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
		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var count = chunk.Count;
			ref var entity0 = ref chunk.Entity(0);
			ref var collider0 = ref chunk.GetFirst<Collider2D>();
			ref var transform0 = ref chunk.GetFirst<Transform2D>();
			var hasRigid = chunk.Has<RigidBody2D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody2D>() : ref Unsafe.NullRef<RigidBody2D>();

			for (var i = 0; i < count; i++)
			{
				var entity = Unsafe.Add(ref entity0, i);
				ref var collider = ref Unsafe.Add(ref collider0, i);
				ref var transform = ref Unsafe.Add(ref transform0, i);
				var rigid = hasRigid ? Unsafe.Add(ref rigid0, i) : RigidBody2D.Static();

				var slot = collider.BodySlot;
				if (slot <= 0 || slot >= _bodyHighWater || !_bodies[slot].Alive || _bodies[slot].Entity != entity)
				{
					collider.BodySlot = CreateBody(entity, collider, transform, rigid);
					continue;
				}

				ref var body = ref _bodies[slot];
				body.Seen = _stamp;
				Push(ref body, collider, transform, rigid, dt);
			}
		}
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

		if (!Collider2D.SameShape(collider, body.Collider))
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
		if (massChanged) ApplyMass(id, rigid);

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

	private int CreateBody(Entity entity, in Collider2D collider, in Transform2D transform, in RigidBody2D rigid)
	{
		Validate(entity, collider);
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
		body.Shape = CreateShape(id, slot, collider, entity);
		if (rigid.Mass > 0) ApplyMass(id, rigid);
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

	private B2.BodyId BodyOf(Entity entity)
	{
		if (!_ecs.IsAlive(entity) || !_ecs.TryGet<Collider2D>(entity, out var collider)) return default;
		var slot = collider.BodySlot;
		if (slot <= 0 || slot >= _bodyHighWater) return default;
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

		foreach (ref var chunk in _ecs.Query(in Bodies))
		{
			var count = chunk.Count;
			ref var collider0 = ref chunk.GetFirst<Collider2D>();
			ref var transform0 = ref chunk.GetFirst<Transform2D>();
			var hasRigid = chunk.Has<RigidBody2D>();
			ref var rigid0 = ref hasRigid ? ref chunk.GetFirst<RigidBody2D>() : ref Unsafe.NullRef<RigidBody2D>();

			for (var i = 0; i < count; i++)
			{
				var slot = Unsafe.Add(ref collider0, i).BodySlot;
				ref var body = ref _bodies[slot];
				if (body.Moved != _stamp) continue;

				ref var transform = ref Unsafe.Add(ref transform0, i);
				transform.Position = FromB2(body.Pulled.p) * _toWorld;
				transform.Rotation = B2.RotGetAngle(body.Pulled.q);
				body.Position = transform.Position;
				body.Rotation = transform.Rotation;

				if (hasRigid)
				{
					ref var rigid = ref Unsafe.Add(ref rigid0, i);
					rigid.LinearVelocity = FromB2(B2.BodyGetLinearVelocity(body.Body)) * _toWorld;
					rigid.AngularVelocity = B2.BodyGetAngularVelocity(body.Body);
					body.Rigid = rigid;
				}
			}
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
	public bool RayCast(Vector2 origin, Vector2 translation, out RayHit2D hit, uint mask = uint.MaxValue)
	{
		var result = B2.WorldCastRayClosest(Id, ToB2(origin * _toPhysics), ToB2(translation * _toPhysics), Filter(mask));
		if (!result.hit)
		{
			hit = default;
			return false;
		}

		hit = new RayHit2D(EntityOf(result.shapeId), FromB2(result.point) * _toWorld, FromB2(result.normal), result.fraction);
		return true;
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

			var transform = B2.BodyGetTransform(body.Body);
			var position = FromB2(transform.p) * _toWorld;
			var rotation = new Vector2(transform.q.c, transform.q.s);
			var color = body.Collider.IsSensor ? DebugColor.Sensor
				: body.Rigid.Type switch
				{
					RigidBodyType2D.Static => DebugColor.Static,
					RigidBodyType2D.Kinematic => DebugColor.Kinematic,
					_ => B2.BodyIsAwake(body.Body) ? DebugColor.Dynamic : DebugColor.Sleeping,
				};

			ref readonly var collider = ref body.Collider;
			switch (collider.Shape)
			{
				case ColliderShape2D.Circle:
				{
					var center = position + Rotate(rotation, collider.Offset);
					DrawCircle(lines, center, collider.Radius, color);
					lines.Line(center, center + rotation * collider.Radius, color);
					break;
				}
				case ColliderShape2D.Capsule:
				{
					var a = position + Rotate(rotation, collider.PointA + collider.Offset);
					var b = position + Rotate(rotation, collider.PointB + collider.Offset);
					DrawCircle(lines, a, collider.Radius, color);
					DrawCircle(lines, b, collider.Radius, color);
					var axis = b - a;
					var normal = axis.LengthSquared() > 0 ? Vector2.Normalize(new Vector2(-axis.Y, axis.X)) * collider.Radius : Vector2.Zero;
					lines.Line(a + normal, b + normal, color);
					lines.Line(a - normal, b - normal, color);
					break;
				}
				default:
				{
					var polygon = B2.ShapeGetPolygon(body.Shape);
					var count = Math.Min(polygon.count, Polygon2D.MaxVertices);
					for (var i = 0; i < count; i++) points[i] = position + Rotate(rotation, FromB2(polygon.vertices[i]) * _toWorld);
					for (var i = 0; i < count; i++) lines.Line(points[i], points[(i + 1) % count], color);
					break;
				}
			}
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

	private struct BodySlot
	{
		public bool Alive;
		public bool Driven;
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
