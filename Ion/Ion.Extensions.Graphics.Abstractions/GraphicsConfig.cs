


namespace Ion.Extensions.Graphics;



public class GraphicsConfig
{
	/// <summary>
	/// The graphics API to use (<c>Ion:Graphics:PreferredBackend</c>). With the Silk.NET stack, <see cref="GraphicsBackend.Vulkan"/>
	/// and <see cref="GraphicsBackend.OpenGLES"/> force that backend, and <see cref="GraphicsBackend.Auto"/> takes the first
	/// available one in platform order (<see cref="GraphicsBackendSelector.AutoOrder()"/>: Vulkan then OpenGL ES on desktop,
	/// OpenGL ES first on Linux arm64) where the backend-selecting registration (<c>AddRhiGraphics</c>, headless rendering)
	/// is used. The Veldrid backend maps <see cref="GraphicsBackend.Auto"/> to its own platform default.
	/// </summary>
	public GraphicsBackend PreferredBackend { get; set; } = GraphicsBackend.Vulkan;

	/// <summary>Wait for vertical blank when presenting (FIFO). Off: mailbox where supported, else immediate.</summary>
	public bool VSync { get; set; }
	public uint MaxFPS { get; set; }
	public GraphicsOutput Output { get; set; } = GraphicsOutput.Window;

	/// <summary>
	/// The number of frames the CPU may record ahead of the GPU in the RHI backends (2 or 3; other values are clamped).
	/// </summary>
	public int FramesInFlight { get; set; } = 2;

	/// <summary>
	/// Enables the graphics API's validation (Vulkan: <c>VK_LAYER_KHRONOS_validation</c> and a debug messenger, when
	/// installed). Null: on in Debug builds of the backend, off in Release.
	/// </summary>
	public bool? Validation { get; set; }

	/// <summary>
	/// Whether the RHI backends create a depth target with the color target (<see cref="IGraphicsFrame.DepthTarget"/>).
	/// </summary>
	public bool DepthBuffer { get; set; } = true;

	/// <summary>
	/// Windowed RHI backends: copy every presented frame to host memory so <see cref="IScreenshotSource.Capture"/> can return
	/// it. Costs a full-frame copy per frame, so it is off by default; the headless backend always supports capture.
	/// </summary>
	public bool RetainLastFrame { get; set; }

	/// <summary>
	/// The adapter to use when there are several: the first whose name contains this text (case-insensitive). Null: prefer
	/// a discrete GPU, then an integrated one, then anything else.
	/// </summary>
	public string? Adapter { get; set; }
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

	/// <summary>
	/// The platform default: for the Silk.NET stack, the first available of Vulkan then OpenGL ES (OpenGL ES first on Linux
	/// arm64, the R36S); for the Veldrid backend, its own platform default.
	/// </summary>
	Auto,
}