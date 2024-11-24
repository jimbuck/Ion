using Vector2 = System.Numerics.Vector2;

namespace Ion.Examples.Breakout.ECS.Common;

public record struct Transform2D(Vector2 Position, float Rotation = 0);
