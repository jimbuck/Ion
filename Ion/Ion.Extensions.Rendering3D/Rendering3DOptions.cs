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

	/// <summary>
	/// Multisample anti-aliasing of the cameras that draw into the frame: 1 (off, the default), 2, 4 or 8 samples per
	/// pixel. More than the device supports (<c>DeviceLimits.MaxSampleCount</c>) uses the most it supports. Render
	/// target cameras are not multisampled.
	/// </summary>
	public int Msaa { get; set; } = 1;

	/// <summary>
	/// Whether the frame's cameras draw into a high dynamic range (RGBA16F) scene texture, mapped to the display by
	/// <see cref="ToneMapping"/> after <see cref="Exposure"/>. Devices that cannot render RGBA16F (some OpenGL ES ones)
	/// draw in low dynamic range instead, without tone mapping.
	/// </summary>
	public bool Hdr { get; set; }

	/// <summary>The curve that maps the HDR scene to the display (with <see cref="Hdr"/>).</summary>
	public ToneMapping ToneMapping { get; set; } = ToneMapping.Aces;

	/// <summary>The multiplier of the HDR scene before tone mapping (with <see cref="Hdr"/>).</summary>
	public float Exposure { get; set; } = 1f;

	/// <summary>Whether bright parts of the frame bleed light around them (bloom). Best with <see cref="Hdr"/>, where brightness above 1 is kept.</summary>
	public bool Bloom { get; set; }

	/// <summary>The brightness from which bloom starts (linear; 1 is full white in LDR, so lower it without <see cref="Hdr"/>).</summary>
	public float BloomThreshold { get; set; } = 1f;

	/// <summary>How gradually bloom starts below the threshold, as a fraction of it (0: a hard cut).</summary>
	public float BloomSoftKnee { get; set; } = 0.5f;

	/// <summary>How strongly the bloom is added to the frame.</summary>
	public float BloomIntensity { get; set; } = 0.6f;

	/// <summary>The levels of the bloom chain (each half the size of the last), 1 to 8: more spread the light wider.</summary>
	public int BloomLevels { get; set; } = 5;

	/// <summary>The most cameras rendered in one frame (the uniform ring is sized for this many).</summary>
	public int MaxCameras { get; set; } = 8;
}
