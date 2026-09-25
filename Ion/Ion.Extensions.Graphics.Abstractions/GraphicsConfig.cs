


namespace Ion.Extensions.Graphics;



public class GraphicsConfig
{
	public GraphicsBackend PreferredBackend { get; set; } = GraphicsBackend.Vulkan;
	public bool VSync { get; set; }
	public uint MaxFPS { get; set; }
	public GraphicsOutput Output { get; set; } = GraphicsOutput.Window;
	/// <summary>
	/// The color the back buffer is cleared to each frame. Set it from code, or from configuration through <see cref="ClearColorHex"/>.
	/// </summary>
	public Color ClearColor { get; set; } = Color.Black;

	/// <summary>
	/// <see cref="ClearColor"/> as a hex string, for configuration binding (for example <c>Ion:Graphics:ClearColorHex = "#333333"</c>).
	/// Accepts <c>RGB</c>, <c>RGBA</c>, <c>RRGGBB</c> and <c>RRGGBBAA</c>, with or without a leading <c>#</c>.
	/// Reading it returns the current <see cref="ClearColor"/> as <c>#RRGGBBAA</c>.
	/// </summary>
	public string? ClearColorHex
	{
		get => $"#{ClearColor.PackedValue:X8}";
		set
		{
			if (string.IsNullOrWhiteSpace(value)) return;
			ClearColor = ParseHexColor(value);
		}
	}
	public string? CanvasSelector { get; set; }

	private static Color ParseHexColor(string value)
	{
		var hex = value.Trim().TrimStart('#');
		if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var packed))
		{
			throw new FormatException($"'{value}' is not a valid hex color.");
		}

		// Expand the short forms (RGB, RGBA) to one byte per channel.
		static int Nibble(uint v, int shift) => (int)((v >> shift) & 0xF) * 0x11;
		static int Byte(uint v, int shift) => (int)((v >> shift) & 0xFF);

		return hex.Length switch
		{
			3 => new Color(Nibble(packed, 8), Nibble(packed, 4), Nibble(packed, 0), 255),
			4 => new Color(Nibble(packed, 12), Nibble(packed, 8), Nibble(packed, 4), Nibble(packed, 0)),
			6 => new Color(Byte(packed, 16), Byte(packed, 8), Byte(packed, 0), 255),
			8 => new Color(Byte(packed, 24), Byte(packed, 16), Byte(packed, 8), Byte(packed, 0)),
			_ => throw new FormatException($"'{value}' is not a valid hex color (expected RGB, RGBA, RRGGBB or RRGGBBAA)."),
		};
	}
}

public enum GraphicsOutput : byte
{
	/// <summary>
	/// Indicates no graphical output should be generated. Useful for servers and unit tests.
	/// </summary>
	None = 0,

	/// <summary>
	/// Indicates that graphics will be rendered to a file. Useful for simulations and automated tests.
	/// </summary>
	File,

	/// <summary>
	/// Indicates that graphics will be rendered to a window. Default value for games.
	/// </summary>
	Window,
}

public enum GraphicsBackend : byte
{
	/// <summary>
	/// Direct3D 11.
	/// </summary>
	Direct3D11,
	/// <summary>
	/// Direct3D 12.
	/// </summary>
	Direct3D12,
	/// <summary>
	/// Vulkan.
	/// </summary>
	Vulkan,
	/// <summary>
	/// OpenGL.
	/// </summary>
	OpenGL,
	/// <summary>
	/// Metal.
	/// </summary>
	Metal,
	/// <summary>
	/// OpenGL ES.
	/// </summary>
	OpenGLES,

	/// <summary>
	/// WebGPU
	/// </summary>
	WebGPU,
}