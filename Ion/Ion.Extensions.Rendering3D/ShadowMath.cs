using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Rendering3D;

/// <summary>The directional shadow of a frame: the light's view-projection and the matrix shaders sample the shadow map with.</summary>
/// <param name="ViewProjection">World to the shadow map's clip space (an orthographic projection along the light).</param>
/// <param name="ShadowMatrix">World to shadow map texture coordinates (xy, row 0 at the top) and depth (z).</param>
/// <param name="Center">The center of the covered sphere (the camera's frustum slice).</param>
/// <param name="Radius">The radius of the covered sphere.</param>
/// <param name="TexelSize">The size of one shadow map texel in world units.</param>
/// <param name="DepthRange">The depth covered along the light direction, in world units.</param>
public readonly record struct DirectionalShadow(Matrix4x4 ViewProjection, Matrix4x4 ShadowMatrix, Vector3 Center, float Radius, float TexelSize, float DepthRange);

/// <summary>Shadow map fitting for directional lights.</summary>
public static class ShadowMath
{
	/// <summary>
	/// Maps clip space to shadow map coordinates: x and y from [-1, 1] to [0, 1] with y flipped (row 0 of a texture is
	/// the top, clip y points up), depth unchanged ([0, 1] in the RHI's clip space).
	/// </summary>
	public static readonly Matrix4x4 ClipToTexture = new(
		0.5f, 0, 0, 0,
		0, -0.5f, 0, 0,
		0, 0, 1, 0,
		0.5f, 0.5f, 0, 1);

	/// <summary>
	/// Fits an orthographic shadow projection along <paramref name="lightDirection"/> (the direction the light travels)
	/// around the part of the camera's view frustum up to <paramref name="shadowDistance"/>: the slice's bounding sphere,
	/// so the projection does not change size as the camera turns, with its center snapped to whole shadow map texels, so
	/// shadow edges do not shimmer as the camera moves. The depth range reaches back to every caster in
	/// <paramref name="casterBounds"/> (world space) so objects outside the view still cast into it.
	/// </summary>
	public static DirectionalShadow Fit(in Camera camera, in Matrix4x4 cameraView, float aspectRatio, Vector3 lightDirection, float shadowDistance, int shadowMapSize, ReadOnlySpan<Aabb> casterBounds)
	{
		var direction = lightDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(lightDirection) : -Vector3.UnitY;

		// The camera frustum slice [near, min(far, shadow distance)] and its bounding sphere.
		var slice = camera;
		slice.Far = MathF.Min(camera.EffectiveFar, MathF.Max(camera.EffectiveNear * 2, shadowDistance));
		var sliceViewProjection = cameraView * slice.GetProjectionMatrix(aspectRatio);
		Span<Vector3> corners = stackalloc Vector3[8];
		Frustum.GetCorners(sliceViewProjection, corners);
		var center = Vector3.Zero;
		foreach (var corner in corners) center += corner;
		center /= 8f;
		var radius = 0f;
		foreach (var corner in corners) radius = MathF.Max(radius, Vector3.Distance(center, corner));
		// Round the radius up so small changes (floating point noise) do not change the texel size.
		radius = MathF.Ceiling(radius * 16f) / 16f;
		radius = MathF.Max(radius, 0.01f);

		// Light space: looking along the light, with a stable up vector.
		var up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
		var lightView = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);

		// Snap the center to texel increments in light space.
		var texelSize = 2f * radius / Math.Max(1, shadowMapSize);
		var lightCenter = Vector3.Transform(center, lightView);
		lightCenter.X = MathF.Floor(lightCenter.X / texelSize) * texelSize;
		lightCenter.Y = MathF.Floor(lightCenter.Y / texelSize) * texelSize;

		// Depth: light view space looks down -Z, so the distance along the light is -z. Cover the sphere and every caster.
		var nearDistance = -lightCenter.Z - radius;
		var farDistance = -lightCenter.Z + radius;
		var absDirection = Vector3.Abs(direction);
		foreach (ref readonly var box in casterBounds)
		{
			if (!box.IsValid) continue;
			var d = Vector3.Dot(box.Center, direction);
			var extent = Vector3.Dot(box.Extents, absDirection);
			nearDistance = MathF.Min(nearDistance, d - extent);
		}

		// A small margin so casters exactly on the near plane are not clipped.
		nearDistance -= texelSize * 4;
		var projection = Matrix4x4.CreateOrthographicOffCenter(
			lightCenter.X - radius, lightCenter.X + radius,
			lightCenter.Y - radius, lightCenter.Y + radius,
			nearDistance, farDistance);
		var viewProjection = lightView * projection;
		return new DirectionalShadow(viewProjection, viewProjection * ClipToTexture, center, radius, texelSize, farDistance - nearDistance);
	}
}
