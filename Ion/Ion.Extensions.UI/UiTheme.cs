using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>
/// The look and metrics of every widget: the font, colors, sizes, optional nine-slice skins and input repeat timing. Set it
/// with <see cref="Ui.Theme"/> (usually <c>UiTheme.Default with { Font = ... }</c>); it applies from the next widget call.
/// </summary>
/// <remarks>
/// Without a <see cref="Font"/> text is not drawn and is measured as <see cref="FallbackFontSize"/> pixels tall and half
/// as wide per character, so layout and the tree still work (headless tests use the null backend's font, which measures
/// the same way).
/// </remarks>
public struct UiTheme
{
	/// <summary>The font of every text (created with <c>IFontSet.CreateStyle</c>), or null.</summary>
	public IFont? Font { get; set; }

	/// <summary>The size of the stand-in metrics used when <see cref="Font"/> is null.</summary>
	public float FallbackFontSize { get; set; }

	/// <summary>The color of text.</summary>
	public Color Text { get; set; }

	/// <summary>The color of the text of disabled widgets.</summary>
	public Color TextDisabled { get; set; }

	/// <summary>The background of panels.</summary>
	public Color PanelBackground { get; set; }

	/// <summary>The background of buttons and of the toggle box.</summary>
	public Color Button { get; set; }

	/// <summary>The background of a button under the pointer.</summary>
	public Color ButtonHover { get; set; }

	/// <summary>The background of a button held down.</summary>
	public Color ButtonPressed { get; set; }

	/// <summary>The background of a disabled button.</summary>
	public Color ButtonDisabled { get; set; }

	/// <summary>The accent: a checked toggle, the filled part of a slider, the caret.</summary>
	public Color Accent { get; set; }

	/// <summary>The slider track.</summary>
	public Color Track { get; set; }

	/// <summary>The slider thumb.</summary>
	public Color Thumb { get; set; }

	/// <summary>The background of text input fields.</summary>
	public Color InputBackground { get; set; }

	/// <summary>The background of a list item under the pointer.</summary>
	public Color ListItemHover { get; set; }

	/// <summary>The background of the selected list item.</summary>
	public Color ListItemSelected { get; set; }

	/// <summary>The outline around the focused widget.</summary>
	public Color FocusOutline { get; set; }

	/// <summary>The scroll position indicator of scroll views.</summary>
	public Color ScrollBar { get; set; }

	/// <summary>The default padding of panels.</summary>
	public float PanelPadding { get; set; }

	/// <summary>The default gap between the children of containers.</summary>
	public float Gap { get; set; }

	/// <summary>The minimum height of buttons, toggles, sliders, text inputs and list items.</summary>
	public float ControlHeight { get; set; }

	/// <summary>The horizontal padding around a button's and a list item's text.</summary>
	public float ButtonPadding { get; set; }

	/// <summary>The side of a toggle's box.</summary>
	public float ToggleSize { get; set; }

	/// <summary>The minimum width of a slider's track.</summary>
	public float SliderWidth { get; set; }

	/// <summary>The width of a slider's thumb.</summary>
	public float ThumbWidth { get; set; }

	/// <summary>The width reserved for a slider's value text.</summary>
	public float ValueWidth { get; set; }

	/// <summary>The minimum width of a text input's field.</summary>
	public float InputWidth { get; set; }

	/// <summary>
	/// The width reserved for the caption of sliders and text inputs, so that a column of them lines up; 0 sizes each
	/// caption to its text.
	/// </summary>
	public float LabelWidth { get; set; }

	/// <summary>The thickness of the focus outline.</summary>
	public float FocusThickness { get; set; }

	/// <summary>The width of a scroll view's position indicator.</summary>
	public float ScrollBarWidth { get; set; }

	/// <summary>Pixels scrolled per wheel notch.</summary>
	public float ScrollSpeed { get; set; }

	/// <summary>Seconds a navigation key, D-pad direction or editing key is held before it repeats.</summary>
	public float RepeatDelay { get; set; }

	/// <summary>Seconds between repeats of a held key.</summary>
	public float RepeatInterval { get; set; }

	/// <summary>How far (0 to 1) a gamepad's left stick must be pushed to navigate.</summary>
	public float StickThreshold { get; set; }

	/// <summary>A nine-slice skin for panels (tinted with the panel color), or null for a solid rectangle.</summary>
	public UiNineSlice? PanelSkin { get; set; }

	/// <summary>A nine-slice skin for buttons (tinted with the button state color), or null for a solid rectangle.</summary>
	public UiNineSlice? ButtonSkin { get; set; }

	/// <summary>
	/// The default theme: dark panels, blue accent, amber focus outline, 36 px controls, no font (set <see cref="Font"/>).
	/// </summary>
	public static UiTheme Default => new()
	{
		FallbackFontSize = 16,
		Text = new Color(230, 232, 238, 255),
		TextDisabled = new Color(120, 124, 136, 255),
		PanelBackground = new Color(28, 31, 40, 240),
		Button = new Color(56, 62, 80, 255),
		ButtonHover = new Color(76, 84, 108, 255),
		ButtonPressed = new Color(40, 112, 196, 255),
		ButtonDisabled = new Color(42, 45, 54, 255),
		Accent = new Color(64, 156, 255, 255),
		Track = new Color(16, 18, 24, 255),
		Thumb = new Color(232, 234, 240, 255),
		InputBackground = new Color(16, 18, 24, 255),
		ListItemHover = new Color(58, 64, 82, 255),
		ListItemSelected = new Color(36, 86, 150, 255),
		FocusOutline = new Color(255, 196, 60, 255),
		ScrollBar = new Color(255, 255, 255, 90),
		PanelPadding = 16,
		Gap = 8,
		ControlHeight = 36,
		ButtonPadding = 16,
		ToggleSize = 20,
		SliderWidth = 160,
		ThumbWidth = 10,
		ValueWidth = 56,
		InputWidth = 180,
		LabelWidth = 0,
		FocusThickness = 2,
		ScrollBarWidth = 4,
		ScrollSpeed = 40,
		RepeatDelay = 0.4f,
		RepeatInterval = 0.08f,
		StickThreshold = 0.5f,
	};
}

/// <summary>
/// Settings of the UI module, given to <c>AddUi</c>.
/// </summary>
public sealed class UiOptions
{
	/// <summary>
	/// When nothing has the focus at the end of a frame (at startup, or after the focused widget disappeared), focus the
	/// first focusable widget, so a gamepad-only device (the R36S) always has something to act on. Default true.
	/// </summary>
	public bool AutoFocus { get; set; } = true;

	/// <summary>The gamepad slot that navigates, or -1 (the default) for every connected gamepad.</summary>
	public int Gamepad { get; set; } = -1;

	/// <summary>The theme the <see cref="Ui"/> starts with; <see cref="UiTheme.Default"/> when null.</summary>
	public UiTheme? Theme { get; set; }
}
