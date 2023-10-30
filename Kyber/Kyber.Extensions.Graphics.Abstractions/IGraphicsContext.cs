using System.Numerics;

namespace Kyber.Extensions.Graphics;

public interface IGraphicsContext
{
	Matrix4x4 ProjectionMatrix { get; }
	bool NoRender { get; }

	Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far);
	Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far);
}