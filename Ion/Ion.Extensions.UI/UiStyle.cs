using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>The main axis of a container.</summary>
public enum UiDirection : byte
{
	/// <summary>Children top to bottom (the default).</summary>
	Column,
	/// <summary>Children left to right.</summary>
	Row,
}

/// <summary>How a container places its children along the main axis when they do not fill it (and nothing grows).</summary>
public enum UiJustify : byte
{
	/// <summary>At the start (the default).</summary>
	Start,
	/// <summary>Centered.</summary>
	Center,
	/// <summary>At the end.</summary>
	End,
	/// <summary>The free space between the children; the first and last touch the edges.</summary>
	SpaceBetween,
	/// <summary>The free space around the children: half a share before the first and after the last.</summary>
	SpaceAround,
}

/// <summary>How children are placed on the cross axis.</summary>
public enum UiAlign : byte
{
	/// <summary>Stretched to the container's (or the wrapped line's) cross size, within their min/max (the default).</summary>
	Stretch,
	/// <summary>At the start (left or top).</summary>
	Start,
	/// <summary>Centered.</summary>
	Center,
	/// <summary>At the end (right or bottom).</summary>
	End,
}

/// <summary>A child's own cross-axis alignment, overriding its container's <see cref="UiStyle.AlignItems"/>.</summary>
public enum UiAlignSelf : byte
{
	/// <summary>Follow the container's <see cref="UiStyle.AlignItems"/> (the default).</summary>
	Auto,
	/// <summary>Stretched.</summary>
	Stretch,
	/// <summary>At the start.</summary>
	Start,
	/// <summary>Centered.</summary>
	Center,
	/// <summary>At the end.</summary>
	End,
}

/// <summary>Distances from the four edges of a rectangle, in pixels.</summary>
/// <param name="Left">The left distance.</param>
/// <param name="Top">The top distance.</param>
/// <param name="Right">The right distance.</param>
/// <param name="Bottom">The bottom distance.</param>
public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
	/// <summary>The same distance on every edge.</summary>
	public UiThickness(float all) : this(all, all, all, all)
	{
	}

	/// <summary><paramref name="horizontal"/> left and right, <paramref name="vertical"/> top and bottom.</summary>
	public UiThickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical)
	{
	}

	/// <summary>Left plus right.</summary>
	public float Horizontal => Left + Right;

	/// <summary>Top plus bottom.</summary>
	public float Vertical => Top + Bottom;

	/// <summary>A uniform thickness.</summary>
	public static implicit operator UiThickness(float all) => new(all);
}

/// <summary>
/// The layout and look of one node: a flex-style subset (direction, fixed/min/max size, grow, padding, gap, justification,
/// alignment, wrapping) plus an optional background. <c>default</c> is an auto-sized column child: sizes 0 mean "from the
/// content", maximums 0 mean "none", and unset padding, gap and background come from the widget and <see cref="UiTheme"/>.
/// </summary>
/// <remarks>
/// Layout runs once per frame at the end of Update: sizes are measured bottom-up from the content (text, theme metrics)
/// and fixed sizes, clamped to min/max, then placed top-down: free main-axis space goes to children by <see cref="Grow"/>
/// (respecting their maximums), else is spread by <see cref="Justify"/>; the cross axis follows <see cref="AlignItems"/>
/// or <see cref="AlignSelf"/>. Children larger than their container overflow it (scroll views clip and scroll them).
/// </remarks>
public struct UiStyle
{
	/// <summary>The main axis of a container's children. Ignored by <c>Row</c> and <c>Column</c>, which set it.</summary>
	public UiDirection Direction { get; set; }

	/// <summary>A fixed width in pixels; 0 sizes from the content (or from the parent when stretched or grown).</summary>
	public float Width { get; set; }

	/// <summary>A fixed height in pixels; 0 sizes from the content (or from the parent when stretched or grown).</summary>
	public float Height { get; set; }

	/// <summary>The minimum width.</summary>
	public float MinWidth { get; set; }

	/// <summary>The minimum height.</summary>
	public float MinHeight { get; set; }

	/// <summary>The maximum width; 0 for none.</summary>
	public float MaxWidth { get; set; }

	/// <summary>The maximum height; 0 for none.</summary>
	public float MaxHeight { get; set; }

	/// <summary>The share of the container's free main-axis space this node takes (0: none).</summary>
	public float Grow { get; set; }

	/// <summary>The space between the node's edges and its children; null for the widget's default (the theme's panel padding for panels, 0 otherwise).</summary>
	public UiThickness? Padding { get; set; }

	/// <summary>The space between children (and between wrapped lines); null for <see cref="UiTheme.Gap"/>.</summary>
	public float? Gap { get; set; }

	/// <summary>The main-axis placement of the children.</summary>
	public UiJustify Justify { get; set; }

	/// <summary>The cross-axis placement of the children.</summary>
	public UiAlign AlignItems { get; set; }

	/// <summary>This node's cross-axis placement in its container.</summary>
	public UiAlignSelf AlignSelf { get; set; }

	/// <summary>
	/// Wraps the children onto several lines when they do not fit. Needs a definite main size: a fixed or maximum width for
	/// a row (height for a column) when measuring, or the size the parent gives when placing. Ignored in scroll views.
	/// </summary>
	public bool Wrap { get; set; }

	/// <summary>A background color; null for the widget's default (the theme's panel color for panels, none for rows and columns).</summary>
	public Color? Background { get; set; }
}

/// <summary>
/// A nine-slice skin: <see cref="Texture"/> cut by <see cref="Border"/> (in texels) into corners that keep their size,
/// edges that stretch along one axis and a center that stretches along both.
/// </summary>
/// <param name="Texture">The texture (premultiplied, as loaded by the 2D renderer).</param>
/// <param name="Border">The widths of the left, top, right and bottom borders in texels.</param>
/// <param name="Scale">The on-screen size of one texel of the borders.</param>
public readonly record struct UiNineSlice(ITexture2D Texture, UiThickness Border, float Scale = 1f);
