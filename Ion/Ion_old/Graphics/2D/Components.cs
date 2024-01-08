namespace Ion.Graphics;

public struct Position2dComponent
{
	public RectangleF Position;
	public float Depth;

	public Position2dComponent(RectangleF position, float depth)
	{
		Position = position;
		Depth = depth;
	}
}

public struct Rotation2dComponent
{
	public float Angle;
	public Rotation2dComponent(float angle)
	{
		Angle = angle;
	}
}

public struct Scale2dComponent
{
	public Vector2 Scale;

	public Scale2dComponent(Vector2 scale)
	{
		Scale = scale;
	}
}

public struct SpriteOptionsComponent
{
	public SpriteEffect Options;
	public RectangleF? Scissor;

	public SpriteOptionsComponent(SpriteEffect options = SpriteEffect.None, RectangleF? scissor = default) 
	{
		Options = options;
		Scissor = scissor;
	}
}

public struct Velocity2dComponent
{
	public Vector2 Velocity;

	public Velocity2dComponent(Vector2 velocity)
	{
		Velocity = velocity;
	}
}