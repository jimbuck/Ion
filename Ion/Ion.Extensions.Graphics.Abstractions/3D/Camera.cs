using System.Numerics;
using System.Runtime.InteropServices;

namespace Ion.Extensions.Graphics;

/// <summary>How a <see cref="Camera"/> projects.</summary>
public enum ProjectionKind
{
	/// <summary>A perspective projection with a vertical <see cref="Camera.FieldOfView"/>.</summary>
	Perspective,
	/// <summary>An orthographic projection <see cref="Camera.OrthographicSize"/> world units high (half height).</summary>
	Orthographic,
}

/// <summary>What a <see cref="Camera"/> draws behind its scene.</summary>
public enum CameraClear
{
	/// <summary>The <see cref="Camera.ClearColor"/>.</summary>
	Color,
	/// <summary>The skybox of the frame's <see cref="SceneEnvironment"/> (the clear color when it has none).</summary>
	Skybox,
	/// <summary>Nothing: the target keeps what earlier cameras (or passes) drew; the depth is still cleared.</summary>
	None,
}

/// <summary>
/// A camera: projection, near and far planes, the viewport on its target, what it clears to, its priority and its target.
/// Its position and orientation come from a <see cref="Transform"/> (or world matrix): it looks down its local -Z axis
/// with y up. Unmanaged; an ECS component as is.
/// </summary>
/// <remarks>
/// <para>
/// Depth: standard depth (near maps to 0, far to 1, the depth test is <c>Less</c>, the buffer is cleared to 1) in the
/// RHI's WebGPU clip space. Reverse-Z was not picked because the OpenGL ES backend remaps depth from [0, 1] to [-1, 1]
/// in clip space (ES 3.x has no <c>glClipControl</c>), which cancels its precision benefit; <c>Depth32Float</c>
/// buffers and a sensible <see cref="Near"/> keep z-fighting away at game scales.
/// </para>
/// <para>
/// Zero values mean defaults, so <c>default(Camera)</c> still renders: a zero <see cref="FieldOfView"/> is 60 degrees, a
/// zero <see cref="OrthographicSize"/> is 5, a zero <see cref="Near"/> is 0.1, a <see cref="Far"/> not beyond
/// <see cref="Near"/> is 1000, an empty <see cref="Viewport"/> is the whole target and a zero <see cref="CullingMask"/>
/// is every layer.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Camera
{
	/// <summary>The default vertical field of view: 60 degrees.</summary>
	public const float DefaultFieldOfView = MathF.PI / 3f;

	/// <summary>Perspective or orthographic.</summary>
	public ProjectionKind Projection;

	/// <summary>The vertical field of view of a perspective camera, in radians.</summary>
	public float FieldOfView;

	/// <summary>Half the height of an orthographic camera's view, in world units.</summary>
	public float OrthographicSize;

	/// <summary>The distance to the near plane.</summary>
	public float Near;

	/// <summary>The distance to the far plane.</summary>
	public float Far;

	/// <summary>
	/// The part of the target rendered into, normalized: (0, 0) is the top-left corner and (1, 1) the bottom-right. An
	/// empty rectangle is the whole target.
	/// </summary>
	public RectangleF Viewport;

	/// <summary>The color behind the scene with <see cref="CameraClear.Color"/> (and <see cref="CameraClear.Skybox"/> without a skybox).</summary>
	public Color ClearColor;

	/// <summary>What is drawn behind the scene.</summary>
	public CameraClear Clear;

	/// <summary>The order among the frame's cameras: lower priorities render first, so higher ones draw on top.</summary>
	public int Priority;

	/// <summary>The render target, or <see cref="RenderTargetHandle.None"/> (the default) for the frame (the window or offscreen target).</summary>
	public RenderTargetHandle Target;

	/// <summary>The layers this camera renders: a mesh renderer is drawn when its <see cref="MeshRenderer.LayerMask"/> shares a bit with it.</summary>
	public uint CullingMask;

	/// <summary>A perspective camera: 60 degrees, near 0.1, far 1000, the whole target, cleared to black, every layer.</summary>
	public Camera()
	{
		Projection = ProjectionKind.Perspective;
		FieldOfView = DefaultFieldOfView;
		OrthographicSize = 5f;
		Near = 0.1f;
		Far = 1000f;
		Viewport = new RectangleF(0, 0, 1, 1);
		ClearColor = Color.Black;
		Clear = CameraClear.Color;
		CullingMask = uint.MaxValue;
	}

	/// <summary>A perspective camera with a vertical field of view in radians.</summary>
	public static Camera CreatePerspective(float fieldOfView, float near = 0.1f, float far = 1000f) => new() { FieldOfView = fieldOfView, Near = near, Far = far };

	/// <summary>An orthographic camera <paramref name="halfHeight"/> world units high above and below its axis.</summary>
	public static Camera CreateOrthographic(float halfHeight, float near = 0.1f, float far = 1000f) =>
		new() { Projection = ProjectionKind.Orthographic, OrthographicSize = halfHeight, Near = near, Far = far };

	/// <summary>The near plane distance with the default applied.</summary>
	public readonly float EffectiveNear => Near > 0 ? Near : 0.1f;

	/// <summary>The far plane distance with the default applied.</summary>
	public readonly float EffectiveFar => Far > EffectiveNear ? Far : 1000f;

	/// <summary>The viewport with the default applied (an empty rectangle is the whole target).</summary>
	public readonly RectangleF EffectiveViewport => Viewport.Width > 0 && Viewport.Height > 0 ? Viewport : new RectangleF(0, 0, 1, 1);

	/// <summary>
	/// The projection matrix for a viewport of <paramref name="aspectRatio"/> (width over height), right-handed, into the
	/// RHI's clip space (depth 0 at the near plane, 1 at the far plane).
	/// </summary>
	public readonly Matrix4x4 GetProjectionMatrix(float aspectRatio)
	{
		var aspect = aspectRatio > 0 && float.IsFinite(aspectRatio) ? aspectRatio : 1f;
		var near = EffectiveNear;
		var far = EffectiveFar;
		if (Projection == ProjectionKind.Orthographic)
		{
			var halfHeight = OrthographicSize > 0 ? OrthographicSize : 5f;
			return Matrix4x4.CreateOrthographic(2 * halfHeight * aspect, 2 * halfHeight, near, far);
		}

		var fov = FieldOfView > 0 && FieldOfView < MathF.PI ? FieldOfView : DefaultFieldOfView;
		return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, near, far);
	}

	/// <summary>The view matrix of a camera placed by <paramref name="transform"/> (its scale is ignored).</summary>
	public static Matrix4x4 GetViewMatrix(in Transform transform) =>
		Matrix4x4.CreateTranslation(-transform.Position) * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(Quaternion.Normalize(transform.Rotation)));

	/// <summary>The view matrix of a camera placed by the world matrix <paramref name="world"/> (scale removed).</summary>
	public static Matrix4x4 GetViewMatrix(in Matrix4x4 world)
	{
		// Orthonormalize the rotation part so a scaled parent does not scale the view.
		var x = Vector3.Normalize(new Vector3(world.M11, world.M12, world.M13));
		var y = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
		var z = Vector3.Normalize(Vector3.Cross(x, y));
		y = Vector3.Cross(z, x);
		var p = world.Translation;
		// The inverse of a rotation (rows x, y, z) and a translation: the transposed rotation after -p.
		return new Matrix4x4(
			x.X, y.X, z.X, 0,
			x.Y, y.Y, z.Y, 0,
			x.Z, y.Z, z.Z, 0,
			-Vector3.Dot(p, x), -Vector3.Dot(p, y), -Vector3.Dot(p, z), 1);
	}
}
