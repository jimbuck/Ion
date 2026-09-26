namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Options of the 3D renderer, bound from <c>Ion:Rendering3D</c>.
/// </summary>
public sealed class Rendering3DOptions
{
	/// <summary>Whether the first shadow-casting directional light renders a shadow map.</summary>
	public bool Shadows { get; set; } = true;

	/// <summary>The width and height of the shadow map in texels.</summary>
	public int ShadowMapSize { get; set; } = 2048;

	/// <summary>How far from the camera shadows reach, in world units (the shadow map covers the view frustum up to this distance).</summary>
	public float ShadowDistance { get; set; } = 40f;

	/// <summary>
	/// Whether opaque objects are drawn into the depth buffer first (a depth-only pass), so the opaque pass shades each
	/// pixel once. Pays off with expensive materials and heavy overdraw; costs a second vertex pass.
	/// </summary>
	public bool DepthPrepass { get; set; }

	/// <summary>The most cameras rendered in one frame (the uniform ring is sized for this many).</summary>
	public int MaxCameras { get; set; } = 8;
}
