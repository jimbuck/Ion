using System.Numerics;
using System.Runtime.CompilerServices;

using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;

using BepuUtilities;

namespace Ion.Extensions.Physics3D;

/// <summary>
/// The narrow phase callbacks: collision filtering by layer and mask, the material of a pair (friction, and restitution
/// approximated by the contact spring), sensors (overlap recorded, no constraint) and the touching pairs recorded for the
/// begin and end events. A compound body is filtered by the union of its shapes' layers and masks at the pair level, and
/// by each shape's own in the per-child callbacks, which also record its events and drop its sensors' contacts.
/// </summary>
internal struct NarrowPhaseCallbacks(PhysicsWorld3D world) : INarrowPhaseCallbacks
{
	private Simulation _sim = null!;

	public void Initialize(Simulation simulation) => _sim = simulation;

	public readonly bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
	{
		// At least one side must move freely; kinematic and static bodies do not collide with each other.
		if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic)
		{
			// Sensors on kinematic bodies still see other kinematic bodies.
			var slotA0 = world.SlotOf(a);
			var slotB0 = world.SlotOf(b);
			if (slotA0 == 0 || slotB0 == 0) return false;
			if (!(world.MaySense(slotA0) || world.MaySense(slotB0)) || a.Mobility == CollidableMobility.Static && b.Mobility == CollidableMobility.Static) return false;
		}

		var slotA = world.SlotOf(a);
		var slotB = world.SlotOf(b);
		if (slotA == 0 || slotB == 0) return false;
		var (layerA, maskA) = world.FilterOf(slotA);
		var (layerB, maskB) = world.FilterOf(slotB);
		return (layerA & maskB) != 0 && (layerB & maskA) != 0;
	}

	public readonly bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
	{
		var slotA = world.SlotOf(pair.A);
		var slotB = world.SlotOf(pair.B);
		if (slotA == 0 || slotB == 0) return false;
		ref readonly var colliderA = ref world.ColliderOf(slotA, childIndexA);
		ref readonly var colliderB = ref world.ColliderOf(slotB, childIndexB);
		if ((colliderA.Layer & colliderB.Mask) == 0 || (colliderB.Layer & colliderA.Mask) == 0) return false;

		// Two non-dynamic bodies only meet through a sensor.
		if (pair.A.Mobility != CollidableMobility.Dynamic && pair.B.Mobility != CollidableMobility.Dynamic) return colliderA.IsSensor || colliderB.IsSensor;
		return true;
	}

	public readonly bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
		where TManifold : unmanaged, IContactManifold<TManifold>
	{
		var slotA = world.SlotOf(pair.A);
		var slotB = world.SlotOf(pair.B);
		ref readonly var colliderA = ref world.Slots[slotA].Collider;
		ref readonly var colliderB = ref world.Slots[slotB].Collider;
		var sensor = colliderA.IsSensor || colliderB.IsSensor;

		var restitution = MathF.Max(colliderA.Restitution, colliderB.Restitution);
		pairMaterial.FrictionCoefficient = MathF.Sqrt(colliderA.Friction * colliderB.Friction);
		if (restitution > 0)
		{
			// Bepu has no restitution: a less damped contact spring that may push the bodies apart fast bounces.
			pairMaterial.MaximumRecoveryVelocity = float.MaxValue;
			pairMaterial.SpringSettings = new SpringSettings(30, MathF.Max(0, 1 - restitution));
		}
		else
		{
			pairMaterial.MaximumRecoveryVelocity = 2f;
			pairMaterial.SpringSettings = new SpringSettings(30, 1);
		}

		// A compound's events and sensors are handled per shape (below).
		if (world.IsCompound(slotA) || world.IsCompound(slotB)) return !sensor;

		var wantsEvents = sensor ? colliderA.EnableEvents && colliderB.EnableEvents : colliderA.EnableEvents || colliderB.EnableEvents;
		if (wantsEvents)
		{
			// Touching: any contact at or inside the surface (speculative contacts have a negative depth).
			for (var i = 0; i < manifold.Count; i++)
			{
				manifold.GetContact(i, out var offset, out var normal, out var depth, out _);
				if (depth < 0) continue;
				var positionA = PositionOf(pair.A);
				// Bepu's manifold normal points from B to A.
				world.RecordPair(workerIndex, slotA, 0, slotB, 0, positionA + offset, -normal, sensor, colliderA.IsSensor);
				break;
			}
		}

		return !sensor;
	}

	public readonly bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
	{
		// Called for the shapes of compound pairs: events per shape, and a sensor shape's contacts are not solved.
		var slotA = world.SlotOf(pair.A);
		var slotB = world.SlotOf(pair.B);
		ref readonly var colliderA = ref world.ColliderOf(slotA, childIndexA);
		ref readonly var colliderB = ref world.ColliderOf(slotB, childIndexB);
		var sensor = colliderA.IsSensor || colliderB.IsSensor;
		var wantsEvents = sensor ? colliderA.EnableEvents && colliderB.EnableEvents : colliderA.EnableEvents || colliderB.EnableEvents;
		if (wantsEvents)
		{
			for (var i = 0; i < manifold.Count; i++)
			{
				manifold.GetContact(i, out var offset, out var normal, out var depth, out _);
				if (depth < 0) continue;
				// The child manifold's offsets are from child A's center.
				var positionA = PositionOf(pair.A) + Vector3.Transform(world.ChildOffset(slotA, childIndexA), OrientationOf(pair.A));
				world.RecordPair(workerIndex, slotA, childIndexA, slotB, childIndexB, positionA + offset, -normal, sensor, colliderA.IsSensor);
				break;
			}
		}

		return !sensor;
	}

	private readonly Quaternion OrientationOf(CollidableReference collidable) =>
		collidable.Mobility == CollidableMobility.Static ? _sim.Statics[collidable.StaticHandle].Pose.Orientation : _sim.Bodies[collidable.BodyHandle].Pose.Orientation;

	private readonly Vector3 PositionOf(CollidableReference collidable) =>
		collidable.Mobility == CollidableMobility.Static ? _sim.Statics[collidable.StaticHandle].Pose.Position : _sim.Bodies[collidable.BodyHandle].Pose.Position;

	public readonly void Dispose()
	{
	}
}

