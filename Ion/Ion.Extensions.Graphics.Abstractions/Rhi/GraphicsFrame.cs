using System.Numerics;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The frame being rendered by an RHI backend: its color (and depth) target and the device. Resolve it in systems that
/// render with the RHI; during the Render stage <see cref="IsRendering"/> is true and the targets are set.
/// </summary>
/// <remarks>
/// <para>
/// The graphics system acquires the target at the start of the Render stage (a <c>Begin</c> scope at
/// <see cref="StageOrder.Graphics"/>) and submits and presents at its end. In between, systems create command encoders,
/// record passes into <see cref="ColorTarget"/> and submit them to <see cref="IGraphicsDevice.Queue"/>.
/// </para>
/// <para>
/// Clearing: <see cref="ColorAttachment"/> returns <see cref="LoadOp.Clear"/> with <see cref="ClearColor"/> the first time it
/// is called in a frame and <see cref="LoadOp.Load"/> afterwards, so the first pass clears for free. If no pass is recorded
/// the graphics system clears the target itself before presenting.
/// </para>
/// <para>
/// When the window is minimized (a zero-sized framebuffer) or the swapchain is being recreated, <see cref="IsRendering"/>
/// is false for that frame and nothing should be recorded.
/// </para>
/// </remarks>
public interface IGraphicsFrame
{
	/// <summary>The device.</summary>
	IGraphicsDevice Device { get; }

	/// <summary>True inside the Render stage when a target was acquired.</summary>
	bool IsRendering { get; }

	/// <summary>The color target of this frame (a swapchain image or the offscreen target), or null outside a frame.</summary>
	ITexture? ColorTarget { get; }

	/// <summary>The depth target of this frame, or null when the backend has none (<see cref="GraphicsConfig.DepthBuffer"/>).</summary>
	ITexture? DepthTarget { get; }

	/// <summary>The format of <see cref="ColorTarget"/> (stable across frames; use it for pipeline color targets).</summary>
	TextureFormat ColorFormat { get; }

	/// <summary>The format of <see cref="DepthTarget"/>, or <see cref="TextureFormat.Undefined"/>.</summary>
	TextureFormat DepthFormat { get; }

	/// <summary>The width of the target in pixels.</summary>
	uint Width { get; }

	/// <summary>The height of the target in pixels.</summary>
	uint Height { get; }

	/// <summary>The color the frame is cleared to (<see cref="GraphicsConfig.ClearColor"/>).</summary>
	Vector4 ClearColor { get; }

	/// <summary>The color attachment for a pass into <see cref="ColorTarget"/>: clears on the first call in a frame, loads afterwards.</summary>
	RenderPassColorAttachment ColorAttachment();

	/// <summary>The depth attachment for a pass, or null when there is no depth target: clears on the first call in a frame, loads afterwards.</summary>
	RenderPassDepthStencilAttachment? DepthAttachment();
}

/// <summary>
/// Captures rendered frames as RGBA8 pixels. Implemented by the RHI backends (headless: the offscreen target; windowed: the
/// last presented frame, when <see cref="GraphicsConfig.RetainLastFrame"/> is on).
/// </summary>
public interface IScreenshotSource
{
	/// <summary>
	/// Captures the most recently completed frame.
	/// </summary>
	/// <exception cref="InvalidOperationException">No frame has been rendered yet, or (windowed) frame retention is off.</exception>
	Screenshot Capture();

	/// <summary>Captures the most recently completed frame and writes it to <paramref name="path"/> as a PNG file.</summary>
	void SaveScreenshot(string path);
}

/// <summary>
/// One captured frame: <see cref="Width"/> by <see cref="Height"/> RGBA8 pixels, rows top to bottom, no padding.
/// </summary>
public sealed class Screenshot
{
	/// <summary>Creates a screenshot over <paramref name="rgba"/> (which must hold <c>width * height * 4</c> bytes).</summary>
	public Screenshot(int width, int height, byte[] rgba)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		ArgumentNullException.ThrowIfNull(rgba);
		if (rgba.Length != width * height * 4) throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA8, got {rgba.Length}.", nameof(rgba));
		Width = width;
		Height = height;
		Rgba = rgba;
	}

	/// <summary>The width in pixels.</summary>
	public int Width { get; }

	/// <summary>The height in pixels.</summary>
	public int Height { get; }

	/// <summary>The pixels: 4 bytes (R, G, B, A) per pixel, rows top to bottom.</summary>
	public byte[] Rgba { get; }

	/// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>), origin top-left.</summary>
	public Rgba8 GetPixel(int x, int y)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(x);
		ArgumentOutOfRangeException.ThrowIfNegative(y);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
		var i = (y * Width + x) * 4;
		return new Rgba8(Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
	}
}

/// <summary>One RGBA8 pixel.</summary>
public readonly record struct Rgba8(byte R, byte G, byte B, byte A)
{
	/// <summary>The largest per-channel difference to <paramref name="other"/>.</summary>
	public int MaxChannelDifference(Rgba8 other) =>
		Math.Max(Math.Max(Math.Abs(R - other.R), Math.Abs(G - other.G)), Math.Max(Math.Abs(B - other.B), Math.Abs(A - other.A)));

	/// <inheritdoc/>
	public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

/// <summary>
/// A window that a graphics backend can render into: its framebuffer size in pixels and its native handles. Implemented by
/// the windowing modules (<c>Ion.Extensions.Windowing.SilkNet</c>).
/// </summary>
public interface IWindowSurface
{
	/// <summary>True once the native window exists (after the window's Init step).</summary>
	bool IsCreated { get; }

	/// <summary>
	/// The framebuffer size in pixels. Differs from <see cref="IWindow.Size"/> (window coordinates) on HiDPI displays; the
	/// swapchain is sized from this.
	/// </summary>
	Vector2 FramebufferSize { get; }

	/// <summary>The native window handles (for backends that create surfaces themselves).</summary>
	NativeWindowHandles NativeHandles { get; }

	/// <summary>
	/// The windowing library's own window object, for backends built on the same library (Silk.NET: the <c>IView</c>, which
	/// is also an <c>IVkSurfaceSource</c> and an <c>IGLContextSource</c>). Null before the window is created.
	/// </summary>
	object? PlatformWindow { get; }
}

/// <summary>The kind of native window system.</summary>
public enum NativeWindowKind
{
	/// <summary>None (headless).</summary>
	None,
	/// <summary>Win32: <see cref="NativeWindowHandles.Window"/> is the HWND, <see cref="NativeWindowHandles.Display"/> the HINSTANCE.</summary>
	Win32,
	/// <summary>X11: display and window.</summary>
	X11,
	/// <summary>Wayland: display and surface.</summary>
	Wayland,
	/// <summary>Cocoa: the NSWindow.</summary>
	Cocoa,
	/// <summary>Android: the ANativeWindow.</summary>
	Android,
	/// <summary>UIKit: the UIWindow.</summary>
	UIKit,
}

/// <summary>Native window handles.</summary>
/// <param name="Kind">The window system.</param>
/// <param name="Display">The display, connection or instance handle, when the window system has one.</param>
/// <param name="Window">The window or surface handle.</param>
public readonly record struct NativeWindowHandles(NativeWindowKind Kind, nint Display, nint Window);
