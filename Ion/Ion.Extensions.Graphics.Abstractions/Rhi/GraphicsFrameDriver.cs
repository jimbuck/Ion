using System.Numerics;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Drives the frames of an RHI device, windowed (a surface) or offscreen: acquires the frame's color and depth targets,
/// hands out clear-on-first-use attachments, clears the target when nothing rendered, presents, and captures frames.
/// Written only against the RHI, so every backend (Vulkan and GLES) shares it.
/// </summary>
public sealed class GraphicsFrameDriver : IGraphicsFrame, IScreenshotSource, IDisposable
{
	private readonly IWindowSurface? _window;
	private readonly GraphicsConfig _config;
	private readonly uint _offscreenWidth;
	private readonly uint _offscreenHeight;

	private ITexture? _offscreen;
	private ITexture? _depth;
	private IBuffer? _readback;
	private uint _readbackWidth;
	private uint _readbackHeight;
	private TextureFormat _readbackFormat;
	private bool _retained;
	private bool _hasFrame;
	private bool _colorCleared;
	private bool _depthCleared;
	private bool _reconfigure;
	private (uint Width, uint Height) _configuredFor = (uint.MaxValue, uint.MaxValue);

	/// <summary>
	/// Creates a driver for <paramref name="device"/>. With a surface, <paramref name="window"/> gives the framebuffer size
	/// the surface is configured to; without one, frames render into an offscreen <see cref="TextureFormat.Rgba8Unorm"/>
	/// target of <paramref name="offscreenWidth"/> by <paramref name="offscreenHeight"/>.
	/// </summary>
	public GraphicsFrameDriver(IGraphicsDevice device, GraphicsConfig config, IWindowSurface? window, uint offscreenWidth, uint offscreenHeight)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(config);
		Device = device;
		_config = config;
		_window = window;
		_offscreenWidth = Math.Max(1, offscreenWidth);
		_offscreenHeight = Math.Max(1, offscreenHeight);
		ClearColor = config.ClearColor.ToVector4();
		DepthFormat = config.DepthBuffer ? TextureFormat.Depth32Float : TextureFormat.Undefined;

