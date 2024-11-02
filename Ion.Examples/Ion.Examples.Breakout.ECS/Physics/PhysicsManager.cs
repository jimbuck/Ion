using World = Arch.Core.World;
using Vector2 = System.Numerics.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Collision.Shapes;

using Ion.Extensions.Graphics;

namespace Ion.Examples.Breakout.ECS.Physics;

public record struct DynamicRigidBody(Body Body);
public record struct KinematicRigidBody(Body Body);
public record struct StaticBody(Body Body);

public class PhysicsManager(ISpriteBatch spriteBatch) : IDisposable
{
	public readonly AetherWorld World = new(new AetherVector2(0));
	public float PhysicsScale { get; set; } = 10f;

	public float KineticVelocityFactor { get; set; } = 100f;

	public float MaxKineticVelocitySquared { get; set; } = 57600;
	public float MaxKineticVelocity
	{
		get => MathF.Sqrt(MaxKineticVelocitySquared);
		set => MaxKineticVelocitySquared = value * value;
	}

	public void Init()
	{
		World.ContactManager.VelocityConstraintsMultithreadThreshold = 256;
		World.ContactManager.PositionConstraintsMultithreadThreshold = 256;
		World.ContactManager.CollideMultithreadThreshold = 256;
	}

	public void Step(GameTime dt)
	{
		World.Step(dt.Delta);
	}

	public void DebugRender(GameTime dt, float scale)
	{
		foreach (var body in World.BodyList)
		{
			var position = new Vector2(body.Position.X, body.Position.Y);
			var rotation = body.Rotation;

			foreach( var fixture in body.FixtureList)
			{
				var shape = fixture.Shape;
				var color = fixture.Body.BodyType switch
				{
					BodyType.Static => Color.Blue,
					BodyType.Dynamic => Color.Red,
					BodyType.Kinematic => Color.Green,
					_ => Color.White
				};

				if (shape is PolygonShape polygon)
				{
					var vertices = polygon.Vertices;
					var count = polygon.Vertices.Count;

					for (var i = 0; i < count; i++)
					{
						var localStart = new Vector2(vertices[i].X, vertices[i].Y);
						var localEnd = new Vector2(vertices[(i + 1) % count].X, vertices[(i + 1) % count].Y);

						// Apply rotation to the vertices
						var rotatedStart = new Vector2(
							localStart.X * MathF.Cos(rotation) - localStart.Y * MathF.Sin(rotation),
							localStart.X * MathF.Sin(rotation) + localStart.Y * MathF.Cos(rotation)
						);

						var rotatedEnd = new Vector2(
							localEnd.X * MathF.Cos(rotation) - localEnd.Y * MathF.Sin(rotation),
							localEnd.X * MathF.Sin(rotation) + localEnd.Y * MathF.Cos(rotation)
						);

						var start = new Vector2(rotatedStart.X + position.X, rotatedStart.Y + position.Y);
						var end = new Vector2(rotatedEnd.X + position.X, rotatedEnd.Y + position.Y);

						spriteBatch.DrawLine(color, start * scale, end * scale);
					}
				}
				else if (shape is CircleShape circle)
				{
					var center = new Vector2(circle.Position.X, circle.Position.Y) + position;
					var radius = circle.Radius;
					var segments = 10f;
					var increment = MathHelper.TwoPi / segments;

					for(var line = 0; line < segments; line++)
					{
						var angle = line * increment;
						var nextAngle = (line + 1) * increment;

						var localStart = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius) + center;
						var localEnd = new Vector2(MathF.Cos(nextAngle) * radius, MathF.Sin(nextAngle) * radius) + center;

						var start = new Vector2(localStart.X, localStart.Y);
						var end = new Vector2(localEnd.X, localEnd.Y);

						spriteBatch.DrawLine(color, start * scale, end * scale);
					}

					// Draw radius line based on rotation:
					var radiusEnd = new Vector2(MathF.Cos(rotation) * radius, MathF.Sin(rotation) * radius) + center;
					spriteBatch.DrawLine(color, center * scale, radiusEnd * scale);
				}
			}

			var jointEdge = body.JointList;
			while (jointEdge is not null)
			{
				// Draw boxes for joints
				var anchorA = jointEdge.Joint.WorldAnchorA * scale;
				var anchorB = jointEdge.Joint.WorldAnchorB * scale;

				// Draw rotated rectangle from anchorA to anchorB
				spriteBatch.DrawLine(Color.Yellow, new Vector2(anchorA.X, anchorA.Y), new Vector2(anchorB.X, anchorB.Y));
				jointEdge = jointEdge.Next;
			}
		}
	}

	public Body CreateBody(Vector2 position, float rotation = 0, BodyType bodyType = BodyType.Static)
	{
		return World.CreateBody(new AetherVector2(position.X, position.Y), rotation, bodyType);
	}

	public void Remove(Body body)
	{
		World.Remove(body);
	}

	public void Dispose()
	{
		World.Clear();
	}
}
