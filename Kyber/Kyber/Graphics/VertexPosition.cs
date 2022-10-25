namespace Kyber.Graphics;

public record struct VertexPosition(Vector3 Position)
{
	public const uint SizeInBytes = 12;
}
public record struct VertexPositionNormal(Vector3 Position, Vector3 Normal)
{
	public const uint SizeInBytes = 24;
}

public record struct VertexPositionColor(Vector3 Position, Color Color)
{
	public const uint SizeInBytes = 28;
}
public record struct VertexPositionNormalColor(Vector3 Position, Vector3 Normal, Color Color)
{
	public const uint SizeInBytes = 40;
}

public record struct VertexPositionTexture(Vector3 Position, Vector2 TextureCoordinate)
{
	public const uint SizeInBytes = 20;
}
public record struct VertexPositionNormalTexture(Vector3 Position, Vector3 Normal, Vector2 TextureCoordinate)
{
	public const uint SizeInBytes = 32;
}

public record struct Vertex(Vector3 Position, Color Color, Vector2 TextureCoordinate)
{
	public const uint SizeInBytes = 36;
}
public record struct VertexPositionNormalColorTexture(Vector3 Position, Vector3 Normal, Color Color, Vector2 TextureCoordinate)
{
	public const uint SizeInBytes = 48;
}