		if (device.Surface is { } surface)
		{
			if (window is null) throw new ArgumentException("A device with a surface needs the window it presents to.", nameof(window));
			_configure(surface);
			ColorFormat = surface.Format;
		}
		else
		{
			ColorFormat = TextureFormat.Rgba8Unorm;
			_offscreen = device.CreateTexture(new TextureDescriptor(_offscreenWidth, _offscreenHeight, ColorFormat,
				TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.TextureBinding, Label: "Ion offscreen color"));
		}
	}

	/// <inheritdoc/>
	public IGraphicsDevice Device { get; }

	/// <summary>True when frames render into an offscreen target (no surface).</summary>
	public bool IsOffscreen => Device.Surface is null;

	/// <inheritdoc/>
	public bool IsRendering { get; private set; }

	/// <inheritdoc/>
	public ITexture? ColorTarget { get; private set; }

	/// <inheritdoc/>
	public ITexture? DepthTarget => IsRendering ? _depth : null;

	/// <inheritdoc/>
	public TextureFormat ColorFormat { get; private set; }

	/// <inheritdoc/>
	public TextureFormat DepthFormat { get; }

	/// <inheritdoc/>
	public uint Width => ColorTarget?.Width ?? (IsOffscreen ? _offscreenWidth : Device.Surface!.Width);

	/// <inheritdoc/>
	public uint Height => ColorTarget?.Height ?? (IsOffscreen ? _offscreenHeight : Device.Surface!.Height);

	/// <inheritdoc/>
	public Vector4 ClearColor { get; set; }

	/// <summary>The number of frames that acquired a target and were submitted.</summary>
	public long RenderedFrames { get; private set; }

	/// <inheritdoc/>
	public RenderPassColorAttachment ColorAttachment()
	{
		var target = ColorTarget ?? throw new InvalidOperationException("No frame is being rendered (IsRendering is false).");
		var clear = !_colorCleared;
		_colorCleared = true;
		return new RenderPassColorAttachment(target.DefaultView, clear ? LoadOp.Clear : LoadOp.Load, StoreOp.Store, ClearColor);
	}

	/// <inheritdoc/>
	public RenderPassDepthStencilAttachment? DepthAttachment()
	{
		if (!IsRendering || _depth is null) return null;
		var clear = !_depthCleared;
		_depthCleared = true;
		return new RenderPassDepthStencilAttachment(_depth.DefaultView, clear ? LoadOp.Clear : LoadOp.Load, StoreOp.Store);
	}

	/// <summary>
	/// Starts a frame: begins the device frame and acquires the target (reconfiguring the surface after a resize). When no
	/// target can be acquired (minimized window) <see cref="IsRendering"/> stays false.
	/// </summary>
	public void BeginFrame()
	{
		Device.BeginFrame();
		IsRendering = false;
		ColorTarget = null;
		_colorCleared = _depthCleared = false;

		if (Device.Surface is { } surface)
		{
			var size = _framebufferSize();
			if (_reconfigure || size != _configuredFor) _configure(surface);
			if (surface.Width == 0 || surface.Height == 0) return;

			var acquired = surface.GetCurrentTexture();
			if (acquired.Status == SurfaceTextureStatus.Outdated)
			{
				_configure(surface);
				if (surface.Width == 0 || surface.Height == 0) return;
				acquired = surface.GetCurrentTexture();
			}

			if (acquired.Texture is null) return;
			_reconfigure = acquired.Status == SurfaceTextureStatus.Suboptimal;
			ColorTarget = acquired.Texture;
		}
		else
		{
			ColorTarget = _offscreen;
		}

		_ensureDepth(ColorTarget!.Width, ColorTarget.Height);
		IsRendering = true;
	}

	/// <summary>
	/// Ends a frame: clears the target if no pass did, retains a copy for <see cref="Capture"/> when configured (windowed),
	/// presents and ends the device frame.
	/// </summary>
	public void EndFrame()
	{
		if (IsRendering && ColorTarget is { } target)
		{
			if (!_colorCleared)
			{
				var encoder = Device.CreateCommandEncoder("Ion clear");
				var pass = encoder.BeginRenderPass(new RenderPassDescriptor([ColorAttachment()], DepthAttachment()));
				pass.End();
				Device.Queue.Submit(encoder.Finish());
			}

			if (!IsOffscreen && _config.RetainLastFrame)
			{
				_copyToReadback(target);
				_retained = true;
			}

			_hasFrame = true;
			RenderedFrames++;
		}

		Device.Surface?.Present();
		Device.EndFrame();
		IsRendering = false;
		ColorTarget = null;
	}

	/// <inheritdoc/>
	public Screenshot Capture()
	{
		if (IsOffscreen)
		{
			if (!_hasFrame || _offscreen is null) throw new InvalidOperationException("No frame has been rendered yet.");
			_copyToReadback(_offscreen);
		}
		else if (!_config.RetainLastFrame)
		{
			throw new InvalidOperationException("Capturing a windowed frame needs Ion:Graphics:RetainLastFrame = true (the presented image is copied every frame).");
		}
		else if (!_retained)
		{
			throw new InvalidOperationException("No frame has been presented yet.");
		}

		var readback = _readback!;
		var width = (int)_readbackWidth;
		var height = (int)_readbackHeight;
		var pixels = new byte[width * height * 4];
		readback.Read(0, pixels);

		if (_readbackFormat is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb)
		{
			for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
		}

		return new Screenshot(width, height, pixels);
	}

	/// <inheritdoc/>
	public void SaveScreenshot(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		SavePng(Capture(), path);
	}

	/// <summary>Writes <paramref name="screenshot"/> to <paramref name="path"/> as a PNG file (creating the directory).</summary>
	public static void SavePng(Screenshot screenshot, string path)
	{
		ArgumentNullException.ThrowIfNull(screenshot);
		ArgumentException.ThrowIfNullOrEmpty(path);
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		using var file = File.Create(path);
		PngWriter.Write(screenshot, file);
	}

	/// <summary>Releases the offscreen, depth and readback resources.</summary>
	public void Dispose()
	{
		_offscreen?.Dispose();
		_depth?.Dispose();
		_readback?.Dispose();
		_offscreen = _depth = null;
		_readback = null;
	}

	private void _copyToReadback(ITexture source)
	{
		if (_readback is null || _readbackWidth != source.Width || _readbackHeight != source.Height)
		{
			_readback?.Dispose();
			_readback = Device.CreateBuffer(new BufferDescriptor((ulong)source.Width * source.Height * 4, BufferUsage.MapRead | BufferUsage.CopyDst, "Ion readback"));
			_readbackWidth = source.Width;
			_readbackHeight = source.Height;
		}

		_readbackFormat = source.Format;
		var encoder = Device.CreateCommandEncoder("Ion readback");
		encoder.CopyTextureToBuffer(source, TextureRegion.Whole(source), _readback, 0, source.Width * 4);
		Device.Queue.Submit(encoder.Finish());
	}

	private void _ensureDepth(uint width, uint height)
	{
		if (DepthFormat == TextureFormat.Undefined) return;
		if (_depth is not null && _depth.Width == width && _depth.Height == height) return;
		_depth?.Dispose();
		_depth = Device.CreateTexture(new TextureDescriptor(width, height, DepthFormat, TextureUsage.RenderAttachment, Label: "Ion depth"));
	}

	private void _configure(ISurface surface)
	{
		var size = _framebufferSize();
		var mode = _config.VSync ? PresentMode.Fifo : PresentMode.Mailbox;
		surface.Configure(new SurfaceConfiguration(size.Width, size.Height, mode));
		_configuredFor = size;
		ColorFormat = surface.Format;
		_reconfigure = false;
	}

	private (uint Width, uint Height) _framebufferSize()
	{
		var size = _window?.FramebufferSize ?? Vector2.Zero;
		return ((uint)Math.Max(0, size.X), (uint)Math.Max(0, size.Y));
	}
}
