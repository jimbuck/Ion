using Ion.Extensions.Ecs;

using Vector2 = System.Numerics.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace Ion.Examples.Breakout.ECS.Physics;

/// <summary>
/// The adapter between Aether and the ECS: in each fixed step, kinematic bodies follow their <see cref="Transform2D"/>, the
/// physics world steps, and dynamic bodies write their position back. All three run before the game's fixed steps
/// (order 0), like the single step they replace. Physics stays a sample adapter until the physics module (Stage 5b).
/// </summary>
public partial class PhysicsSystem(PhysicsManager physics)
{
	public bool IsDebugRenderEnabled { get; set; } = true;

	[Init]
	public void Init(GameTime dt)
	{
		physics.Init();
	}

	// Drawn over the sprites and the score (order 0).
	[Render(Order = 10)]
	public void Render(GameTime dt)
	{
		if (IsDebugRenderEnabled) physics.DebugRender(dt, physics.PhysicsScale);
	}

	/// <summary>Moves kinematic bodies (the paddle) towards their transform.</summary>
	[FixedUpdate(Order = -3), Query]
	private void SyncKinematic(in KinematicRigidBody kinematic, in Transform2D transform)
	{
		var body = kinematic.Body;
		var physTransform = body.GetTransform();

		var targetPos = new AetherVector2(transform.Position.X / physics.PhysicsScale, transform.Position.Y / physics.PhysicsScale);

		body.LinearVelocity = (targetPos - physTransform.p) * physics.KineticVelocityFactor;
		if (body.LinearVelocity.LengthSquared() >= physics.MaxKineticVelocitySquared)
		{
			var rotation = MathF.Acos(physTransform.q.R);

			body.LinearVelocity = AetherVector2.Zero;
			body.SetTransform(targetPos, rotation);
		}
	}

	[FixedUpdate(Order = -2)]
	public void Step(GameTime dt)
	{
		physics.Step(dt);
	}

	/// <summary>Writes dynamic bodies (the balls) back into their transform.</summary>
	[FixedUpdate(Order = -1), Query]
	private void SyncDynamic(in DynamicRigidBody rigidBody, ref Transform2D transform)
	{
		var position2d = rigidBody.Body.Position;
		transform.Position = new Vector2(position2d.X * physics.PhysicsScale, position2d.Y * physics.PhysicsScale);
	}
}
