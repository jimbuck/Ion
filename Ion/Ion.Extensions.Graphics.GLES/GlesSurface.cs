using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// The GLES <see cref="ISurface"/>: the window's default framebuffer, fronted by one offscreen
/// <see cref="TextureFormat.Rgba8Unorm"/> color target per frame slot. Frames render into the slot's target (so they can
/// be sampled and read back like any texture, with row 0 at the top); <see cref="Present"/> blits it to the default
/// framebuffer with a vertical flip (GL's window origin is bottom-left) and swaps buffers.
/// </summary>
internal sealed class GlesSurface(GlesDevice device) : ISurface
{
	private GlesTexture[] _targets = [];
	private bool _acquired;
	private uint _readFramebuffer;

	public TextureFormat Format => TextureFormat.Rgba8Unorm;

	public uint Width { get; private set; }

	public uint Height { get; private set; }

	public PresentMode PresentMode { get; private set; }

	/// <summary>Called after the blit into the default framebuffer and before the swap (tests read the presented image here).</summary>
	internal Action<uint, uint>? Presenting { get; set; }

	public void Configure(in SurfaceConfiguration configuration)
	{
		device.Context.MakeCurrent();
		PresentMode = configuration.PresentMode == PresentMode.Fifo ? PresentMode.Fifo : PresentMode.Immediate;
		device.Context.SwapInterval(PresentMode == PresentMode.Fifo ? 1 : 0);
		_acquired = false;

		if (configuration.Width == Width && configuration.Height == Height && _targets.Length > 0) return;
		_destroyTargets();
		Width = configuration.Width;
		Height = configuration.Height;
		if (Width == 0 || Height == 0)
		{
			Width = Height = 0;
			return;
		}

		_targets = new GlesTexture[device.FramesInFlight];
		for (var i = 0; i < _targets.Length; i++)
		{
			_targets[i] = new GlesTexture(device, new TextureDescriptor(Width, Height, Format,
				TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding, Label: $"Ion surface {i}"));
		}
	}

	public SurfaceTexture GetCurrentTexture()
	{
		if (Width == 0 || Height == 0 || _targets.Length == 0) return new SurfaceTexture(null, SurfaceTextureStatus.Outdated);
		_acquired = true;
		return new SurfaceTexture(_targets[device.FrameIndex], SurfaceTextureStatus.Success);
	}

	public void Present()
	{
		if (!_acquired) return;
		_acquired = false;
		var gl = device.Gl;
		device.Context.MakeCurrent();

		var target = _targets[device.FrameIndex];
		if (_readFramebuffer == 0) _readFramebuffer = gl.GenFramebuffer();
		gl.BindFramebuffer(GLEnum.ReadFramebuffer, _readFramebuffer);
		gl.FramebufferTexture2D(GLEnum.ReadFramebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, target.Handle, 0);
		gl.ReadBuffer(GLEnum.ColorAttachment0);
		gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0);

		// The blit is subject to the scissor test and the color mask.
		gl.Disable(GLEnum.ScissorTest);
		gl.ColorMask(true, true, true, true);
		gl.BlitFramebuffer(0, 0, (int)Width, (int)Height, 0, (int)Height, (int)Width, 0, (uint)GLEnum.ColorBufferBit, GLEnum.Nearest);
		gl.BindFramebuffer(GLEnum.Framebuffer, 0);
		device.Executor.InvalidateFramebuffer();
		device.CheckErrors("Present");
		Presenting?.Invoke(Width, Height);

		device.Context.SwapBuffers();
	}

	public void Destroy()
	{
		_destroyTargets();
		if (_readFramebuffer != 0) device.Gl.DeleteFramebuffer(_readFramebuffer);
		_readFramebuffer = 0;
	}

	private void _destroyTargets()
	{
		foreach (var target in _targets) target.Dispose();
		_targets = [];
	}
}