/// <summary>
/// The pose integrator callbacks: gravity scaled per body, and linear and angular damping per body
/// (<c>v *= 1 / (1 + dt * damping)</c>, Box2D's form).
/// </summary>
internal struct PoseIntegratorCallbacks(PhysicsWorld3D world) : IPoseIntegratorCallbacks
{
	private Simulation _sim = null!;
	private Vector3Wide _gravity;

	public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;

	public readonly bool AllowSubstepsForUnconstrainedBodies => false;

	public readonly bool IntegrateVelocityForKinematics => false;

	public void Initialize(Simulation simulation) => _sim = simulation;

	public void PrepareForIntegration(float dt) => _gravity = Vector3Wide.Broadcast(world.Gravity);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
	{
		Span<float> gravityScale = stackalloc float[Vector<float>.Count];
		Span<float> linearDamping = stackalloc float[Vector<float>.Count];
		Span<float> angularDamping = stackalloc float[Vector<float>.Count];
		var handles = _sim.Bodies.ActiveSet.IndexToHandle;
		for (var lane = 0; lane < Vector<int>.Count; lane++)
		{
			if (integrationMask[lane] == 0) continue;
			var slot = world.BodyHandleToSlot[handles[bodyIndices[lane]].Value];
			ref readonly var rigid = ref world.Slots[slot].Rigid;
			gravityScale[lane] = rigid.GravityScale;
			linearDamping[lane] = rigid.LinearDamping;
			angularDamping[lane] = rigid.AngularDamping;
		}

		var scaledDt = dt * new Vector<float>(gravityScale);
		velocity.Linear.X += _gravity.X * scaledDt;
		velocity.Linear.Y += _gravity.Y * scaledDt;
		velocity.Linear.Z += _gravity.Z * scaledDt;

		var linear = Vector<float>.One / (Vector<float>.One + dt * new Vector<float>(linearDamping));
		var angular = Vector<float>.One / (Vector<float>.One + dt * new Vector<float>(angularDamping));
		velocity.Linear *= linear;
		velocity.Angular *= angular;
	}
}
