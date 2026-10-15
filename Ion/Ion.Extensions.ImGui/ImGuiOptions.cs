namespace Ion.Extensions.ImGui;

/// <summary>The color preset ImGui starts with (<see cref="ImGuiOptions.Style"/>).</summary>
public enum ImGuiStyle
{
	/// <summary><c>ImGui.StyleColorsDark</c>, ImGui's default.</summary>
	Dark,
	/// <summary><c>ImGui.StyleColorsLight</c>.</summary>
	Light,
	/// <summary><c>ImGui.StyleColorsClassic</c>.</summary>
	Classic,
}

/// <summary>Options of the Dear ImGui module, bound from <c>Ion:ImGui</c>.</summary>
public sealed class ImGuiOptions
{
	/// <summary>Whether ImGui is drawn and fed input. Off, the frame still runs (calls stay valid) but nothing is drawn and no input reaches it.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Keyboard navigation (<c>ImGuiConfigFlags.NavEnableKeyboard</c>): arrows and Tab move between widgets.</summary>
	public bool KeyboardNavigation { get; set; } = true;

	/// <summary>
	/// Where ImGui saves window positions and sizes, relative to the working directory; null (the default) saves nothing,
	/// so a game does not leave an <c>imgui.ini</c> behind.
	/// </summary>
	public string? IniFile { get; set; }

	/// <summary>The color preset.</summary>
	public ImGuiStyle Style { get; set; } = ImGuiStyle.Dark;

	/// <summary>
	/// Scales every size and the fonts (for high-density screens and handhelds): <c>ImGuiStyle.ScaleAllSizes</c> and the
	/// main font scale. 1 by default.
	/// </summary>
	public float Scale { get; set; } = 1f;
}
