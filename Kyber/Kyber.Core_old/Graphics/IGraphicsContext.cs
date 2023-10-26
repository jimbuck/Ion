using System.Numerics;

using Veldrid;

namespace Kyber.Graphics;

public interface IGraphicsContext
{
	public GraphicsDevice? GraphicsDevice { get; }
	public ResourceFactory Factory { get; }
	Matrix4x4 ProjectionMatrix { get; }
	bool NoRender { get; }

	void SubmitCommands(CommandList cl);
	Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far);
	Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far);
}
