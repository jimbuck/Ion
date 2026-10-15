namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Options of the 3D renderer, bound from <c>Ion:Rendering3D</c>.
/// </summary>
public sealed class Rendering3DOptions
{
	/// <summary>Whether the first shadow-casting directional light renders a shadow map.</summary>
	public bool Shadows { get; set; } = true;

	/// <summary>
	/// The width and height of the shadow map in texels. With more than one cascade the map is split into a 2x2 atlas, so
	/// each cascade gets a tile of half this size.
	/// </summary>
	public int ShadowMapSize { get; set; } = 2048;

	/// <summary>How far from the camera shadows reach, in world units (the cascades cover the view frustum up to this distance).</summary>
	public float ShadowDistance { get; set; } = 40f;

	/// <summary>
	/// The number of shadow cascades, 1 to 4: slices of the view frustum from near to <see cref="ShadowDistance"/>, each
	/// with its own part of the shadow map, so near shadows are sharp and far ones still exist. Every cascade draws the
	/// casters it covers, so each one costs shadow draw calls.
	/// </summary>
	public int ShadowCascades { get; set; } = 4;

	/// <summary>
	/// Where the cascades end, as fractions of <see cref="ShadowDistance"/> (ascending, one per cascade; the last is
	/// treated as 1). Null (the default) places them with <see cref="ShadowSplitLambda"/>.
	/// </summary>
	public float[]? ShadowCascadeSplits { get; set; }

	/// <summary>
	/// How the automatic splits are spread: 0 evenly, 1 logarithmically (each cascade a fixed multiple of the last), in
	/// between a mix. Higher values give the near cascades more detail.
	/// </summary>
	public float ShadowSplitLambda { get; set; } = 0.75f;

	/// <summary>
	/// The fraction of each cascade's depth over which it blends into the next one (the last fades out to no shadow), so
	/// the change of resolution does not show as a line. 0 switches sharply.
	/// </summary>
	public float ShadowCascadeBlend { get; set; } = 0.1f;

	/// <summary>Tints what each cascade covers (red, green, blue, yellow from near to far) to tune the splits.</summary>
	public bool ShowShadowCascades { get; set; }

	/// <summary>
	/// Whether opaque objects are drawn into the depth buffer first (a depth-only pass), so the opaque pass shades each
	/// pixel once. Pays off with expensive materials and heavy overdraw; costs a second vertex pass.
	/// </summary>
	public bool DepthPrepass { get; set; }

	/// <summary>The most cameras rendered in one frame (the uniform ring is sized for this many).</summary>
	public int MaxCameras { get; set; } = 8;
}
