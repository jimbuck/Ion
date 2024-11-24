using Arch.Core;

using World = Arch.Core.World;
using Vector2 = System.Numerics.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

using Ion.Examples.Breakout.ECS.Common;

namespace Ion.Examples.Breakout.ECS.Physics;

public class PhysicsSystem(World world, PhysicsManager physics)
{
	private readonly QueryDescription _kinematicQuery = new QueryDescription().WithAll<KinematicRigidBody, Transform2D>();
	private readonly QueryDescription _dynamicQuery = new QueryDescription().WithAll<DynamicRigidBody, Transform2D>();

	public bool IsDebugRenderEnabled { get; set; } = true;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		physics.Init();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (IsDebugRenderEnabled) physics.DebugRender(dt, physics.PhysicsScale);
	}

	[FixedUpdate]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		world.Query(in _kinematicQuery, (ref KinematicRigidBody kineticComponent, ref Transform2D transform, ref Sprite sprite) =>
		{
			var physTransform = kineticComponent.Body.GetTransform();

			var targetPos = new AetherVector2(transform.Position.X / physics.PhysicsScale, transform.Position.Y / physics.PhysicsScale);

			kineticComponent.Body.LinearVelocity = (targetPos - physTransform.p) * physics.KineticVelocityFactor;
			if (kineticComponent.Body.LinearVelocity.LengthSquared() >= physics.MaxKineticVelocitySquared)
			{
				var rotation = MathF.Acos(physTransform.q.R);

				kineticComponent.Body.LinearVelocity = AetherVector2.Zero;
				kineticComponent.Body.SetTransform(targetPos, rotation);
			}
		});

		physics.Step(dt);

		world.Query(in _dynamicQuery, (ref DynamicRigidBody rigidBody, ref Transform2D transform, ref Sprite sprite) =>
		{
			var position2d = rigidBody.Body.Position;
			transform.Position = new Vector2(position2d.X * physics.PhysicsScale, position2d.Y * physics.PhysicsScale);
		});

		next(dt);
	}
}
